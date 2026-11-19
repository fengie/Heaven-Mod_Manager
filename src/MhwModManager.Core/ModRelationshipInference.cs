using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Cross-source relationships remain suggestions until the user confirms one.
/// A shared game path is a conflict, not proof of common authorship or overwrite order.
/// </summary>
public enum ModRelationshipKind
{
    PossibleOverride
}

public sealed record ModRelationshipSuggestion(
    string ParentModId,
    string ChildModId,
    ModRelationshipKind Kind,
    int SharedPaths,
    int Score,
    string Evidence);

public static partial class ModRelationshipInference
{
    private const int MaxCandidateProvidersPerPath = 128;
    private const int MaxChildPaths = 64;
    private static readonly HashSet<string> AddOnCues = new(StringComparer.OrdinalIgnoreCase)
    {
        "overlay", "addon", "optional", "patch", "hotfix", "fix", "fixed",
        "component", "suboverride", "retexture", "texture", "textures"
    };
    private static readonly HashSet<string> GenericNameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod", "mods", "main", "base", "default", "core", "original", "overlay", "addon",
        "optional", "patch", "hotfix", "component", "suboverride", "retexture",
        "skin", "skins", "texture", "textures", "body", "armor", "armour", "outfit",
        "pack", "fix", "files", "file", "for", "with", "the", "and", "version"
    };

    public static IReadOnlyList<ModRelationshipSuggestion> Suggest(
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyList<ModFileDescriptor> files,
        int maximumSuggestions = 200)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (maximumSuggestions <= 0) return Array.Empty<ModRelationshipSuggestion>();
        // Confirmed manual roots remain eligible as parents of later add-ons.
        // Confirmed child/optional members never become new candidate parents.
        var eligible = mods.Where(m => !m.IsSuperseded &&
                (string.IsNullOrWhiteSpace(m.FamilyId) || IsConfirmedManualMain(m)))
            .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        if (eligible.Count < 2) return Array.Empty<ModRelationshipSuggestion>();

        var byMod = files.Where(f => eligible.ContainsKey(f.ModId))
            .GroupBy(f => f.ModId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var profiles = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
        var providersByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in eligible.Values)
        {
            if (!byMod.TryGetValue(mod.Id, out var owned) || owned.Length == 0) continue;
            var paths = owned.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Index captured content once. Duplicate target rows have ambiguous
            // ownership and must never be treated as proven identical bytes.
            var hashesByPath = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in owned)
                if (!hashesByPath.TryAdd(file.Path, file.BlobSha256))
                    hashesByPath[file.Path] = string.Empty;
            var tokens = Tokens(NexusArchiveIdentity.RemoveSuffix(mod.DisplayName));
            var profile = new Profile(mod, paths, hashesByPath,
                owned.Any(f => f.FileClass == FileClass.Structural),
                owned.All(f => f.FileClass == FileClass.Texture),
                HasAddOnCue(mod), tokens);
            profiles[mod.Id] = profile;
            foreach (var path in paths)
            {
                if (!providersByPath.TryGetValue(path, out var providers))
                    providersByPath[path] = providers = new List<string>();
                providers.Add(mod.Id);
            }
        }

        var suggestions = new List<ModRelationshipSuggestion>();
        foreach (var child in profiles.Values.Where(p => p.IsAddOn && p.Paths.Count <= MaxChildPaths)
                     .OrderBy(p => p.Mod.Id, StringComparer.OrdinalIgnoreCase))
        {
            var isNegatedOption = HasNegatedOptionCue(child.Mod);
            var parentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in child.Paths)
            {
                if (!providersByPath.TryGetValue(path, out var providers) ||
                    providers.Count > MaxCandidateProvidersPerPath) continue;
                foreach (var id in providers) parentIds.Add(id);
            }

            foreach (var id in parentIds.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (id.Equals(child.Mod.Id, StringComparison.OrdinalIgnoreCase) ||
                    !profiles.TryGetValue(id, out var parent) ||
                    parent.IsAddOn ||
                    parent.Paths.Count < child.Paths.Count ||
                    ProvenanceIntelligence.SameNexusMod(parent.Mod, child.Mod)) continue;

                var shared = child.Paths.Count(path => parent.Paths.Contains(path));
                if (shared == 0 || shared * 4 < child.Paths.Count * 3) continue;

                var identical = 0;
                foreach (var path in child.Paths)
                {
                    if (!parent.HashesByPath.TryGetValue(path, out var parentHash) ||
                        !child.HashesByPath.TryGetValue(path, out var childHash) ||
                        parentHash.Length != 64 || childHash.Length != 64 ||
                        !parentHash.All(Uri.IsHexDigit) || !childHash.All(Uri.IsHexDigit))
                        continue;
                    if (StringComparer.OrdinalIgnoreCase.Equals(parentHash, childHash))
                        identical++;
                }
                // A completely byte-identical overlap has no overwrite effect.
                // Do not turn a copied resource into mod-wide precedence.
                if (identical == shared) continue;

                var sharedIdentity = child.IdentityTokens.Overlaps(parent.IdentityTokens);
                // 'No-X' options must retain a meaningful shared name token. Sharing
                // one common texture with an unrelated structural mod is not enough.
                if (isNegatedOption && !sharedIdentity) continue;
                // Texture-only replacements of a structural base can be an add-on even if
                // their titles are different. Otherwise require a distinctive name token;
                // an identical texture path alone is not a parent-child relationship.
                if (!sharedIdentity && !(child.TextureOnly && parent.HasStructural)) continue;

                var score = 50 + (shared == child.Paths.Count ? 10 : 0)
                    + (parent.HasStructural && child.TextureOnly ? 15 : 0)
                    + (sharedIdentity ? 15 : 0);
                var explanation = $"Review required: {shared}/{child.Paths.Count} child file(s) share target paths with '{parent.Mod.DisplayName}' ({identical} byte-identical, {shared-identical} different or unverified). " +
                    $"'{child.Mod.DisplayName}' has an add-on/overlay label; " +
                    (parent.HasStructural && child.TextureOnly
                        ? "the proposed parent also supplies structural assets. "
                        : "both titles share a distinctive identity term. ") +
                    "Cross-source file overlap does not establish dependency or overwrite order.";
                suggestions.Add(new(parent.Mod.Id, child.Mod.Id,
                    ModRelationshipKind.PossibleOverride, shared, score, explanation));
            }
        }

        // Keep the review surface bounded for a child touching a common resource path.
        return suggestions.GroupBy(s => s.ChildModId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderByDescending(s => s.Score)
                .ThenBy(s => s.ParentModId, StringComparer.OrdinalIgnoreCase).Take(5))
            .OrderBy(s => s.ChildModId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(s => s.Score)
            .ThenBy(s => s.ParentModId, StringComparer.OrdinalIgnoreCase)
            .Take(maximumSuggestions).ToArray();
    }

    private static bool IsConfirmedManualMain(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return mod.FamilyId is not null &&
            mod.FamilyId.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(mod.FamilyRole, "Main", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAddOnCue(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // An add-on role must be detected before generic identity noise is removed.
        var nameAndRole = $"{mod.Name} {mod.DisplayName} {mod.FamilyRole}";
        if (ModChoiceSignals.HasPickOneCue(nameAndRole)) return false;
        var tokens = WordRegex().Matches(nameAndRole.ToLowerInvariant())
            .Cast<Match>().Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return HasNegatedOptionCue(mod) || tokens.Overlaps(AddOnCues) ||
            tokens.Contains("add") && tokens.Contains("on") ||
            string.Equals(mod.FamilyRole, "Optional", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNegatedOptionCue(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();

        static bool IsNegatedPrefix(string name)
        {
            name = NexusArchiveIdentity.RemoveSuffix(name).TrimStart();
            return name.StartsWith("no-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("no ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("without-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("without ", StringComparison.OrdinalIgnoreCase);
        }

        return IsNegatedPrefix(mod.Name) || IsNegatedPrefix(mod.DisplayName);
    }

    private static HashSet<string> Tokens(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return WordRegex().Matches(text.ToLowerInvariant()).Cast<Match>().Select(x => x.Value)
            .Where(x => x.Length > 1 && !GenericNameWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Profile(
        ModDescriptor Mod,
        HashSet<string> Paths,
        Dictionary<string,string> HashesByPath,
        bool HasStructural,
        bool TextureOnly,
        bool IsAddOn,
        HashSet<string> IdentityTokens);

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
