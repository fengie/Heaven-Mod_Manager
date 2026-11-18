using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed class CatalogService(ManagerDatabase db,ModScanner scanner,string modsRoot,string? legacyModsRoot=null)
{
    public async Task RefreshFoldersAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(modsRoot);
        var current=await db.GetModCatalogSourceIdentitiesAsync(ct);
        var existingByPath=current.GroupBy(m=>m.SourcePath,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var backfills=new List<NexusArchiveMetadataBackfill>();
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
            var name=Path.GetFileName(dir);
            if(existingByPath.TryGetValue(dir,out var tracked))
            {
                if(NexusArchiveIdentity.TryParse(name,out var identity))
                {
                    foreach(var existing in tracked)
                        if(string.IsNullOrWhiteSpace(existing.NexusModId) &&
                           string.IsNullOrWhiteSpace(existing.SourceUrl) &&
                           StringComparer.OrdinalIgnoreCase.Equals(existing.Name,name))
                            backfills.Add(new(existing.Id,existing.Name,dir,identity.ModId,identity.DisplayName));
                }
                continue;
            }
            var id="local-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir.ToLowerInvariant())).AsSpan(0,12)).ToLowerInvariant();
            var parsed=NexusArchiveIdentity.TryParse(name,out var newIdentity);
            var nexusModId=parsed?newIdentity.ModId:null;
            var display=ModDisplayNames.StripTransportDigest(parsed?newIdentity.DisplayName:name);
            await db.UpsertModAsync(new(id,name,display,dir,false,priority++,NexusModId:nexusModId),ct);
        }
        if(backfills.Count>0)
        {
            var updated=await db.BackfillNexusArchiveMetadataAsync(backfills,ct);
            MasterDebugLog.Write("CATALOG", $"Backfilled Nexus archive metadata for {updated} existing mod(s).");
        }
    }

    public async Task EnsureCapturedAsync(IEnumerable<ModDescriptor> mods,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Intentionally serialize mods while each mod performs bounded file parallelism.
        // Nested parallelism would saturate the SSD and hurt UI responsiveness.
        foreach(var m in mods){ct.ThrowIfCancellationRequested();await scanner.CaptureAsync(m,ct);}
    }

}
