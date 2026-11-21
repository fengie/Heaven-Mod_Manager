namespace MhwModManager.Core;

public static class UnrealPakLayout
{
    public static string? ProjectMount(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var parts = path.Replace('\\', '/').Split('/', 4);
        return parts.Length >= 3 && parts[0].Length > 0 &&
            parts[1].Equals("Content", StringComparison.OrdinalIgnoreCase) &&
            parts[2].Equals("Paks", StringComparison.OrdinalIgnoreCase)
            ? string.Join("/", parts.Take(3)) : null;
    }

    public static bool IsMember(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Path.GetExtension(path).ToLowerInvariant() is ".pak" or ".sig" or ".utoc" or ".ucas";
    }

    // A complete flat container may ship alongside its editable Unreal project. Only a
    // project descriptor accounts for the accompanying subtree; a second package or
    // installer inside that tree still needs independent inspection.
    public static bool HasBundledEditorProject(IReadOnlyList<string> paths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = paths.Select(path => path.Replace('\\', '/')).ToArray();
        var direct = normalized.Where(path => !path.Contains('/') && !ManagedCollectionNotes.IsNote(path)).ToArray();
        if (!Complete(direct, singleStem: true)) return false;
        var roots = normalized.Where(path => path.Contains('/') &&
                path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))
            .Select(path => path[..path.IndexOf('/')]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (roots.Count == 0) return false;
        return normalized.Where(path => path.Contains('/')).All(path =>
            roots.Contains(path[..path.IndexOf('/')]) && !IsMember(path) &&
            Path.GetExtension(path).ToLowerInvariant() is not (".zip" or ".rar" or ".7z") &&
            !path.EndsWith("/ModuleConfig.xml", StringComparison.OrdinalIgnoreCase));
    }

    public static bool Complete(IEnumerable<string> files, bool singleStem = false)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            var extension = Path.GetExtension(path);
            var flag = extension.ToLowerInvariant() switch
            {
                ".pak" => 1, ".sig" => 2, ".utoc" => 4, ".ucas" => 8, _ => 0
            };
            if (flag == 0 || Path.GetFileNameWithoutExtension(path).Length == 0) return false;
            var key = path[..^extension.Length];
            groups[key] = groups.GetValueOrDefault(key) | flag;
        }
        return groups.Count > 0 && (!singleStem || groups.Count == 1) &&
            groups.Values.All(mask => mask is 1 or 3 or 13 or 15);
    }
}
