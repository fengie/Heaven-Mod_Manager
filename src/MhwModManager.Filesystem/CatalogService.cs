using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification="The asynchronous gate never allocates a wait handle and lives for the catalog session.")]
public sealed partial class CatalogService(ManagerDatabase db,ModScanner scanner,string modsRoot,string? legacyModsRoot=null,
    Func<string,CancellationToken,IReadOnlyList<string>>? discoverChildren=null,
    Func<string,CancellationToken,ManagedCollectionLayout>? inspectShelf=null)
{
    private readonly ExactPackageCopies exactCopies = new(db);
    private readonly SemaphoreSlim duplicateRefresh = new(1,1);
    public long LastDuplicateBytesHashed
    {
        get
        {
            using var trace = MasterDebugLog.BeginMethod();
            return exactCopies.LastBytesHashed;
        }
    }

    /// <summary>Remove one transport wrapper while retaining the registered game's deployment root.</summary>
    public void NormalizeStagedArchive(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var folder = new DirectoryInfo(root);
        var children = folder.GetFileSystemInfos();
        if (children.Length != 1 || children[0] is not DirectoryInfo child || scanner.IsArchiveTargetRoot(child.Name))
            return;

        foreach (var entry in child.GetFileSystemInfos())
        {
            var destination = Path.Combine(root, entry.Name);
            if (entry is DirectoryInfo directory) directory.MoveTo(destination);
            else ((FileInfo)entry).MoveTo(destination);
        }
        child.Delete();
    }

    public async Task RefreshFoldersAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(modsRoot);
        var current=await db.GetModCatalogSourceIdentitiesAsync(ct);
        var existingByPath=current.GroupBy(m=>m.SourcePath,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var backfills=new List<ModArchiveDisplayBackfill>();
        // Existing MHW and generic source paths stay authoritative: a migration that moves them
        // would invalidate persisted mod IDs, explicit overrides and rollback references.
        var roots=new[]{modsRoot,legacyModsRoot}
            .Where(path=>!string.IsNullOrWhiteSpace(path)&&Directory.Exists(path))
            .Select(path=>Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var directories = await Task.Run(() => roots
            .SelectMany(root=>Directory.EnumerateDirectories(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray(), ct);
        var verifiedFamilyRoots = new HashSet<string>(PathRules.Comparer);
        await using (var connection = await db.OpenAsync(ct))
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText="SELECT DISTINCT package_root FROM authored_component_sources WHERE verified=1";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) verifiedFamilyRoots.Add(reader.GetString(0));
        }
        var savedShelves = inspectShelf is null ? null : await db.ReadShelfEntriesAsync(ct);
        var priority=100000;
        foreach(var dir in directories)
        {
            ct.ThrowIfCancellationRequested();
            existingByPath.TryGetValue(dir, out var tracked);
            try
            {
                if (await DiscoverFamilyAsync(dir,priority,ct)) continue;
                if (verifiedFamilyRoots.Contains(dir))
                {
                    await using var connection=await db.OpenAsync(ct);
                    await using var cmd=connection.CreateCommand();
                    cmd.CommandText="UPDATE authored_component_sources SET verified=0 WHERE package_root=$dir";
                    cmd.Parameters.AddWithValue("$dir",dir);
                    await cmd.ExecuteNonQueryAsync(ct);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MasterDebugLog.Write("CATALOG","Incomplete source inspection; preserving saved entries.",ex);
                continue;
            }
            if (inspectShelf is not null && (tracked is null || tracked.All(row => row.CanInspectCollection && !row.HasSavedReferences)))
            {
                try
                {
                    var layout = await Task.Run(() => inspectShelf(dir, ct), ct);
                    if (layout.FullyAccounted && layout.Units.Count > 0)
                    {
                        await ReconcileShelfAsync(dir, layout.Units, tracked ?? [], savedShelves![dir], priority, ct);
                        priority += layout.Units.Count;
                        continue;
                    }
                    if (savedShelves!.Contains(dir)) await db.SetShelfEntriesAsync(dir, [], ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    MasterDebugLog.Write("CATALOG", "Keeping the previous catalog view after incomplete inspection.", ex);
                    if (tracked is not null || savedShelves!.Contains(dir)) continue;
                }
            }
            else if (savedShelves?.Contains(dir) == true && tracked is not null)
                await db.SetShelfEntriesAsync(dir, [], ct);
            if (discoverChildren is not null && (tracked is null || tracked.All(row => row.CanInspectCollection)))
            {
                IReadOnlyList<string> children = [];
                try
                {
                    children = await Task.Run(() => discoverChildren(dir, ct), ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    MasterDebugLog.Write("CATALOG", "Collection could not be inspected; keeping original catalog source.", ex);
                }
                foreach (var child in children)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!existingByPath.ContainsKey(child))
                        await AddLocalFolderAsync(child, priority++, ct);
                }
                if (children.Count > 0 && tracked is null) continue;
            }
            var name=Path.GetFileName(dir);
            if(tracked is not null)
            {
                if(NexusArchiveIdentity.TryParse(name,out var identity))
                {
                    foreach(var existing in tracked)
                        if(string.IsNullOrWhiteSpace(existing.NexusModId) &&
                           string.IsNullOrWhiteSpace(existing.SourceUrl) &&
                           StringComparer.OrdinalIgnoreCase.Equals(existing.Name,name))
                            backfills.Add(new(existing.Id,existing.Name,dir,identity.DisplayName));
                }
                continue;
            }
            if (IsEmptyLocalFolder(dir)) continue;
            await AddLocalFolderAsync(dir, priority++, ct);
        }
        if(backfills.Count>0)
        {
            var updated=await db.BackfillArchiveDisplayNameAsync(backfills,ct);
            MasterDebugLog.Write("CATALOG", $"Repaired filename-derived display titles for {updated} existing mod(s).");
        }
        await duplicateRefresh.WaitAsync(ct);
        try
        {
            var shelves = await db.ReadShelfEntriesAsync(ct);
            var all = await db.GetModsAsync(true,ct);
            var sources = all.ToDictionary(mod => mod.Id, mod => mod.SourcePath, StringComparer.Ordinal);
            // A parent representing loose files overlaps its independently indexed
            // children. Only whole, independent review sources can be exact peers.
            var excluded = shelves.SelectMany(group => group.Where(entry => entry.Role == "wrapper" ||
                    (entry.Role == "review" && group.Skip(1).Any() &&
                     sources.TryGetValue(entry.Id, out var source) && PathRules.Comparer.Equals(source, group.Key))))
                .Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
            var eligible = all.Where(mod=>!mod.IsSuperseded && !excluded.Contains(mod.Id)).ToArray();
            var groups = await Task.Run(()=>exactCopies.FindAsync(eligible,ct),ct);
            await db.ReconcileExactCopiesAsync(groups,ct);
        }
        finally { duplicateRefresh.Release(); }
    }

    private async Task ReconcileShelfAsync(string shelf, IReadOnlyList<ManagedCollectionUnit> units,
        IReadOnlyList<ModCatalogSourceIdentity> wrappers, IEnumerable<(string Id,string Role,string? Canonical)> previous,
        int priority, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var all = await db.GetModsAsync(true, ct);
        var byPath = all.ToLookup(mod => mod.SourcePath, StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units.Where(unit => !byPath.Contains(unit.SourcePath)))
            await AddLocalFolderAsync(unit.SourcePath, priority++, ct);
        if (units.Any(unit => !byPath.Contains(unit.SourcePath)))
        {
            all = await db.GetModsAsync(true, ct);
            byPath = all.ToLookup(mod => mod.SourcePath, StringComparer.OrdinalIgnoreCase);
        }
        var files = (await db.GetModFilesAsync(ct)).ToLookup(file => file.ModId);
        var enabled = all.Where(mod => mod.Enabled && files.Contains(mod.Id) && Directory.Exists(mod.SourcePath) &&
            (File.GetAttributes(mod.SourcePath) & FileAttributes.ReparsePoint) == 0).ToArray();
        var entries = new List<(string Id, string Role, string? Canonical)>();
        foreach (var unit in units)
        {
            if (unit.NeedsReview && !PathRules.Comparer.Equals(unit.SourcePath,shelf) &&
                await DiscoverFamilyAsync(unit.SourcePath,priority,ct))
            {
                foreach(var wrapper in byPath[unit.SourcePath].Where(mod=>!mod.Enabled))
                    entries.Add((wrapper.Id,"wrapper",null));
                continue;
            }
            foreach (var mod in byPath[unit.SourcePath])
            {
                if (mod.Enabled) continue;
                if (unit.NeedsReview)
                {
                    entries.Add((mod.Id, "review", null));
                    continue;
                }
                var indexed = files[mod.Id].ToArray();
                if (indexed.Length == 0 || !await Task.Run(() => scanner.ShelfIndexMatchesSource(mod, indexed, ct), ct))
                    indexed = (await scanner.CaptureShelfPackageAsync(mod, ct)).ToArray();
                var keys = indexed.ToDictionary(file => file.Path, file => file.BlobSha256, PathRules.Comparer);
                var canonical = enabled.FirstOrDefault(active =>
                    files[active.Id].Count() == keys.Count && files[active.Id].All(file =>
                        keys.TryGetValue(file.Path, out var digest) && digest == file.BlobSha256));
                entries.Add((mod.Id, canonical is null ? "package" : "duplicate", canonical?.Id));
            }
        }
        // When the shelf itself owns unresolved loose files it already has a visible
        // review role. Reuse its original identity and keep its deployment toggle guarded.
        if (!units.Any(unit => StringComparer.OrdinalIgnoreCase.Equals(unit.SourcePath, shelf)))
            entries.AddRange(wrappers.Select(wrapper => (wrapper.Id, "wrapper", (string?)null)));
        if (!previous.ToHashSet().SetEquals(entries))
            await db.SetShelfEntriesAsync(shelf, entries, ct);
    }

    private Task AddLocalFolderAsync(string dir, int priority, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = Path.GetFileName(dir);
        var id = LocalId(dir);
        var display = ModDisplayNames.StripTransportDigest(NexusArchiveIdentity.TryParse(name, out var identity) ? identity.DisplayName : name);
        return db.InsertDiscoveredModAsync(id, name, display, dir, priority, ct);
    }

    public async Task EnsureCapturedAsync(IEnumerable<ModDescriptor> mods,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var pending = mods.ToArray();
        // Keep saved rows intact and reject empty sources before capturing any mod in this batch.
        foreach (var mod in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (IsEmptyLocalFolder(mod.SourcePath))
                throw new InvalidDataException($"'{mod.DisplayName}' has an empty source folder. Deselect it or restore its package files, then try again.");
        }

        // Intentionally serialize mods while each mod performs bounded file parallelism.
        // Nested parallelism would saturate the SSD and hurt UI responsiveness.
        foreach(var m in pending){ct.ThrowIfCancellationRequested();await scanner.CaptureAsync(m,ct);}
    }

    private static bool IsEmptyLocalFolder(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var folder = new DirectoryInfo(path);
        return folder.Exists && (folder.Attributes & FileAttributes.ReparsePoint) == 0 &&
            !folder.EnumerateFileSystemInfos().Any();
    }
}
