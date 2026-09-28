using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed class UnmanagedAdoptionService(ManagerDatabase db,PlannerSnapshotRepository plannerSnapshots,HashingService hashing,string modsRoot,GameProfile game)
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
        var folder=UniqueDirectory(Path.Combine(modsRoot,$"Imported Manual Install {stamp}"));
        var sourceRoot=game.IsMonsterHunterWorld?Path.Combine(folder,"nativePC"):Path.Combine(folder,"GameRoot",game.ModRootRelativePath);
        Directory.CreateDirectory(sourceRoot);
        try
        {
            var adopted=new List<(string path,string sha256)>(candidates.Count);
            foreach(var item in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var relative=Path.GetRelativePath(live,item.File);var dest=Path.Combine(sourceRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(item.File,dest,false);adopted.Add((item.Key,item.Sha256));
            }
            await RecordAdoptionAsync(live,folder,adopted,ct);
            return new(true,folder,candidates.Count,$"Copied {candidates.Count} unmanaged live file(s) into a new tracked source package for {game.DisplayName}. The live game tree was not modified.");
        }
        catch
        {
            try{if(Directory.Exists(folder))Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
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

    private static string UniqueDirectory(string path){if(!Directory.Exists(path))return path;for(var i=2;;i++){var p=$"{path} ({i})";if(!Directory.Exists(p))return p;}}
    private sealed record Candidate(string File,string Key,string Sha256);
    private sealed record AdoptedRecord(string Sha256,string SourceFolder);
    public sealed record AdoptionResult(bool Created,string? SourceFolder,int FileCount,string Message);
}
