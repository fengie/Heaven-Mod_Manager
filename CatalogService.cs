using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed partial class CatalogService(ManagerDatabase db,ModScanner scanner,string modsRoot)
{
    public async Task RefreshFoldersAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(modsRoot);
        var current=(await db.GetModsAsync(ct)).ToDictionary(x=>x.SourcePath,StringComparer.OrdinalIgnoreCase);
        var directories = await Task.Run(() => Directory.EnumerateDirectories(modsRoot).Order(StringComparer.OrdinalIgnoreCase).ToArray(), ct);
        var priority=100000;
        foreach(var dir in directories)
        {
            ct.ThrowIfCancellationRequested();
            if(current.ContainsKey(dir)) continue;
            var name=Path.GetFileName(dir);
            var id="local-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir.ToLowerInvariant())).AsSpan(0,12)).ToLowerInvariant();
            var nexusMatch=NexusArchiveSuffixRegex().Match(name);
            var nexusModId=nexusMatch.Success?nexusMatch.Groups[1].Value:null;
            var display=NexusArchiveSuffixRegex().Replace(name,string.Empty).Trim();
            await db.UpsertModAsync(new(id,name,display,dir,false,priority++,NexusModId:nexusModId),ct);
        }
    }

    public async Task EnsureCapturedAsync(IEnumerable<ModDescriptor> mods,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Intentionally serialize mods while each mod performs bounded file parallelism.
        // Nested parallelism would saturate the SSD and hurt UI responsiveness.
        foreach(var m in mods){ct.ThrowIfCancellationRequested();await scanner.CaptureAsync(m,ct);}
    }

    [GeneratedRegex(@"-(\d{2,7})-\d+(?:-\d+){0,6}(?:\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NexusArchiveSuffixRegex();
}
