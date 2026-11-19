using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Negative evidence for automatic composition and cross-source add-on suggestions.
/// Word boundaries are consistent even for hyphenated archive labels such as
/// "Alternate-Patch", where other name tokenizers preserve a combined token.
/// </summary>
internal static partial class ModChoiceSignals
{
    internal static IReadOnlySet<string> PickOneTokens { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alternative", "alternate", "alt", "variant", "choice", "choose", "either",
            "color", "colour", "style", "version"
        };

    internal static bool HasPickOneCue(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return false;
        foreach (Match token in WordRegex().Matches(value))
        {
            if (PickOneTokens.Contains(token.Value))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
