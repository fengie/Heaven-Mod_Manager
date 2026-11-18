using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed record DuplicateArchiveOutcome(int Archived, DuplicateAnalysis Analysis);

public sealed class DuplicateCleanupService(ManagerDatabase db, string archiveRoot)
{
    public async Task<DuplicateAnalysis> AnalyzeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var active = mods.Where(mod => !mod.IsSuperseded)
            .ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var partsByMod = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Preserve SQLite's original ORDER BY path hashing semantics, but stream all
        // mod-file rows through one indexed read instead of N connections/queries.
        await using (var c = await db.OpenAsync(ct))
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT mod_id,path,blob_sha256,length FROM mod_files ORDER BY mod_id,path";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var id = reader.GetString(0);
                if (!active.ContainsKey(id)) continue;
                if (!partsByMod.TryGetValue(id, out var parts))
                    partsByMod[id] = parts = [];
                parts.Add(reader.GetString(1).ToLowerInvariant() + "|" + reader.GetString(2));
                sizes[id] = sizes.GetValueOrDefault(id) + reader.GetInt64(3);
            }
        }

        var fingerprints = new Dictionary<string, List<ModDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(mod => !mod.IsSuperseded))
        {
            if (!partsByMod.TryGetValue(mod.Id, out var parts) || parts.Count == 0) continue;
            var fp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts)))).ToLowerInvariant();
            if (!fingerprints.TryGetValue(fp, out var list)) fingerprints[fp] = list = [];
            list.Add(mod);
        }
        var groups = new List<DuplicateGroup>(); long reclaim = 0;
        foreach (var pair in fingerprints.Where(x => x.Value.Count > 1))
        {
            var ordered = pair.Value.OrderByDescending(m => m.Enabled).ThenByDescending(m => m.NexusUploadedAt).ThenByDescending(m => m.Priority).ToArray();
            var keeper = ordered[0];
            var bytes = ordered.Skip(1).Sum(m => sizes.GetValueOrDefault(m.Id));
            reclaim += bytes;
            groups.Add(new(pair.Key, ordered.Select(x => x.Id).ToArray(), ordered.Select(x => x.SourcePath).ToArray(), bytes, keeper.Id));
        }
        var superseded = mods.Where(x => x.IsSuperseded).Select(x => x.Id).ToArray();
        return new(groups, superseded, reclaim);
    }

    public async Task<int> ArchiveSafeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Normal cleanup callers need only the count: preserve their original
        // single-analysis cost even when a package actually moves.
        return (await ArchiveSafeCoreAsync(includeRemainingAnalysis: false, ct)).Archived;
    }

    /// <summary>
    /// Return the already computed duplicate analysis when no package was retired.
    /// Re-query only after a real retirement changed the durable catalog.
    /// </summary>
    public async Task<DuplicateArchiveOutcome> ArchiveSafeWithAnalysisAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return await ArchiveSafeCoreAsync(includeRemainingAnalysis: true, ct);
    }

    private async Task<DuplicateArchiveOutcome> ArchiveSafeCoreAsync(bool includeRemainingAnalysis, CancellationToken ct)
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
                await db.RetireModAsync(id,ct);
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
        return new DuplicateArchiveOutcome(moved,
            includeRemainingAnalysis && moved > 0 ? await AnalyzeAsync(ct) : analysis);
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
