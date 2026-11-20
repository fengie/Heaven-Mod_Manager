namespace MhwModManager.Core;

public static class UnrealPakLayout
{
    public static string? ProjectMount(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', 4);
        return parts.Length >= 3 && parts[0].Length > 0 &&
            parts[1].Equals("Content", StringComparison.OrdinalIgnoreCase) &&
            parts[2].Equals("Paks", StringComparison.OrdinalIgnoreCase)
            ? string.Join("/", parts.Take(3)) : null;
    }

    public static bool IsMember(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".pak" or ".sig" or ".utoc" or ".ucas";

    public static bool Complete(IEnumerable<string> files, bool singleStem = false)
    {
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
