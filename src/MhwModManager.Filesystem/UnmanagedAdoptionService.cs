using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public enum UnmanagedAdoptionFaultPoint
{
    AfterPackageRootCreated,
    BeforeCopy,
    AfterCopy
}

public sealed class UnmanagedAdoptionService(ManagerDatabase db,PlannerSnapshotRepository plannerSnapshots,HashingService hashing,string modsRoot,GameProfile game,Action<UnmanagedAdoptionFaultPoint,string>? faultInjector=null)
{
    public async Task<int> CountAsync(CancellationToken ct=default)=>(await FindCandidatesAsync(ct)).Count;

    public async Task<AdoptionResult> AdoptAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        var live=game.LiveModRoot;
        if(!Directory.Exists(live))return new(false,null,0,$"The configured live mod folder does not exist: {live}");
        var candidates=await FindCandidatesAsync(ct);
        if(candidates.Count==0)return new(false,null,0,$"No unmanaged files were found in {game.ManagedDescription}.");

        var stamp=DateTimeOffset.Now.ToString("yyyy-MM-dd HHmmss",CultureInfo.InvariantCulture);
        // The random run identity makes the package root invocation-owned from the
        // moment it is created. Cleanup can therefore never delete another run's work.
        var folder=Path.Combine(modsRoot,$"Imported Manual Install {stamp} {Guid.NewGuid():N}");
        var sourceRoot=game.IsMonsterHunterWorld?Path.Combine(folder,"nativePC"):Path.Combine(folder,"GameRoot",game.ModRootRelativePath);
        try
        {
            Directory.CreateDirectory(sourceRoot);
            faultInjector?.Invoke(UnmanagedAdoptionFaultPoint.AfterPackageRootCreated,folder);
            var adopted=new List<(string path,string sha256)>(candidates.Count);
            foreach(var item in candidates)
            {
                ct.ThrowIfCancellationRequested();
                faultInjector?.Invoke(UnmanagedAdoptionFaultPoint.BeforeCopy,item.File);
                var relative=ValidateLiveCandidate(live,item.File);
                var dest=Path.Combine(sourceRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(item.File,dest,false);
                faultInjector?.Invoke(UnmanagedAdoptionFaultPoint.AfterCopy,item.File);
                // Revalidate topology after the copy and certify the exact bytes that
                // will become managed source material. Any concurrent replacement of
                // the source or one of its ancestors fails closed before DB publication.
                ValidateLiveCandidate(live,item.File);
                var copied=await hashing.HashFileAsync(dest,true,ct);
                if(!StringComparer.OrdinalIgnoreCase.Equals(copied.Sha256,item.Sha256))
                    throw new IOException($"Unmanaged file changed while it was being adopted: {relative}");
                adopted.Add((item.Key,item.Sha256));
            }
            await RecordAdoptionAsync(live,folder,adopted,ct);
            return new(true,folder,candidates.Count,$"Copied {candidates.Count} unmanaged live file(s) into a new tracked source package for {game.DisplayName}. The live game tree was not modified.");
        }
        catch
        {
            TryCleanupOwnedPackage(folder);
            throw;
        }
    }

