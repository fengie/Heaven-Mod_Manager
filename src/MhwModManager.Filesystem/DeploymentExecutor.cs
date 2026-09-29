using System.Text.Json;
using System.Globalization;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

/// <summary>
/// Executes already-planned deployments using a write-ahead journal and an explicit commit point.
/// Filesystem mutation and durable SQLite state are separated so startup recovery can always choose
/// either the complete before-state or the complete committed after-state.
/// </summary>
public sealed class DeploymentExecutor(
    ManagerDatabase db,
    BlobStore blobs,
    HashingService hashing,
    string gameRoot,
    Action<string, int>? faultInjector = null)
{
    private const int PreflightParallelism = 4;

    public string Destination(string key)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var p=PathRules.Normalize(key);
        return p.StartsWith("root\\",StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(gameRoot,p[5..])
            : Path.Combine(gameRoot,"nativePC",p[9..]);
    }

    public async Task<OperationResult> ApplyAsync(
        DeploymentPlan plan,
        string description,
        IReadOnlyDictionary<string,(bool enabled,int priority)>? targetModState=null,
        string? parentOperationId=null,
        CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return await ApplyWithMetadataAsync(plan,description,[],targetModState,parentOperationId,ct);
    }

    public async Task<OperationResult> ApplyWithMetadataAsync(
        DeploymentPlan plan,
        string description,
        IReadOnlyList<MetadataRowChange> metadataChanges,
        IReadOnlyDictionary<string,(bool enabled,int priority)>? targetModState=null,
        string? parentOperationId=null,
        CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"planId={plan.Id}; changes={plan.Changes.Count}; description={description}");
        if(plan.IsBlocked)return new(false,"Deployment is blocked by unresolved conflicts.",plan.Id,null,FailureCategory.UserActionRequired);

        await using var deploymentLease=await AcquireDeploymentLeaseAsync(ct);
        await RecoverIncompleteCoreAsync(ct);
        var beforeState=await ReadModStateAsync(ct);
        var afterState=targetModState is null
            ? beforeState
            : new Dictionary<string,(bool enabled,int priority)>(targetModState,StringComparer.OrdinalIgnoreCase);

        var prepared=await PrepareChangesAsync(plan.Changes,ct);
        var journalDurable=false;
        var filesChanged=0;
        string? activePath=null;
        Inject("before-journal",0);

        try
        {
            await WritePreparedJournalAsync(plan.Id,description,parentOperationId,beforeState,afterState,prepared,ct);
            journalDurable=true;
            Inject("after-journal",0);

            // Validate the entire plan before the first mutation. Each file is then revalidated
            // immediately before its own write to close the TOCTOU window as much as practical.
            await VerifyAllPreconditionsAsync(prepared,ct);
            VerifyNoKnownFileLocks(prepared);
            await SetOperationStateAsync(plan.Id,OperationState.Applying,ct);
            Inject("before-first-mutation",0);

            foreach(var ch in prepared)
            {
                ct.ThrowIfCancellationRequested();
                activePath=ch.Path;
                MasterDebugLog.Write("DEPLOY-FILE", $"BEGIN planId={plan.Id}; seq={ch.Sequence}; kind={ch.Kind}; path={ch.Path}; before={ch.BeforeBlobSha256 ?? "<none>"}; after={ch.AfterBlobSha256 ?? "<none>"}");
                var dest=Destination(ch.Path);
                await VerifyPreconditionAsync(ch,dest,ct);
                await SetJournalStatusAsync(plan.Id,ch.Sequence,"Writing",ct);
                EnsureNoReparseTraversal(dest);

                if(ch.Kind==ChangeKind.Remove)
                {
                    if(File.Exists(dest))File.Delete(dest);
                    PruneEmpty(Path.GetDirectoryName(dest)!,StopRootFor(ch.Path));
                }
                else if(ch.AfterBlobSha256 is not null)
                {
                    await blobs.RestoreAsync(ch.AfterBlobSha256,dest,ct);
                }

                filesChanged++;
                MasterDebugLog.Write("DEPLOY-FILE", $"PASS planId={plan.Id}; seq={ch.Sequence}; path={ch.Path}; destination={dest}");
                Inject("after-file-write",ch.Sequence);
                await SetJournalStatusAsync(plan.Id,ch.Sequence,"Applied",ct);
            }

            await SetOperationStateAsync(plan.Id,OperationState.FilesWritten,ct);
            Inject("after-files-written",0);
            await SetOperationStateAsync(plan.Id,OperationState.StateCommitting,ct);
            Inject("before-db-commit",0);

            // Manifest, ownership, enabled state, and the COMMITTED marker cross one SQLite
            // transaction boundary. If the process dies during this transaction SQLite rolls it
            // back and recovery sees StateCommitting, so it restores the filesystem before-image.
            await CommitAfterStateAsync(plan.Id,prepared,afterState,targetModState is not null,metadataChanges,ct);
            Inject("after-db-commit",0);
            Inject("before-cleanup",0);

            return new(true,$"Applied {prepared.Count} file change(s).",plan.Id,null,null,false,filesChanged);
        }
        catch(SimulatedCrashException)
        {
            // Intentionally leave durable state untouched to emulate abrupt process death.
            throw;
        }
        catch(Exception original)
        {
            var ex=EnrichWithLockOwner(original,activePath);
            if(!journalDurable)
                return new(false,"Deployment failed before any live-file transaction began.",plan.Id,ex,Classify(ex),false,filesChanged);

            try
            {
                await SetOperationStateAsync(plan.Id,OperationState.RollingBack,CancellationToken.None,ex.ToString());
                await RollbackAsync(plan.Id,CancellationToken.None);
                return new(false,"Deployment failed and rollback restored the previous managed state.",plan.Id,ex,Classify(ex),true,filesChanged);
            }
            catch(Exception rollbackError)
            {
                var aggregate=new AggregateException("Deployment failed and automatic rollback could not complete safely. Startup recovery is required.",ex,rollbackError);
                try{await SetOperationStateAsync(plan.Id,OperationState.RecoveryRequired,CancellationToken.None,aggregate.ToString());}catch{}
                return new(false,"Deployment failed and rollback requires recovery. Do not manually delete State or nativePC files; restart the manager and run Health/Diagnostics.",plan.Id,aggregate,FailureCategory.DataIntegrityFailure,false,filesChanged);
            }
        }
    }

    public async Task<OperationResult> UndoLastAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("undo-last");
        string? id=null;
        string? beforeJson=null;
        await using(var c=await db.OpenAsync(ct))
        {
            await using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT id,state_before_json FROM operations WHERE state='Committed' AND description NOT LIKE 'Undo %' AND description NOT IN ('Enter safe mode','Restore after safe mode') ORDER BY committed_at DESC LIMIT 1";
            await using var r=await cmd.ExecuteReaderAsync(ct);
            if(await r.ReadAsync(ct)){id=r.GetString(0);beforeJson=r.IsDBNull(1)?null:r.GetString(1);}
        }
        if(id is null)return new(false,"There is no committed v8 operation to undo.");

        var rows=new List<(int seq,string path,string? before,string? after,string? pb,string? pa)>();
        await using(var c=await db.OpenAsync(ct))
        {
            await using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT seq,path,before_sha256,after_sha256,provider_before,provider_after FROM operation_journal WHERE operation_id=$o ORDER BY seq DESC";
            cmd.Parameters.AddWithValue("$o",id);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))rows.Add((r.GetInt32(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5)));
        }

        var inverse=new List<DeploymentChange>(rows.Count);
        var seq=0;
        foreach(var row in rows)
        {
            ChangeKind kind;
            if(row.before is null)kind=ChangeKind.Remove;
            else if(row.pb is null)kind=ChangeKind.RestoreOriginal;
            else kind=row.after is null?ChangeKind.Add:ChangeKind.Replace;
            inverse.Add(new(++seq,kind,row.path,row.after,row.before,row.pa,row.pb,row.after,null));
        }
        var state=beforeJson is null?null:DeserializeState(beforeJson);
        var plan=new DeploymentPlan(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,inverse,[],[$"Undo committed operation {id}"]);
        await using var metadataConnection=await db.OpenAsync(ct);
        var inverseMetadata=DeploymentMetadata.Invert(await DeploymentMetadata.LoadAsync(metadataConnection,null,id,ct));
        return await ApplyWithMetadataAsync(plan,$"Undo {id}",inverseMetadata,state,id,ct);
    }

    public async Task RecoverIncompleteAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("startup-recovery");
        await using var deploymentLease=await AcquireDeploymentLeaseAsync(ct);
        await RecoverIncompleteCoreAsync(ct);
    }

    private async Task RecoverIncompleteCoreAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("startup-recovery-core");
        var ids=new List<string>();
        await using(var c=await db.OpenAsync(ct))
        {
            await using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT id FROM operations WHERE state IN ('Prepared','Applying','FilesWritten','StateCommitting','RollingBack','RecoveryRequired') ORDER BY started_at";
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))ids.Add(r.GetString(0));
        }

        foreach(var id in ids)
        {
            try{await RollbackAsync(id,ct);}
            catch(Exception ex)
            {
                try{await SetOperationStateAsync(id,OperationState.RecoveryRequired,CancellationToken.None,ex.ToString());}catch{}
                throw new IOException($"Startup recovery stopped safely for transaction {id}: {ex.Message}",ex);
            }
        }
    }

    private async Task<FileStream> AcquireDeploymentLeaseAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var lockRoot=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if(string.IsNullOrWhiteSpace(lockRoot))lockRoot=Path.GetTempPath();
        var lockDirectory=Path.Combine(lockRoot,"MhwModManager","DeploymentLocks");
        Directory.CreateDirectory(lockDirectory);
        var canonicalRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot)).ToUpperInvariant();
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalRoot)));
        var lockPath=Path.Combine(lockDirectory,$"deployment-{key}.lock");

        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.Asynchronous);
            }
            catch(IOException)
            {
                await Task.Delay(50,ct);
            }
        }
    }

    private async Task<List<DeploymentChange>> PrepareChangesAsync(IReadOnlyList<DeploymentChange> input,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var list=new List<DeploymentChange>(input.Count);
        foreach(var ch in input)
        {
            ct.ThrowIfCancellationRequested();
            var prepared=ch;
            var dest=Destination(ch.Path);
            EnsureNoReparseTraversal(dest);
            if(Directory.Exists(dest))throw new IOException($"File/directory collision: deployment target is a directory: {ch.Path}");
            if(ch.BeforeBlobSha256 is null&&File.Exists(dest))
            {
                var original=await blobs.CaptureAsync(dest,ct);
                prepared=ch with{BeforeBlobSha256=original,ExpectedLiveSha256=original};
            }
            list.Add(prepared);
        }
        return list;
    }

    private async Task VerifyAllPreconditionsAsync(List<DeploymentChange> changes,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await Parallel.ForEachAsync(changes,new ParallelOptions{MaxDegreeOfParallelism=PreflightParallelism,CancellationToken=ct},
            async(ch,token)=>await VerifyPreconditionAsync(ch,Destination(ch.Path),token));
    }

    private void VerifyNoKnownFileLocks(List<DeploymentChange> changes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!OperatingSystem.IsWindows())return;
        var paths=new List<string>(changes.Count);
        foreach(var ch in changes)
        {
            var path=Destination(ch.Path);
            EnsureNoReparseTraversal(path);
            if(File.Exists(path))paths.Add(path);
        }
        foreach(var chunk in paths.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(256))
        {
            var blockers=RestartManagerInspector.GetLockingProcesses(chunk);
            if(blockers.Count==0)continue;
            var summary=string.Join(", ",blockers.Select(x=>$"{x.ApplicationName} (PID {x.ProcessId})").Distinct(StringComparer.OrdinalIgnoreCase));
            throw new IOException($"Deployment cannot start because Windows reports one or more target files are in use by: {summary}. Close the listed process(es) and retry.");
        }
    }

    private async Task VerifyPreconditionAsync(DeploymentChange ch,string dest,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureNoReparseTraversal(dest);
        if(Directory.Exists(dest))throw new IOException($"Precondition failed: target became a directory: {ch.Path}");
        if(ch.ExpectedLiveSha256 is not null)
        {
            if(!File.Exists(dest))throw new IOException($"Precondition failed: managed file disappeared: {ch.Path}");
            var live=await hashing.HashFileAsync(dest,true,ct);
            if(!StringComparer.OrdinalIgnoreCase.Equals(live.Sha256,ch.ExpectedLiveSha256))
                throw new IOException($"Precondition failed: live file changed since planning: {ch.Path}");
        }
        else if(ch.BeforeBlobSha256 is null&&File.Exists(dest))
        {
            throw new IOException($"Precondition failed: an unmanaged file appeared after planning: {ch.Path}");
        }
    }

    private async Task WritePreparedJournalAsync(
        string operationId,string description,string? parentOperationId,
        Dictionary<string,(bool enabled,int priority)> beforeState,
        Dictionary<string,(bool enabled,int priority)> afterState,
        List<DeploymentChange> prepared,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await db.InTransactionAsync<object?>(async(c,tx,token)=>
        {
            await using(var op=c.CreateCommand())
            {
                op.Transaction=tx;
                op.CommandText="INSERT INTO operations(id,state,description,started_at,state_before_json,state_after_json,parent_operation_id) VALUES($id,'Prepared',$d,$u,$b,$a,$p)";
                op.Parameters.AddWithValue("$id",operationId);op.Parameters.AddWithValue("$d",description);op.Parameters.AddWithValue("$u",DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));op.Parameters.AddWithValue("$b",SerializeState(beforeState));op.Parameters.AddWithValue("$a",SerializeState(afterState));op.Parameters.AddWithValue("$p",(object?)parentOperationId??DBNull.Value);
                await op.ExecuteNonQueryAsync(token);
            }

            await using var journal=c.CreateCommand();
            journal.Transaction=tx;
            journal.CommandText="INSERT INTO operation_journal(operation_id,seq,path,change_kind,before_sha256,after_sha256,provider_before,provider_after,status) VALUES($o,$s,$p,$k,$b,$a,$pb,$pa,'Pending')";
            var jo=journal.Parameters.Add("$o",SqliteType.Text);var js=journal.Parameters.Add("$s",SqliteType.Integer);var jp=journal.Parameters.Add("$p",SqliteType.Text);var jk=journal.Parameters.Add("$k",SqliteType.Text);var jb=journal.Parameters.Add("$b",SqliteType.Text);var ja=journal.Parameters.Add("$a",SqliteType.Text);var jpb=journal.Parameters.Add("$pb",SqliteType.Text);var jpa=journal.Parameters.Add("$pa",SqliteType.Text);journal.Prepare();

            await using var original=c.CreateCommand();
            original.Transaction=tx;
            original.CommandText="INSERT OR REPLACE INTO original_files(path,blob_sha256,captured_at) VALUES($p,$b,$u)";
            var opath=original.Parameters.Add("$p",SqliteType.Text);var oblob=original.Parameters.Add("$b",SqliteType.Text);var ou=original.Parameters.Add("$u",SqliteType.Text);original.Prepare();
            var now=DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            foreach(var ch in prepared)
            {
                jo.Value=operationId;js.Value=ch.Sequence;jp.Value=ch.Path;jk.Value=ch.Kind.ToString();jb.Value=(object?)ch.BeforeBlobSha256??DBNull.Value;ja.Value=(object?)ch.AfterBlobSha256??DBNull.Value;jpb.Value=(object?)ch.ProviderBefore??DBNull.Value;jpa.Value=(object?)ch.ProviderAfter??DBNull.Value;
                await journal.ExecuteNonQueryAsync(token);
                if(ch.ProviderBefore is null&&ch.ProviderAfter is not null&&ch.BeforeBlobSha256 is not null)
                {
                    opath.Value=ch.Path;oblob.Value=ch.BeforeBlobSha256;ou.Value=now;
                    await original.ExecuteNonQueryAsync(token);
                }
            }
            return null;
        },ct);
    }

    private async Task CommitAfterStateAsync(string operationId,List<DeploymentChange> prepared,Dictionary<string,(bool enabled,int priority)> afterState,bool updateModState,IReadOnlyList<MetadataRowChange>? metadataChanges,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await db.InTransactionAsync<object?>(async(c,tx,token)=>
        {
            foreach(var ch in prepared)
            {
                if(ch.ProviderAfter is null)
                {
                    await ExecuteTxAsync(c,tx,"DELETE FROM deployment_manifest WHERE path=$p",new Dictionary<string,object?>{{"$p",ch.Path}},token);
                    await ExecuteTxAsync(c,tx,"DELETE FROM original_files WHERE path=$p",new Dictionary<string,object?>{{"$p",ch.Path}},token);
                }
                else if(ch.AfterBlobSha256 is not null)
                {
                    await ExecuteTxAsync(c,tx,
                        "INSERT INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at) VALUES($p,$m,$b,$b,$r,$u) ON CONFLICT(path) DO UPDATE SET provider_mod_id=excluded.provider_mod_id,blob_sha256=excluded.blob_sha256,expected_live_sha256=excluded.expected_live_sha256,rule_id=excluded.rule_id,deployed_at=excluded.deployed_at",
                        new Dictionary<string,object?>{{"$p",ch.Path},{"$m",ch.ProviderAfter},{"$b",ch.AfterBlobSha256},{"$r",ch.RuleId},{"$u",DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}},token);
                }
            }

            if(updateModState)
            {
                await using var update=c.CreateCommand();
                update.Transaction=tx;
                update.CommandText="UPDATE mods SET enabled=$e,priority=$p,updated_at=$u WHERE id=$id";
                var pe=update.Parameters.Add("$e",SqliteType.Integer);var pp=update.Parameters.Add("$p",SqliteType.Integer);var pu=update.Parameters.Add("$u",SqliteType.Text);var pi=update.Parameters.Add("$id",SqliteType.Text);update.Prepare();
                var now=DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                foreach(var(id,state) in afterState){pe.Value=state.enabled?1:0;pp.Value=state.priority;pu.Value=now;pi.Value=id;await update.ExecuteNonQueryAsync(token);}
            }

            if(metadataChanges is {Count: > 0})await DeploymentMetadata.CommitAsync(c,tx,operationId,metadataChanges,token);
            await ExecuteTxAsync(c,tx,"UPDATE operations SET state='Committed',committed_at=$u WHERE id=$id",new Dictionary<string,object?>{{"$u",DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)},{"$id",operationId}},token);
            return null;
        },ct);
    }

    private async Task RollbackAsync(string operationId,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        string? beforeState=null;
        var rows=new List<JournalRow>();
        await using(var c=await db.OpenAsync(ct))
        {
            await using(var state=c.CreateCommand()){state.CommandText="SELECT state_before_json FROM operations WHERE id=$o";state.Parameters.AddWithValue("$o",operationId);beforeState=(string?)await state.ExecuteScalarAsync(ct);}
            await using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT seq,path,before_sha256,after_sha256,provider_before,provider_after,status FROM operation_journal WHERE operation_id=$o ORDER BY seq DESC";
            cmd.Parameters.AddWithValue("$o",operationId);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))rows.Add(new(r.GetInt32(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetString(6)));
        }

        foreach(var row in rows)
        {
            if(!row.Status.Equals("RolledBack",StringComparison.OrdinalIgnoreCase))await RestoreJournalRowAsync(row,ct);
            Inject("during-rollback",row.Sequence);
            await SetJournalStatusAsync(operationId,row.Sequence,"RolledBack",ct);
        }

        var restoredState=beforeState is null?null:DeserializeState(beforeState);
        await db.InTransactionAsync<object?>(async(c,tx,token)=>
        {
            foreach(var row in rows.OrderBy(x=>x.Sequence))
            {
                if(row.ProviderBefore is null)
                {
                    await ExecuteTxAsync(c,tx,"DELETE FROM deployment_manifest WHERE path=$p",new Dictionary<string,object?>{{"$p",row.Path}},token);
                    await ExecuteTxAsync(c,tx,"DELETE FROM original_files WHERE path=$p",new Dictionary<string,object?>{{"$p",row.Path}},token);
                }
                else if(row.BeforeSha256 is not null)
                {
                    await ExecuteTxAsync(c,tx,"INSERT OR REPLACE INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at) VALUES($p,$m,$b,$b,NULL,$u)",new Dictionary<string,object?>{{"$p",row.Path},{"$m",row.ProviderBefore},{"$b",row.BeforeSha256},{"$u",DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}},token);
                    if(row.ProviderAfter is null&&row.AfterSha256 is not null)
                        await ExecuteTxAsync(c,tx,"INSERT OR REPLACE INTO original_files(path,blob_sha256,captured_at) VALUES($p,$b,$u)",new Dictionary<string,object?>{{"$p",row.Path},{"$b",row.AfterSha256},{"$u",DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}},token);
                }
            }

            if(restoredState is not null)
            {
                await using var update=c.CreateCommand();
                update.Transaction=tx;
                update.CommandText="UPDATE mods SET enabled=$e,priority=$p,updated_at=$u WHERE id=$id";
                var pe=update.Parameters.Add("$e",SqliteType.Integer);var pp=update.Parameters.Add("$p",SqliteType.Integer);var pu=update.Parameters.Add("$u",SqliteType.Text);var pi=update.Parameters.Add("$id",SqliteType.Text);update.Prepare();
                var now=DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                foreach(var(id,state) in restoredState){pe.Value=state.enabled?1:0;pp.Value=state.priority;pu.Value=now;pi.Value=id;await update.ExecuteNonQueryAsync(token);}
            }
            var committedMetadata=await DeploymentMetadata.LoadAsync(c,tx,operationId,token);
            if(committedMetadata.Count>0)await DeploymentMetadata.ApplyAsync(c,tx,DeploymentMetadata.Invert(committedMetadata),true,token);
            await ExecuteTxAsync(c,tx,"UPDATE operations SET state='RolledBack',committed_at=$u WHERE id=$id",new Dictionary<string,object?>{{"$u",DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)},{"$id",operationId}},token);
            return null;
        },ct);
    }

    private async Task RestoreJournalRowAsync(JournalRow row,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dest=Destination(row.Path);
        EnsureNoReparseTraversal(dest);
        string? liveHash=null;
        if(Directory.Exists(dest))throw new IOException($"Recovery stopped safely: '{row.Path}' is now a directory.");
        if(File.Exists(dest))liveHash=(await hashing.HashFileAsync(dest,true,ct)).Sha256;
        if(HashEquals(liveHash,row.BeforeSha256)||(liveHash is null&&row.BeforeSha256 is null))return;

        var looksApplied=HashEquals(liveHash,row.AfterSha256)||(liveHash is null&&row.AfterSha256 is null&&row.BeforeSha256 is not null);
        if(!looksApplied)throw new IOException($"Recovery stopped safely: '{row.Path}' matches neither the transaction's before nor after image. The file may have been changed externally.");

        EnsureNoReparseTraversal(dest);
        if(row.BeforeSha256 is null)
        {
            if(File.Exists(dest))File.Delete(dest);
            PruneEmpty(Path.GetDirectoryName(dest)!,StopRootFor(row.Path));
        }
        else await blobs.RestoreAsync(row.BeforeSha256,dest,ct);
    }

    private async Task SetJournalStatusAsync(string operationId,int sequence,string status,CancellationToken ct)=>
        await db.ExecuteAsync("UPDATE operation_journal SET status=$st WHERE operation_id=$o AND seq=$s",new Dictionary<string,object?>{{"$st",status},{"$o",operationId},{"$s",sequence}},ct);

    private async Task SetOperationStateAsync(string operationId,OperationState state,CancellationToken ct,string? error=null)=>
        await db.ExecuteAsync("UPDATE operations SET state=$st,error=COALESCE($e,error) WHERE id=$id",new Dictionary<string,object?>{{"$st",state.ToString()},{"$e",error},{"$id",operationId}},ct);

    private static async Task ExecuteTxAsync(SqliteConnection c,SqliteTransaction tx,string sql,Dictionary<string,object?> args,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var kv in args)cmd.Parameters.AddWithValue(kv.Key,kv.Value??DBNull.Value);await cmd.ExecuteNonQueryAsync(ct);
    }

    private void EnsureNoReparseTraversal(string destination)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        var full=Path.GetFullPath(destination);
        var prefix=root.EndsWith(Path.DirectorySeparatorChar)?root:root+Path.DirectorySeparatorChar;
        if(!PathRules.Comparer.Equals(full,root)&&!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Physical containment failed: deployment target escaped the configured game root: {destination}");

        var relative=Path.GetRelativePath(root,full);
        if(PathRules.Comparer.Equals(relative,"."))return;

        var current=root;
        foreach(var segment in relative.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries))
        {
            current=Path.Combine(current,segment);
            try
            {
                var attributes=File.GetAttributes(current);
                if((attributes&FileAttributes.ReparsePoint)!=0)
                    throw new IOException($"Physical containment failed: deployment path traverses a reparse point: {current}");
            }
            catch(FileNotFoundException){break;}
            catch(DirectoryNotFoundException){break;}
        }
    }

    private string StopRootFor(string key)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return PathRules.Normalize(key).StartsWith("root\\",StringComparison.OrdinalIgnoreCase)?gameRoot:Path.Combine(gameRoot,"nativePC");
    }
    private static bool HashEquals(string? left,string? right)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return left is not null&&right is not null&&StringComparer.OrdinalIgnoreCase.Equals(left,right);
    }

    private void Inject(string stage,int sequence)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(faultInjector is null)return;
        try{faultInjector(stage,sequence);}catch(SimulatedCrashException){throw;}
    }

    private async Task<Dictionary<string,(bool enabled,int priority)>> ReadModStateAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods=await db.GetModsAsync(ct);
        return mods.ToDictionary(m=>m.Id,m=>(m.Enabled,m.Priority),StringComparer.OrdinalIgnoreCase);
    }

    private static string SerializeState(Dictionary<string,(bool enabled,int priority)> state)=>
        JsonSerializer.Serialize(state.ToDictionary(k=>k.Key,v=>new StateDto(v.Value.enabled,v.Value.priority),StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string,(bool enabled,int priority)> DeserializeState(string json)=>
        (JsonSerializer.Deserialize<Dictionary<string,StateDto>>(json)??new Dictionary<string,StateDto>())
        .ToDictionary(k=>k.Key,v=>(v.Value.Enabled,v.Value.Priority),StringComparer.OrdinalIgnoreCase);

    private static FailureCategory Classify(Exception ex)=>ex switch
    {
        OperationCanceledException=>FailureCategory.ExpectedTransient,
        UnauthorizedAccessException=>FailureCategory.UserActionRequired,
        InvalidDataException=>FailureCategory.DataIntegrityFailure,
        IOException=>FailureCategory.RecoverableOperationFailure,
        _=>FailureCategory.RecoverableOperationFailure
    };

    private Exception EnrichWithLockOwner(Exception ex,string? path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(path is null||ex is not(IOException or UnauthorizedAccessException))return ex;
        try
        {
            var dest=Destination(path);
            EnsureNoReparseTraversal(dest);
            var blockers=RestartManagerInspector.GetLockingProcesses([dest]);
            if(blockers.Count==0)return ex;
            var summary=string.Join(", ",blockers.Select(x=>$"{x.ApplicationName} (PID {x.ProcessId})"));
            return new IOException($"{ex.Message} Windows reports the target is in use by: {summary}",ex);
        }
        catch{return ex;}
    }

    private sealed record StateDto(bool Enabled,int Priority);
    private sealed record JournalRow(int Sequence,string Path,string? BeforeSha256,string? AfterSha256,string? ProviderBefore,string? ProviderAfter,string Status);

    private void PruneEmpty(string dir,string stopRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stop=Path.GetFullPath(stopRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var current=Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        while(!PathRules.Comparer.Equals(current,stop)&&current.StartsWith(stop+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
        {
            EnsureNoReparseTraversal(current);
            if(!Directory.Exists(current)||Directory.EnumerateFileSystemEntries(current).Any())break;
            var parent=Directory.GetParent(current)?.FullName;
            Directory.Delete(current);
            if(parent is null)break;
            current=parent.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        }
    }
}

/// <summary>Test-only exception used to emulate abrupt process death without in-process rollback.</summary>
public sealed class SimulatedCrashException(string message):Exception(message);
