namespace MhwModManager.Core;

/// <summary>
/// Negative evidence for automatic composition and cross-source add-on suggestions.
/// All naming-based inference layers must use the same pick-one cues; an alternative
/// can share every target path with a base without becoming its dependency.
/// </summary>
internal static class ModChoiceSignals
{
    internal static IReadOnlySet<string> PickOneTokens { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alternative", "alternate", "alt", "variant", "choice", "choose", "either",
            "color", "colour", "style", "version"
        };
}
