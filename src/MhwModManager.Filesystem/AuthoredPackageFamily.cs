using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed record AuthoredPart(string Label, string SourceDirectory, string Role, string? ChoiceGroup, string[]? Files = null);

public static class AuthoredPackageFamily
{
    public static IReadOnlyList<AuthoredPart> Read(string root, GameProfile? game, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        SafeRecursiveTraversal.EnsureRootIsNotReparse(root);
        var dirs = Directory.EnumerateDirectories(root).Take(33).ToArray();
        if (dirs.Length is < 2 or > 32) return [];
        var notes = Directory.EnumerateFiles(root).Take(17).ToArray();
        if (notes.Length is 0 or > 16 || !notes.All(path => (ManagedCollectionNotes.IsNote(path) || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)))) return [];
        var labels = dirs.Select(Path.GetFileName).ToArray();
        var contrast = labels.Select(UnrealPakLayout.ContrastOption).ToArray();
        var alternatives = game?.AdapterId == "unreal-paks" && dirs.Length == 2 && contrast[0] is { } a &&
            contrast[1] is { } b && a.Feature == b.Feature && a.Enabled != b.Enabled;
        var main = Array.FindIndex(labels, label => label!.StartsWith("Main File", StringComparison.OrdinalIgnoreCase));
        var optional = Array.FindIndex(labels, label => label!.StartsWith("Optional File", StringComparison.OrdinalIgnoreCase));
        var overlay = dirs.Length == 2 && main >= 0 && optional >= 0;
        if (!alternatives && !overlay) return [];
        if (dirs.Concat(notes).Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)) return [];
        var text = string.Join(" ",notes.Where(p => Path.GetExtension(p).Equals(".txt",StringComparison.OrdinalIgnoreCase) &&
            new FileInfo(p).Length <= 65536).Select(p => Path.GetFileName(p) + " " + File.ReadAllText(p))).ToLowerInvariant();
        if (alternatives)
        {
            var feature = contrast[0]!.Value.Feature;
            if (!(text.Contains("choose one") || text.Contains("only one") ||
                (text.Contains(feature) && (text.Contains("versions") || text.Contains("options") || text.Contains("without"))))) return [];
            foreach (var dir in dirs)
            {
                var files = SafeRecursiveTraversal.Snapshot(dir, ct).Files;
                if (files.Any(f => Path.GetDirectoryName(f) != dir) || !UnrealPakLayout.Complete(files.Select(Path.GetFileName)!,true)) return [];
            }
            return dirs.Select(dir => new AuthoredPart(Path.GetFileName(dir), dir, "Option", feature)).ToArray();
        }
        var mount = game?.ModRootRelativePath ?? "nativePC";
        if (string.IsNullOrWhiteSpace(mount) || !text.Contains("main file")) return [];
        var paths = new HashSet<string>[2];
        for (var i = 0; i < dirs.Length; i++)
        {
            var target = Path.Combine(dirs[i],mount);
            if (!Directory.Exists(target)) return [];
            var files = SafeRecursiveTraversal.Snapshot(dirs[i],ct).Files;
            if (files.Count == 0 || files.Any(f => !f.StartsWith(target + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))) return [];
            paths[i] = files.Select(f => Path.GetRelativePath(target,f)).ToHashSet(PathRules.Comparer);
        }
        if (!paths[optional].IsSubsetOf(paths[main])) return [];
        return [new(labels[main]!, dirs[main], "Main", null), new(labels[optional]!, dirs[optional], "Optional", null)];
    }
}
