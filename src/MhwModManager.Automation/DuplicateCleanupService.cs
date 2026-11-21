using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;
using MhwModManager.Storage;
using MhwModManager.Filesystem;

namespace MhwModManager.Automation;

public sealed record DuplicateArchiveOutcome(int Archived, DuplicateAnalysis Analysis);

public enum DuplicateArchiveFaultPoint
{
    AfterMoveBeforeDatabaseRetirement,
    AfterDatabaseRetirementBeforeJournalCleanup
}

public sealed class DuplicateCleanupService(
    ManagerDatabase db,
    string archiveRoot,
    Action<DuplicateArchiveFaultPoint, string>? faultInjector = null)
{
    public async Task<DuplicateAnalysis> AnalyzeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(includeShelfSources:false,includeExactCopies:true,ct);
        var activeIds = mods.Where(mod => !mod.IsSuperseded)
            .Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        var fingerprintsByMod = new Dictionary<string, string>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);

        // SQL groups rows by binary mod ID and preserves the original path collation.
        // Hash each newline-separated path|SHA part as it is read; the previous
        // implementation retained every path string and joined it into a second
        // giant string. This keeps temporary memory bounded independently of file count.
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string? currentId = null;
        var currentFiles = 0;
        await using (var c = await db.OpenAsync(ct))
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT mod_id,path,blob_sha256,length FROM mod_files ORDER BY mod_id,path";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var id = reader.GetString(0);
                if (!activeIds.Contains(id)) continue;

                if (currentId is not null && !StringComparer.Ordinal.Equals(currentId, id))
                {
                    fingerprintsByMod.Add(currentId,
                        Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant());
                    currentFiles = 0;
                    currentId = null;
                }
                currentId ??= id;
                if (currentFiles > 0) incremental.AppendData("\n"u8);
                incremental.AppendData(Encoding.UTF8.GetBytes(
                    reader.GetString(1).ToLowerInvariant() + "|" + reader.GetString(2)));
                currentFiles++;
                sizes[id] = sizes.GetValueOrDefault(id) + reader.GetInt64(3);
            }
        }
        if (currentId is not null)
            fingerprintsByMod.Add(currentId,
                Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant());

        // Preserve GetModsAsync's original mod ordering for keeper tie-breaks and
        // group presentation, independent of SQLite's ID ordering.
        var fingerprints = new Dictionary<string, List<ModDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(mod => !mod.IsSuperseded))
        {
            if (!fingerprintsByMod.TryGetValue(mod.Id, out var fp)) continue;
            if (!fingerprints.TryGetValue(fp, out var list)) fingerprints[fp] = list = [];
            list.Add(mod);
        }

        var groups = new List<DuplicateGroup>(); long reclaim = 0;
        var visible = (await db.GetModsAsync(ct)).Select(mod=>mod.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in fingerprints.Where(x => x.Value.Count > 1))
        {
            var ordered = pair.Value.OrderByDescending(m => m.Enabled).ThenByDescending(m=>visible.Contains(m.Id)).ThenByDescending(m => m.NexusUploadedAt).ThenByDescending(m => m.Priority).ToArray();
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

    public Task<int> RecoverInterruptedArchivesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new DuplicateArchiveJournalService(db, archiveRoot).RecoverAsync(ct);
    }

    private async Task<DuplicateArchiveOutcome> ArchiveSafeCoreAsync(bool includeRemainingAnalysis, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var journal = new DuplicateArchiveJournalService(db, archiveRoot);
        // Manual cleanup must also reconcile interrupted work before recomputing candidates.
        await journal.RecoverAsync(ct);
        var analysis = await AnalyzeAsync(ct);
        Directory.CreateDirectory(archiveRoot);
        var mods = (await db.GetModsAsync(includeShelfSources:false,includeExactCopies:true,ct)).ToDictionary(x => x.Id, StringComparer.Ordinal);
        var candidates = new HashSet<string>(analysis.SupersededModIds, StringComparer.Ordinal);
        var keepers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in analysis.ExactDuplicates)
            foreach (var id in group.ModIds.Where(id => id != group.RecommendedKeeperId))
            {
                candidates.Add(id);
                keepers.Add(id, group.RecommendedKeeperId);
            }
        var moved = 0;
        // Archiving an overlapping source would change a second catalog row's files.
        var sources = mods.Values.Where(m => Directory.Exists(m.SourcePath))
            .Select(m => Path.TrimEndingDirectorySeparator(Path.GetFullPath(m.SourcePath)))
            .GroupBy(source => source, StringComparer.OrdinalIgnoreCase).ToArray();
        var overlapping = sources.Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uniqueSources = sources.Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in uniqueSources)
            for (var parent = Path.GetDirectoryName(source); parent is not null; parent = Path.GetDirectoryName(parent))
                if (uniqueSources.Contains(parent))
                {
                    overlapping.Add(source);
                    overlapping.Add(parent);
                }
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!mods.TryGetValue(id, out var mod) || mod.Enabled || !Directory.Exists(mod.SourcePath) ||
                overlapping.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod.SourcePath)))) continue;
            if (keepers.TryGetValue(id, out var keeperId) &&
                (!mods.TryGetValue(keeperId, out var keeper) ||
                 !await ExactPackageCopies.SameContentsAsync(mod.SourcePath, keeper.SourcePath, ct))) continue;
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var dest = Unique(Path.Combine(archiveRoot, stamp + "-" + Path.GetFileName(mod.SourcePath)));
            // Persist and flush intent before moving any mod bytes. If the process
            // stops before SQLite retirement, startup restores the original source.
            var intent = await journal.BeginAsync(id, mod.SourcePath, dest, ct);
            Directory.Move(mod.SourcePath, dest);
            faultInjector?.Invoke(DuplicateArchiveFaultPoint.AfterMoveBeforeDatabaseRetirement, id);
            try
            {
                await db.RetireModAsync(id,ct);
            }
            catch(Exception deleteError)
            {
                try
                {
                    await RestoreSourceWhenDeleteDidNotCommitAsync(id,mod.SourcePath,dest);
                    journal.Complete(intent);
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
            // If the commit succeeded, recovery preserves the archived directory.
            faultInjector?.Invoke(DuplicateArchiveFaultPoint.AfterDatabaseRetirementBeforeJournalCleanup, id);
            journal.Complete(intent);
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
