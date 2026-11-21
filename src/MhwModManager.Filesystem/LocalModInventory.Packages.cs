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
            foreach (var entry in files.Where(e => e.FileEvidence == "Mod manifest in Mods directory"))
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
                    case "Unreal mod container":
                    case "Loose Unreal companion set outside standard Paks directory · review":
                        members = UnrealExtensions.Select(ext => Path.ChangeExtension(path,ext))
                            .Where(file => File.Exists(file) && !HasLinkedAncestor(file)).ToArray();
                        if (!UnrealPakLayout.Complete(members,true)) continue;
                        key = Path.ChangeExtension(relative,null); title = name;
                        var mounted = entry.FileEvidence == "Unreal mod container" &&
                            (string.IsNullOrEmpty(game.ModRootRelativePath) ||
                             path.StartsWith(Path.TrimEndingDirectorySeparator(Path.Combine(root,game.ModRootRelativePath)) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                        owner = mounted ? "Unreal container" : "Unreal package · source location";
                        break;
                    case "REFramework script":
                        key = relative; title = name; owner = "REFramework script"; break;
                    case "UE4SS mod entry point":
                        var package = Directory.GetParent(dir)!;
                        key = Path.GetRelativePath(root,package.FullName); title = package.Name; owner = "UE4SS package"; break;
                    case "DLSS Nexus completed installation receipt":
                        var runtimeDir = VerifiedDlssRuntime(path,root,ct);
                        if (runtimeDir == null) continue;
                        key = "DLSS-Nexus:" + Path.GetRelativePath(root,runtimeDir); title = "DLSS Nexus";
                        owner = "Installed files present · component hash confirmed · " + Path.GetRelativePath(root,runtimeDir);
                        break;
                    case "Paired loader files · runtime use unconfirmed":
                        var runtime = Directory.GetParent(dir)!.FullName;
                        key = relative; title = "Bitfix loader"; owner = PathRules.Comparer.Equals(runtime,Path.GetDirectoryName(Path.Combine(root,game.ExecutableRelativePath)))
                            ? "Loader pair · runtime unverified" : "Loader pair · source location";
                        var dll = Path.Combine(runtime,"dsound.dll");
                        if (!File.Exists(dll) || HasLinkedAncestor(dll)) continue;
                        members = [path,dll]; break;
                    case "Mod manifest in Mods directory":
                    case "Payload in game Mods directory":
                        var manifest = dir;
                        while (manifest != null && !manifests.ContainsKey(manifest) && !PathRules.Comparer.Equals(root,manifest))
                            manifest = Path.GetDirectoryName(manifest);
                        if (manifest != null && manifests.TryGetValue(manifest,out var label))
                        { key = Path.GetRelativePath(root,manifest); title = label; owner = "Local mod manifest"; }
                        else if (entry.FileEvidence == "Payload in game Mods directory" && PathRules.Comparer.Equals(dir,Path.Combine(root,"Mods")))
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
                if (Path.GetExtension(path).Equals(".dll",StringComparison.OrdinalIgnoreCase) &&
                    length <= 8 * 1024 * 1024 && length <= digestBudget)
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
