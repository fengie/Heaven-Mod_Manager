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
        var optional = Enumerable.Range(0, dirs.Length).Where(i => labels[i]!.StartsWith("Optional File", StringComparison.OrdinalIgnoreCase)).ToArray();
        var overlay = main >= 0 && optional.Length == dirs.Length - 1;
        if (!alternatives && !overlay)
            return game?.AdapterId=="unreal-paks" && labels.Count(name=>name is {Length:>0} && char.IsAsciiDigit(name[0]))>1
                ? ParameterFamilyReader.Read(root,ct) : [];

        if (dirs.Concat(notes).Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)) return [];
        var text = string.Join(" ",notes.Where(p=>Path.GetExtension(p).Equals(".txt",StringComparison.OrdinalIgnoreCase))
            .Select(ReadBoundedNote)).ToLowerInvariant();
        if (alternatives)
        {
            var feature = contrast[0]!.Value.Feature;
            if (!(text.Contains("choose one") || text.Contains("only one") ||
                (text.Contains(feature) && (text.Contains(feature + " version") || text.Contains("options") || text.Contains("without"))))) return [];
            foreach (var dir in dirs)
            {
                var files = SafeRecursiveTraversal.Snapshot(dir, ct).Files;
                if (files.Any(f => Path.GetDirectoryName(f) != dir) || !UnrealPakLayout.Complete(files.Select(Path.GetFileName)!,true)) return [];
            }
            return dirs.Select(dir => new AuthoredPart(Path.GetFileName(dir), dir, "Option", feature)).ToArray();
        }
        if (!text.Contains("main file")) return [];
        if (optional.Length > 1 && !IndependentlySelectable(text)) return [];

        // Compare the same declared mount in every part. GameRoot is the scanner's explicit
        // game-relative layout, including for profiles without a narrower loader directory.
        var mount = Directory.Exists(Path.Combine(dirs[main], "GameRoot"))
            ? "GameRoot" : game?.ModRootRelativePath ?? "nativePC";
        if (string.IsNullOrWhiteSpace(mount)) return [];
        var paths = new HashSet<string>[dirs.Length];
        for (var i = 0; i < dirs.Length; i++)
        {
            var target = Path.Combine(dirs[i],mount);
            if (!Directory.Exists(target)) return [];
            var files = SafeRecursiveTraversal.Snapshot(dirs[i],ct).Files;
            if (files.Count == 0 || files.Any(f => !f.StartsWith(target + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))) return [];
            paths[i] = files.Select(f => Path.GetRelativePath(target,f)).ToHashSet(PathRules.Comparer);
        }

        var assigned = new HashSet<string>(PathRules.Comparer);
        foreach (var i in optional)
        {
            if (!paths[i].IsSubsetOf(paths[main]) || assigned.Overlaps(paths[i])) return [];
            assigned.UnionWith(paths[i]);
        }
        return dirs.Select((dir, i) => new AuthoredPart(labels[i]!, dir, i == main ? "Main" : "Optional", null)).ToArray();
    }

    private static string ReadBoundedNote(string path)
    {
        using var trace=MasterDebugLog.BeginMethod();
        const int limit=65536;
        using var stream = File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (stream.Length>limit) return "";
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        using var memory = new MemoryStream(bytes);
        using var reader = new StreamReader(memory,detectEncodingFromByteOrderMarks:true);
        return Path.GetFileName(path) + "\n" + reader.ReadToEnd();
    }

    private static bool IndependentlySelectable(string notes)
    {
        using var trace = MasterDebugLog.BeginMethod();
        var sentences = notes.Split(['.', '!', '?', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return sentences.Any(sentence => sentence is "optional files can be combined" or
            "the optional files can be combined" or "optional files can be used together" or
            "the optional files can be used together" or "optional files may be installed independently") &&
            !(notes.Contains("only one") || notes.Contains("choose one") || notes.Contains("incompatible") || notes.Contains("except"));
    }
}
