using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public static partial class LocalModInventory
{
    private static readonly string[] UnrealExtensions = [".pak",".utoc",".ucas",".sig"];

    public static IReadOnlyList<ExternalLibraryReference> InstalledPackages(IReadOnlyList<LocalModLocation> inventory,
        GameProfile game, IReadOnlySet<string> represented, CancellationToken ct = default)
    {
        using var trace = MasterDebugLog.BeginMethod();
        var rows = new List<ExternalLibraryReference>();
        var candidates = inventory.Where(e => e.RegisteredProfileId == game.Id || e.PreviousProfileId == game.Id);
        foreach (var install in candidates.Where(e => e.InstallationRoot != null).GroupBy(e => e.InstallationRoot!, PathRules.Comparer))
        {
            var root = install.Key;
            var previous = !PathRules.Comparer.Equals(root,game.GameRoot);
            var prefix = previous ? "Previous installation · " : "";
            var files = install.ToArray();
            var manifests = new Dictionary<string, string>(PathRules.Comparer);
            foreach (var entry in files.Where(e => e.FileEvidence == InstalledEvidenceKind.ModManifest))
            {
                var title = ReadPackageName(entry.Location);
                if (title != null) manifests[Path.GetDirectoryName(entry.Location)!] = title;
            }
            var groups = new Dictionary<string,(string Title,string Owner,HashSet<string> Paths)>(PathRules.Comparer);
            foreach (var entry in files)
            {
                ct.ThrowIfCancellationRequested();
                var path = entry.Location;
                var dir = Path.GetDirectoryName(path)!;
                var relative = Path.GetRelativePath(root,path);
                var name = Path.GetFileNameWithoutExtension(path);
                string key, title, owner;
                IEnumerable<string> members = [path];
                switch (entry.FileEvidence)
                {
                    case InstalledEvidenceKind.UnrealContainer:
                    case InstalledEvidenceKind.UnrealSource:
                        members = UnrealExtensions.Select(ext => Path.ChangeExtension(path,ext))
                            .Where(file => File.Exists(file) && !HasLinkedAncestor(file)).ToArray();
                        if (!UnrealPakLayout.Complete(members,true)) continue;
                        key = Path.ChangeExtension(relative,null); title = name;
                        var mounted = entry.FileEvidence == InstalledEvidenceKind.UnrealContainer &&
                            (string.IsNullOrEmpty(game.ModRootRelativePath) ||
                             path.StartsWith(Path.TrimEndingDirectorySeparator(Path.Combine(root,game.ModRootRelativePath)) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                        owner = mounted ? "Unreal container" : "Unreal package · source location";
                        break;
                    case InstalledEvidenceKind.ReframeworkScript:
                        key = relative; title = name; owner = "REFramework script"; break;
                    case InstalledEvidenceKind.Ue4ssEntrypoint:
                        var package = Directory.GetParent(dir)!;
                        key = Path.GetRelativePath(root,package.FullName); title = package.Name; owner = "UE4SS package"; break;
                    case InstalledEvidenceKind.DlssReceipt:
                        var runtimeDir = VerifiedDlssRuntime(path,root,ct);
                        if (runtimeDir == null) continue;
                        key = "DLSS-Nexus:" + Path.GetRelativePath(root,runtimeDir); title = "DLSS Nexus";
                        owner = "Installed files present · component hash confirmed · " + Path.GetRelativePath(root,runtimeDir);
                        break;
                    case InstalledEvidenceKind.LoaderPair:
                        var runtime = Directory.GetParent(dir)!.FullName;
                        key = relative; title = "Bitfix loader"; owner = PathRules.Comparer.Equals(runtime,Path.GetDirectoryName(Path.Combine(root,game.ExecutableRelativePath)))
                            ? "Loader pair · runtime unverified" : "Loader pair · source location";
                        var dll = Path.Combine(runtime,"dsound.dll");
                        if (!File.Exists(dll) || HasLinkedAncestor(dll)) continue;
                        members = [path,dll]; break;
                    case InstalledEvidenceKind.ModManifest:
                    case InstalledEvidenceKind.ModPayload:
                        var manifest = dir;
                        while (manifest != null && !manifests.ContainsKey(manifest) && !PathRules.Comparer.Equals(root,manifest))
                            manifest = Path.GetDirectoryName(manifest);
                        if (manifest != null && manifests.TryGetValue(manifest,out var label))
                        { key = Path.GetRelativePath(root,manifest); title = label; owner = "Local mod manifest"; }
                        else if (entry.FileEvidence == InstalledEvidenceKind.ModPayload && PathRules.Comparer.Equals(dir,Path.Combine(root,"Mods")))
                        { key = relative; title = name; owner = "Loose mod payload · runtime unverified"; }
                        else continue;
                        break;
                    default: continue;
                }
                var remaining = members.Where(member => !represented.Contains(member)).ToArray();
                if (remaining.Length == 0) continue;
                if (remaining.Length != members.Count()) owner += " · partially managed";
                members = remaining;
                if (!groups.TryGetValue(key,out var group))
                    groups[key] = group = (title,prefix + owner,new HashSet<string>(PathRules.Comparer));
                group.Paths.UnionWith(members);
            }
            foreach (var (key,group) in groups)
            {
                var identity = "installed:" + game.Id + ":" + (previous ? "previous:" + root.ToUpperInvariant() + ":" : "") + key.Replace('\\','/').ToUpperInvariant();
                rows.Add(new(identity,group.Title,group.Owner,group.Paths.OrderBy(p=>p,PathRules.Comparer).ToArray()));
            }
        }
        return rows.OrderBy(row=>row.DisplayName,PathRules.Comparer).ThenBy(row=>row.ItemId,PathRules.Comparer).ToArray();
    }

    internal static string? ManagedLoaderPairOwner(LocalModLocation entry, IReadOnlyList<ModDescriptor> mods, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        if (entry.FileEvidence != InstalledEvidenceKind.LoaderPair || entry.InstallationRoot == null) return null;
        var script = entry.Location;
        var runtime = Path.GetDirectoryName(Path.GetDirectoryName(script))!;
        var dll = Path.Combine(runtime,"dsound.dll");
        var scriptHash = LocalFileDigest(script);
        var dllHash = LocalFileDigest(dll);
        if (scriptHash == null || dllHash == null) return null;
        // Catalog roots may start at different levels of a wrapped package. Probe the observed
        // relative suffixes directly; never enumerate a complete source archive or game tree.
        var parts = Path.GetRelativePath(entry.InstallationRoot,runtime).Split(Path.DirectorySeparatorChar);
        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(mod.SourcePath) || HasLinkedAncestor(mod.SourcePath)) continue;
            for (int offset = 0; offset <= parts.Length; offset++)
            {
                var source = Path.Combine(mod.SourcePath,Path.Join(parts.AsSpan(offset).ToArray()));
                var candidate = Path.Combine(source,"bitfix","sig.lua");
                if (!File.Exists(candidate) || !StringComparer.Ordinal.Equals(scriptHash,LocalFileDigest(candidate))) continue;
                if (StringComparer.Ordinal.Equals(dllHash,LocalFileDigest(Path.Combine(source,"dsound.dll"))))
                    return mod.DisplayName;
            }
        }
        return null;
    }

    private static string? VerifiedDlssRuntime(string receipt, string root, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        try
        {
            using var input = File.OpenRead(receipt);
            if (input.Length is 0 or > 1048576) return null;
            using var json = JsonDocument.Parse(input);
            var record = json.RootElement;
            if (!record.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array ||
                files.GetArrayLength() is 0 or > 256) return null;
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            string? runtime = null;
            var verifiedComponent = false;
            long digestBudget = 32 * 1024 * 1024;
            foreach (var member in files.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (member.ValueKind != JsonValueKind.Object || !member.TryGetProperty("destination", out var dest) ||
                    dest.ValueKind != JsonValueKind.String || dest.GetString() is not { } raw ||
                    !Path.IsPathFullyQualified(raw)) return null;
                var path = Path.GetFullPath(raw);
                if (!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || !File.Exists(path) ||
                    HasLinkedAncestor(path)) return null;
                // Configuration can change after installation. Require each receipt destination to be present.
                if (Path.GetExtension(path).Equals(".ini",StringComparison.OrdinalIgnoreCase)) continue;
                if (!member.TryGetProperty("installedHash",out var hash) || hash.ValueKind != JsonValueKind.String ||
                    hash.GetString() is not { Length: 64 } expected || !expected.All(Uri.IsHexDigit)) return null;
                var length = new FileInfo(path).Length;
                if (length == 0) return null;
                // A library observation never grants execution authority. Limit hashing on each refresh.
                var name = Path.GetFileName(path);
                var specific = name.Equals("renodx-dlss.addon64",StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("dlss5-feed.addon64",StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("DLSS5_Feed.fx",StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("nvngx_dlssnr.dll",StringComparison.OrdinalIgnoreCase);
                if (specific && length <= 8 * 1024 * 1024 && length <= digestBudget)
                {
                    if (!StringComparer.OrdinalIgnoreCase.Equals(LocalFileDigest(path),expected)) return null;
                    verifiedComponent = true;
                    digestBudget -= length;
                }
                if (Path.GetFileName(path).Equals("nvngx_dlssnr.dll",StringComparison.OrdinalIgnoreCase))
                    runtime = Path.GetDirectoryName(path);
            }
            return verifiedComponent ? runtime : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException) { return null; }
    }

    private static string? ReadPackageName(string path)
    {
        using var trace = MasterDebugLog.BeginMethod();
        try
        {
            using var input = File.OpenRead(path);
            if (input.Length is 0 or > 65536) return null;
            using var doc = JsonDocument.Parse(input,new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var field in new[]{"name","title","Name"})
                if (doc.RootElement.TryGetProperty(field,out var value) && value.ValueKind == JsonValueKind.String &&
                    value.GetString()?.Trim() is { Length: > 0 and <= 200 } name && !name.Any(char.IsControl)) return name;
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}
