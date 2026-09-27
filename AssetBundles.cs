namespace MhwModManager.Core;

/// <summary>
/// Defines atomic deployment/conflict units for MHW assets. Structural siblings under the same
/// model/material/physics subtree are treated as one bundle so a human choice selects a coherent
/// mod, not a file-by-file Frankenstein combination.
/// </summary>
public static class AssetBundles
{
    private static readonly HashSet<string> StructuralExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mod3", ".mrl3", ".ctc", ".ccl"
    };

    public static string KeyForPath(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = PathRules.Normalize(path);
        var fileClass = PathRules.ClassifyFile(normalized);
        if (fileClass != FileClass.Structural && fileClass != FileClass.GameData && fileClass != FileClass.Plugin && fileClass != FileClass.Executable)
            return "file:" + normalized.ToLowerInvariant();

        if (PathRules.TryGetArmorComponent(normalized, out var modelId, out var component))
            return $"armor:{modelId.ToLowerInvariant()}:{component.ToLowerInvariant()}";

        var directory = Path.GetDirectoryName(normalized)?.Replace('/', '\\') ?? string.Empty;
        var extension = Path.GetExtension(normalized);
        if (StructuralExtensions.Contains(extension))
            return "struct:" + directory.ToLowerInvariant();

        return "binary:" + directory.ToLowerInvariant();
    }

    public static string DisplayNameForPath(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (PathRules.TryGetArmorComponent(path, out var modelId, out var component))
            return $"{modelId} / {component}";
        var dir = Path.GetDirectoryName(PathRules.Normalize(path));
        return string.IsNullOrWhiteSpace(dir) ? path : dir;
    }
}
