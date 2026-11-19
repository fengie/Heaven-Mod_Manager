using System.Globalization;
using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Reads the source-page identity embedded in known Nexus download filenames.
/// This is provenance, not evidence that two different Nexus pages share a family
/// or that either archive may overwrite another without an explicit relationship.
/// </summary>
public sealed partial record NexusArchiveIdentity(string ModId, string DisplayName, string? FileSlug = null)
{
    public static bool TryParse(string? pathOrName, out NexusArchiveIdentity identity)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
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

        // Nexus changed download names in July 2026. This is a filename clue,
        // not verified provider identity; the slug denotes a specific file.
        var modern = IsoTimestampSuffix().Match(leaf);
        if (!modern.Success) modern = HyphenIsoTimestampSuffix().Match(leaf);
        if (modern.Success)
        {
            var date = modern.Groups["date"].Value.ToUpperInvariant();
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd'T'HH-mm'Z'",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return false;
            var displayName = modern.Groups["name"].Value.Trim();
            if (displayName.Length < 2 || !displayName.Any(char.IsLetter)) return false;
            identity = new(modern.Groups["id"].Value, displayName, modern.Groups["slug"].Value);
            return true;
        }

        var match = TimestampedSuffix().Match(leaf);
        if (!match.Success) match = LegacyNumericSuffix().Match(leaf);
        if (!match.Success) return false;

        var display = leaf[..match.Index].Trim().TrimEnd('-', '_', ' ');
        if (display.Length < 2 || !display.Any(char.IsLetter)) return false;
        identity = new(match.Groups["id"].Value, display);
        return true;
    }

    public static string RemoveSuffix(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return TryParse(name, out var identity) ? identity.DisplayName : name;
    }

    [GeneratedRegex(@"^(?<name>.+)\s+(?<id>[1-9]\d{1,8})\s+(?<version>\d+(?:\.[0-9A-Za-z]+){0,6})\s+(?<date>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z)\s+(?<slug>[A-Za-z0-9]{6,20})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsoTimestampSuffix();

    // Recent Nexus downloads may use a hyphen-delimited ISO timestamp with
    // lowercase t/z. Validate the calendar date before accepting this unverified clue.
    [GeneratedRegex(@"^(?<name>.+)-(?<id>[1-9]\d{1,8})-(?<version>\d+(?:\.[0-9A-Za-z]+){0,6})-(?<date>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z)-(?<slug>[A-Za-z0-9]{6,20})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HyphenIsoTimestampSuffix();

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
