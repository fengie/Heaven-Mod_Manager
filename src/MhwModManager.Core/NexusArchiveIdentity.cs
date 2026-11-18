using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Reads the source-page identity embedded in known Nexus download filenames.
/// This is provenance, not evidence that two different Nexus pages share a family
/// or that either archive may overwrite another without an explicit relationship.
/// </summary>
public sealed record NexusArchiveIdentity(string ModId, string DisplayName)
{
    public static bool TryParse(string? pathOrName, out NexusArchiveIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(pathOrName)) return false;

        // Path.GetFileName only recognizes host-native separators. Downloaded names may have
        // originated on another OS; treat either separator as a boundary, never as data.
        var normalized = pathOrName.Trim().Replace('\\', '/');
        var leaf = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (leaf.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            leaf.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
            leaf.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            leaf = leaf[..leaf.LastIndexOf('.')];

        var match = TimestampedSuffix().Match(leaf);
        if (!match.Success) match = LegacyNumericSuffix().Match(leaf);
        if (!match.Success) return false;

        var display = leaf[..match.Index].Trim().TrimEnd('-', '_', ' ');
        if (display.Length < 2 || !display.Any(char.IsLetter)) return false;
        identity = new(match.Groups["id"].Value, display);
        return true;
    }

    public static string RemoveSuffix(string name) =>
        TryParse(name, out var identity) ? identity.DisplayName : name;

    // Nexus archive names may carry a download timestamp followed by a hexadecimal
    // fingerprint. The older numeric-only format is retained for existing libraries.
    // A random suffix alone is not sufficient to infer Nexus provenance.
    [GeneratedRegex(@"-(?<id>[1-9]\d{1,6})-(?:\d+-){1,7}(?<timestamp>(?:1[5-9]|2\d)\d{8})-(?<digest>[a-f0-9]{12,64})(?:\s*\(\d+\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimestampedSuffix();

    [GeneratedRegex(@"-(?<id>[1-9]\d{1,6})-\d+(?:-\d+){0,6}(?:\s*\(\d+\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyNumericSuffix();
}