    private static void TryCleanupOwnedPackage(string folder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"folder={folder}");
        try
        {
            if(!Directory.Exists(folder))return;
            SafeRecursiveTraversal.EnsureNoReparsePoints(folder);
            Directory.Delete(folder,true);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("ADOPTION-CLEANUP",$"Failed to remove owned adoption package '{folder}'.",ex);
        }
    }

    private string ManagedKey(string file)
    {
        var rel=Path.GetRelativePath(game.LiveModRoot,file).Replace('/','\\');
        if(game.IsMonsterHunterWorld)return PathRules.Normalize("nativePC\\"+rel);
        var prefix=string.IsNullOrWhiteSpace(game.ModRootRelativePath)?"root\\":"root\\"+game.ModRootRelativePath.Trim('\\')+"\\";
        return PathRules.Normalize(prefix+rel);
    }

    private async Task<List<Candidate>> FindCandidatesAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        var live=game.LiveModRoot;if(!Directory.Exists(live))return [];
        var snapshot=await plannerSnapshots.LoadAsync(ct);
        var managed=snapshot.CurrentManifest.Keys.Select(PathRules.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adopted=await LoadAdoptedAsync(ct);
        var files=await Task.Run(()=>SafeRecursiveTraversal.Snapshot(live,ct).Files
            .Select(f=>(File:f,Key:ManagedKey(f))).Where(x=>!managed.Contains(x.Key)).ToArray(),ct);
        var result=new List<Candidate>();
        foreach(var item in files)
        {
            ct.ThrowIfCancellationRequested();var current=await hashing.HashFileAsync(item.File,true,ct);
            if(adopted.TryGetValue(item.Key,out var known)&&Directory.Exists(known.SourceFolder)&&StringComparer.OrdinalIgnoreCase.Equals(current.Sha256,known.Sha256))continue;
            result.Add(new(item.File,item.Key,current.Sha256));
        }
        return result;
    }

    private async Task<Dictionary<string,AdoptedRecord>> LoadAdoptedAsync(CancellationToken ct)
    {
        var result=new Dictionary<string,AdoptedRecord>(StringComparer.OrdinalIgnoreCase);
        await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT path,sha256,source_folder FROM adopted_live_files";
        await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result[r.GetString(0)]=new(r.GetString(1),r.GetString(2));return result;
    }

    private async Task RecordAdoptionAsync(string live,string folder,List<(string path,string sha256)> adopted,CancellationToken ct)
    {
        var id=Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using(var run=c.CreateCommand()){run.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;run.CommandText="INSERT INTO adoption_runs(id,created_mod_id,source_root,file_count,created_at,status) VALUES($i,NULL,$r,$c,$t,'Copied')";run.Parameters.AddWithValue("$i",id);run.Parameters.AddWithValue("$r",live);run.Parameters.AddWithValue("$c",adopted.Count);run.Parameters.AddWithValue("$t",now);await run.ExecuteNonQueryAsync(ct);}
        await using(var insert=c.CreateCommand())
        {
            insert.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;insert.CommandText="INSERT INTO adopted_live_files(path,sha256,source_folder,adopted_at) VALUES($p,$s,$f,$t) ON CONFLICT(path) DO UPDATE SET sha256=excluded.sha256,source_folder=excluded.source_folder,adopted_at=excluded.adopted_at";
            var pp=insert.Parameters.Add("$p",Microsoft.Data.Sqlite.SqliteType.Text);var ps=insert.Parameters.Add("$s",Microsoft.Data.Sqlite.SqliteType.Text);var pf=insert.Parameters.Add("$f",Microsoft.Data.Sqlite.SqliteType.Text);var pt=insert.Parameters.Add("$t",Microsoft.Data.Sqlite.SqliteType.Text);
            foreach(var item in adopted){pp.Value=item.path;ps.Value=item.sha256;pf.Value=folder;pt.Value=now;await insert.ExecuteNonQueryAsync(ct);}
        }
        await tx.CommitAsync(ct);
    }

    private static string ValidateLiveCandidate(string liveRoot,string candidate)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"candidate={candidate}");
        var root=Path.GetFullPath(liveRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var file=Path.GetFullPath(candidate);
        var relative=Path.GetRelativePath(root,file);
        if(relative=="."||Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)||relative.StartsWith(".."+Path.AltDirectorySeparatorChar,StringComparison.Ordinal))
            throw new IOException($"Unmanaged adoption source escaped the configured live mod root: {candidate}");

        var current=root;
        if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
            throw new IOException($"Unmanaged adoption refuses a reparse-point live root: {current}");
        var parts=relative.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries);
        foreach(var part in parts)
        {
            current=Path.Combine(current,part);
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new IOException($"Unmanaged adoption refuses reparse-point source paths: {current}");
        }
        if(!File.Exists(file))
            throw new IOException($"Unmanaged adoption source disappeared before copy: {file}");
        return relative;
    }

    private sealed record Candidate(string File,string Key,string Sha256);
    private sealed record AdoptedRecord(string Sha256,string SourceFolder);
    public sealed record AdoptionResult(bool Created,string? SourceFolder,int FileCount,string Message);
}
