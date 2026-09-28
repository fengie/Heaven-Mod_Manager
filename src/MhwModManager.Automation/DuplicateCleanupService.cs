using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class DuplicateCleanupService(ManagerDatabase db, string archiveRoot)
{
    public async Task<DuplicateAnalysis> AnalyzeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var fingerprints = new Dictionary<string, List<ModDescriptor>>(StringComparer.OrdinalIgnoreCase);
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(x => !x.IsSuperseded))
        {
            var parts = new List<string>(); long size = 0;
            await using var c = await db.OpenAsync(ct); await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT path,blob_sha256,length FROM mod_files WHERE mod_id=$m ORDER BY path"; cmd.Parameters.AddWithValue("$m", mod.Id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) { parts.Add(r.GetString(0).ToLowerInvariant()+"|"+r.GetString(1)); size += r.GetInt64(2); }
            if (parts.Count == 0) continue;
            var fp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts)))).ToLowerInvariant();
            if (!fingerprints.TryGetValue(fp, out var list)) fingerprints[fp] = list = [];
            list.Add(mod); sizes[mod.Id] = size;
        }
        var groups = new List<DuplicateGroup>(); long reclaim = 0;
        foreach (var pair in fingerprints.Where(x => x.Value.Count > 1))
        {
            var ordered = pair.Value.OrderByDescending(m => m.Enabled).ThenByDescending(m => m.NexusUploadedAt).ThenByDescending(m => m.Priority).ToArray();
            var keeper = ordered[0];
            var bytes = ordered.Skip(1).Sum(m => sizes.GetValueOrDefault(m.Id));
            reclaim += bytes;
            groups.Add(new(pair.Key, ordered.Select(x=>x.Id).ToArray(), ordered.Select(x=>x.SourcePath).ToArray(), bytes, keeper.Id));
        }
        var superseded = mods.Where(x => x.IsSuperseded).Select(x => x.Id).ToArray();
        return new(groups, superseded, reclaim);
    }

    public async Task<int> ArchiveSafeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var analysis = await AnalyzeAsync(ct);
        Directory.CreateDirectory(archiveRoot);
        var mods = (await db.GetModsAsync(ct)).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var candidates = new HashSet<string>(analysis.SupersededModIds, StringComparer.OrdinalIgnoreCase);
        foreach (var group in analysis.ExactDuplicates) foreach (var id in group.ModIds.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, group.RecommendedKeeperId))) candidates.Add(id);
        var moved = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!mods.TryGetValue(id, out var mod) || mod.Enabled || !Directory.Exists(mod.SourcePath)) continue;
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var dest = Unique(Path.Combine(archiveRoot, stamp + "-" + Path.GetFileName(mod.SourcePath)));
            Directory.Move(mod.SourcePath, dest);
            try
            {
                await db.ExecuteAsync("DELETE FROM mods WHERE id=$m",new Dictionary<string,object?>{{"$m",id}},ct);
            }
            catch(Exception deleteError)
            {
                try
                {
                    await RestoreSourceWhenDeleteDidNotCommitAsync(id,mod.SourcePath,dest);
                }
                catch(Exception recoveryError)
                {
                    throw new AggregateException(
                        $"Duplicate cleanup could not persist retirement for '{id}', and the source move could not be safely reconciled. Archived data remains at '{dest}' when available.",
                        deleteError,
                        recoveryError);
                }
                throw;
            }
            moved++;
        }
        return moved;
    }

    private async Task RestoreSourceWhenDeleteDidNotCommitAsync(string modId,string sourcePath,string archivePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={modId}");
        await using var c=await db.OpenAsync(CancellationToken.None);
        await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT EXISTS(SELECT 1 FROM mods WHERE id=$m)";
        cmd.Parameters.AddWithValue("$m",modId);
        var stillPersisted=Convert.ToInt64(await cmd.ExecuteScalarAsync(CancellationToken.None),CultureInfo.InvariantCulture)!=0;
        if(!stillPersisted)return;

        if(Directory.Exists(sourcePath))
            throw new IOException($"Cannot restore archived duplicate '{modId}' because source path '{sourcePath}' already exists.");
        if(!Directory.Exists(archivePath))
            throw new IOException($"Cannot restore archived duplicate '{modId}' because archive path '{archivePath}' no longer exists.");

        Directory.Move(archivePath,sourcePath);
    }

    private static string Unique(string path) {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Directory.Exists(path)) return path; for (var i=2;;i++) { var candidate = path + " (" + i.ToString(CultureInfo.InvariantCulture) + ")"; if (!Directory.Exists(candidate)) return candidate; } }
}
