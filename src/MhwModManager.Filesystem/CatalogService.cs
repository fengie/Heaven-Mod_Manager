using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed class CatalogService(ManagerDatabase db,ModScanner scanner,string modsRoot,string? legacyModsRoot=null,
    Func<string,CancellationToken,IReadOnlyList<string>>? discoverChildren=null)
{
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
        var priority=100000;
        foreach(var dir in directories)
        {
            ct.ThrowIfCancellationRequested();
            existingByPath.TryGetValue(dir, out var tracked);
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
    }

    private Task AddLocalFolderAsync(string dir, int priority, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = Path.GetFileName(dir);
        var id = "local-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir.ToLowerInvariant())).AsSpan(0, 12)).ToLowerInvariant();
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
