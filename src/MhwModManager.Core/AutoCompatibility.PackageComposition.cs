using System.Globalization;
using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Shared package-role/version inference used by both the planner and the library UI.
/// These helpers deliberately distinguish source-package composition from whole-mod enable state:
/// a base/main package stays enabled while later option/revision packages win only overlapping paths.
/// </summary>
public static partial class AutoCompatibility
{
    private static readonly HashSet<string> OptionalCueWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "optional", "option", "addon", "add-on", "patch", "fix", "fixed", "hotfix", "update", "updated"
    };

    public static bool IsRequiredBasePackage(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ContainsFamilyRoleValue(mod.FamilyRole, "main", "base", "core", "root")) return true;
        return IsStandaloneBaseLabel(mod.Name) || IsStandaloneBaseLabel(mod.DisplayName);
    }

    public static bool IsMainPackage(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return IsRequiredBasePackage(mod) || mod.NexusCategory == NexusFileCategory.Main;
    }

    public static bool IsOptionalPackage(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ContainsFamilyRoleValue(mod.FamilyRole, "optional", "update", "patch", "addon", "add-on", "child")) return true;
        if (mod.NexusCategory is NexusFileCategory.Optional or NexusFileCategory.Update) return true;

        var tokens = PackageTokens(mod.DisplayName).Concat(PackageTokens(mod.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tokens.Any(OptionalCueWords.Contains)) return true;
        return tokens.Length > 0 && tokens[0] is "no" or "without";
    }

    public static bool TryGetPackageVersion(ModDescriptor mod, out int[] version)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var value in new[] { mod.NexusVersion, mod.DisplayName, mod.Name, mod.SourcePath })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var matches = PackageVersionRegex().Matches(value);
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var raw = matches[i].Groups[1].Value;
                var parts = raw.Split('.');
                var parsed = new int[parts.Length];
                var ok = true;
                for (var p = 0; p < parts.Length; p++)
                {
                    if (!int.TryParse(parts[p], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[p]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    version = parsed;
                    return true;
                }
            }
        }

        version = [];
        return false;
    }

    private static bool TryInferSameSourceComposition(
        ModDescriptor a,
        ModDescriptor b,
        PairStats stats,
        out ModDescriptor parent,
        out ModDescriptor child,
        out string reason)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        parent = a;
        child = b;
        reason = string.Empty;

        if (!ProvenanceIntelligence.SameNexusMod(a, b) || stats.SharedPaths == 0) return false;

        var aBase = IsRequiredBasePackage(a);
        var bBase = IsRequiredBasePackage(b);
        if (aBase != bBase)
        {
            var basePackage = aBase ? a : b;
            var layer = aBase ? b : a;

            // Two author-labelled Main files can be true alternatives. A literal/manual base can
            // safely sit below Unknown/Optional/Update siblings from the same Nexus page, but do not
            // silently order another package that Nexus itself also labels Main.
            if (layer.NexusCategory == NexusFileCategory.Main) return false;

            parent = basePackage;
            child = layer;
            reason = $"Auto-composed same-source base stack: '{basePackage.DisplayName}' is the required Main/base package and '{layer.DisplayName}' is a sibling layer from the same Nexus mod. Both stay enabled; the sibling wins only their {stats.SharedPaths} overlapping file(s).";
            return true;
        }

        if (!TryGetPackageVersion(a, out var av) || !TryGetPackageVersion(b, out var bv)) return false;
        var compare = ComparePackageVersion(av, bv);
        if (compare == 0) return false;

        // Version alone must not order unrelated sibling features published on the same Nexus page.
        // Require a strong shared name identity after removing explicit version markers.
        var similarity = PackageIdentitySimilarity(a, b);
        if (similarity < .45) return false;

        parent = compare < 0 ? a : b;
        child = compare < 0 ? b : a;
        reason = $"Auto-composed same-source revision stack: '{child.DisplayName}' is the newer explicit package revision of '{parent.DisplayName}' on the same Nexus mod. Both stay enabled; v{string.Join(".", compare < 0 ? bv : av)} wins only overlapping files, while older-only files remain available.";
        return true;
    }

    private static bool IsStandaloneBaseLabel(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var leaf = Path.GetFileNameWithoutExtension(value.Trim());
        leaf = NexusArchiveTailRegex().Replace(leaf, string.Empty);
        leaf = Regex.Replace(leaf, @"[_-]+", " ", RegexOptions.CultureInvariant).Trim();
        return StandaloneBaseLabelRegex().IsMatch(leaf);
    }

    private static bool ContainsFamilyRoleValue(string? value, params string[] roles) =>
        !string.IsNullOrWhiteSpace(value) && roles.Any(role => value.Contains(role, StringComparison.OrdinalIgnoreCase));

    private static double PackageIdentitySimilarity(ModDescriptor a, ModDescriptor b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var left = PackageIdentityTokens(a.DisplayName.Length > 0 ? a.DisplayName : a.Name);
        var right = PackageIdentityTokens(b.DisplayName.Length > 0 ? b.DisplayName : b.Name);
        if (left.Length == 0 || right.Length == 0) return 0;
        var l = left.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var r = right.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intersection = l.Count(r.Contains);
        var union = l.Count + r.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static string[] PackageIdentityTokens(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var cleaned = PackageVersionRegex().Replace(value, " ");
        return PackageTokens(cleaned)
            .Where(t => t is not "ver" and not "version" and not "iceborne" and not "compatible")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] PackageTokens(string value) =>
        PackageTokenRegex().Matches(value.ToLowerInvariant()).Cast<Match>().Select(m => m.Value).Where(x => x.Length > 1).ToArray();

    private static int ComparePackageVersion(int[] left, int[] right)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var count = Math.Max(left.Length, right.Length);
        for (var i = 0; i < count; i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    [GeneratedRegex(@"(?<![a-z0-9])(?:version|ver|v)\s*([0-9]{1,3}(?:\.[0-9]{1,4}){1,3})(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackageVersionRegex();

    [GeneratedRegex(@"-\d{2,7}-\d+(?:-\d+){0,6}(?:\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NexusArchiveTailRegex();

    [GeneratedRegex(@"^(?:main|base|core|default)(?:\s+(?:file|files|package))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneBaseLabelRegex();

    [GeneratedRegex(@"[a-z0-9]+(?:-[a-z0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackageTokenRegex();
}
