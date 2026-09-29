using System.Text.RegularExpressions;

namespace MhwModManager.Core;

public static partial class PathRules
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> ReservedDeviceNames = BuildReservedNames();

    public static string Normalize(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Replace('/', '\\').Trim();
        if (p.StartsWith('\\') || Path.IsPathRooted(p))
            throw new ArgumentException($"Unsafe rooted path: {path}", nameof(path));
        p = p.TrimStart('\\');
        var segments = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments.Any(IsUnsafeSegment))
            throw new ArgumentException($"Unsafe relative path: {path}", nameof(path));

        p = string.Join('\\', segments);
        if (!p.StartsWith("nativePC\\", StringComparison.OrdinalIgnoreCase) &&
            !p.StartsWith("root\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path must begin with nativePC\\ or root\\: {path}", nameof(path));
        return p;
    }

    public static bool IsSafeArchiveRelativePath(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var p = path.Replace('/', '\\').Trim();
        if (p.StartsWith('\\') || Path.IsPathRooted(p) || p.Contains(':'))
            return false;
        var segments = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(s => !IsUnsafeSegment(s));
    }

    private static bool IsUnsafeSegment(string segment)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (segment is "." or ".." || string.IsNullOrWhiteSpace(segment))
            return true;
        if (segment.Contains(':') || segment.IndexOfAny(InvalidFileNameChars) >= 0)
            return true;
        var trimmed = segment.TrimEnd('.', ' ');
        if (trimmed.Length == 0 || !trimmed.Equals(segment, StringComparison.Ordinal))
            return true;
        var stem = Path.GetFileNameWithoutExtension(trimmed);
        return ReservedDeviceNames.Contains(stem);
    }

    public static string? ResourceNamespace(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = Normalize(path);
        var segments = normalized.Split('\\');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith("mod_", StringComparison.OrdinalIgnoreCase))
                return string.Join('\\', segments.Take(i + 1)).ToLowerInvariant();
        }
        return null;
    }

    public static bool TryGetArmorComponent(string path, out string modelId, out string component)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        modelId = string.Empty;
        component = string.Empty;
        var p = Normalize(path);
        var match = ArmorModelRegex().Match(p);
        if (!match.Success)
            return false;
        var matchedModelId = match.Groups[1].Value.ToLowerInvariant();
        modelId = matchedModelId;

        var segments = p.Split('\\');
        var modelIndex = Array.FindIndex(segments, s => s.Equals(matchedModelId, StringComparison.OrdinalIgnoreCase));
        if (modelIndex < 0 || modelIndex + 1 >= segments.Length)
            return false;
        component = segments[modelIndex + 1].ToLowerInvariant() switch
        {
            "helm" => "head",
            "body" => "chest",
            "arm" => "arms",
            "wst" => "waist",
            "leg" => "legs",
            _ => string.Empty
        };
        return component.Length > 0;
    }

    public static FileClass ClassifyFile(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".tex" or ".dds" or ".tga" or ".ktx" or ".ktx2" => FileClass.Texture,
            ".mod3" or ".mrl3" or ".ctc" or ".ccl" or ".evbd" or ".evhl" or ".nif" or ".mesh" or ".hkx" or ".gr2" => FileClass.Structural,
            ".lmt" or ".timl" or ".efx" or ".epv3" or ".gmd" or ".sobj" or ".em" or ".col" or ".pak" or ".ucas" or ".utoc" or ".archive" or ".bundle" or ".ba2" or ".bsa" or ".bin" or ".dat" or ".json" or ".xml" or ".yaml" or ".yml" => FileClass.GameData,
            ".dll" or ".asi" or ".esp" or ".esm" or ".esl" or ".pex" or ".lua" => FileClass.Plugin,
            ".exe" => FileClass.Executable,
            _ => FileClass.Other
        };
    }

    private static HashSet<string> BuildReservedNames()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
        for (var i = 1; i <= 9; i++)
        {
            names.Add($"COM{i}");
            names.Add($"LPT{i}");
        }
        return names;
    }

    [GeneratedRegex(@"(?:^|\\)(pl\d{3}_\d{4})(?:\\|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArmorModelRegex();
}
