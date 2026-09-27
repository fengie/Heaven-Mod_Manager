using System.Text.RegularExpressions;

namespace MhwModManager.Core;

public sealed record LogicalModFamily(
    string Id,
    string Name,
    string DisplayName,
    IReadOnlyList<ModDescriptor> Members,
    string? Category,
    IReadOnlyList<ModDescriptor>? ArchivedMembers = null)
{
    public bool IsComposite => Members.Count > 1;
    public int EnabledMembers => Members.Count(x => x.Enabled);
    public IReadOnlyList<ModDescriptor> SupersededMembers => ArchivedMembers ?? Array.Empty<ModDescriptor>();
}

/// <summary>
/// Groups physical source packages into one user-facing logical mod without destroying provenance.
/// A family can contain a main package plus optional body parts, patches, fixes, or revisions.
/// Alternative/variant packages deliberately remain separate because they usually represent a
/// pick-one replacement rather than an additive overwrite layer.
/// </summary>
public static partial class LogicalModFamilies
{
    private static readonly HashSet<string> ComposableWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "patch", "fix", "fixed", "hotfix", "update", "updated", "optional", "option", "addon", "add-on",
        "top", "waist", "wst", "waste", "leg", "legs", "helm", "helmet", "head", "chest", "body",
        "arms", "arm", "cape", "nocape", "no-cape", "open", "opened", "skimpy", "thong", "invisible",
        "belt", "skirt", "bra", "jacket", "physics", "ctc", "ccl", "size", "sleeve", "sleeves",
        "fur", "pants", "shorts", "corset", "texture", "textures", "retexture", "skin", "skins",
        "nipple", "nipples", "braless", "topless", "nude", "naked", "panty", "panties", "underwear",
        "bikini", "glove", "gloves", "boot", "boots", "coat", "coatless", "sleeveless", "jacketless",
        "hoodless", "maskless", "uncensored", "censored"
    };

    private static readonly HashSet<string> PickOneWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "alternative", "alt", "variant", "choice", "choose", "either"
    };

    private static readonly HashSet<string> BareSuffixWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "patch", "fix", "fixed", "hotfix", "update", "updated", "optional", "option", "addon", "add-on",
        "top", "waist", "wst", "waste", "leg", "legs", "helm", "helmet", "head", "chest", "body",
        "arms", "arm", "cape", "nocape", "no-cape", "open", "opened", "skimpy", "thong", "invisible",
        "belt", "skirt", "bra", "jacket", "physics", "ctc", "ccl", "size", "sleeve", "sleeves",
        "fur", "pants", "shorts", "corset", "no", "with", "without", "new", "newer", "latest",
        "revised", "revision", "refresh", "remaster", "nipple", "nipples", "braless", "topless",
        "nude", "naked", "panty", "panties", "underwear", "bikini", "glove", "gloves", "boot", "boots",
        "coat", "coatless", "sleeveless", "jacketless", "hoodless", "maskless", "uncensored", "censored"
    };

    private static readonly HashSet<string> RevisionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "newer", "latest", "revised", "revision", "refresh", "remaster", "update", "updated",
        "fix", "fixed", "hotfix"
    };

    private static readonly HashSet<string> TextureWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "texture", "textures", "retexture", "retextures", "skin", "skins", "diffuse", "normal", "normals",
        "2k", "4k", "8k", "hd", "hires", "highres"
    };

    private static readonly string[] Separators = [" - ", " – ", " — ", ": ", " | "];

    public static IReadOnlyList<LogicalModFamily> Build(IReadOnlyList<ModDescriptor> mods, IReadOnlyList<ModFileDescriptor>? files = null, GameProfile? game = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var archivedBySuccessor = mods.Where(m=>m.IsSuperseded&&!string.IsNullOrWhiteSpace(m.SupersededByModId))
            .GroupBy(m=>m.SupersededByModId!,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var inferred = GenericFamilyInference.InferKeys(mods, files ?? Array.Empty<ModFileDescriptor>(), out var familyEvidence, game is null || game.IsMonsterHunterWorld);
        foreach (var evidence in familyEvidence)
            MasterDebugLog.Write("FAMILY", $"GENERIC pair={evidence.LeftModId}<->{evidence.RightModId}; score={evidence.Score}; evidence={evidence.Summary}");

        var keyed = mods.Where(m=>!m.IsSuperseded).Select(m => (mod: m, key: FamilyKey(m, inferred))).ToArray();
        var families = new List<LogicalModFamily>();
        foreach (var group in keyed.GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.Select(x => x.mod).ToArray();
            var provenFamily = group.Key.StartsWith("saved:", StringComparison.OrdinalIgnoreCase) ||
                               group.Key.StartsWith("nexus:", StringComparison.OrdinalIgnoreCase) ||
                               group.Key.StartsWith("evidence:", StringComparison.OrdinalIgnoreCase) ||
                               candidates.Any(IsComposableMember) ||
                               candidates.Count(IsTextureRevision) >= 2;

            // Same display/folder labels are not proof of a family. Keep ambiguous duplicates such as
            // several different packages all called "Fatalis Patch" as separate logical mods.
            if (!provenFamily && candidates.Length > 1)
            {
                MasterDebugLog.Write("FAMILY", $"SPLIT ambiguous key={group.Key}; members={candidates.Length}; names=[{string.Join(" | ", candidates.Select(x=>x.DisplayName))}]");
                foreach (var candidate in candidates) families.Add(CreateFamily("single:" + candidate.Id, [candidate], archivedBySuccessor.GetValueOrDefault(candidate.Id)));
                continue;
            }

            if (candidates.Length > 1)
            {
                var evidence = group.Key.StartsWith("saved:", StringComparison.OrdinalIgnoreCase) ? "saved/manager family id"
                    : group.Key.StartsWith("nexus:", StringComparison.OrdinalIgnoreCase) ? "shared source/Nexus lineage"
                    : group.Key.StartsWith("evidence:", StringComparison.OrdinalIgnoreCase) ? "generic name + file-layout evidence"
                    : candidates.Any(IsComposableMember) ? "component/patch naming"
                    : "texture revision lineage";
                MasterDebugLog.Write("FAMILY", $"GROUP key={group.Key}; members={candidates.Length}; evidence={evidence}; names=[{string.Join(" | ", candidates.Select(x=>x.DisplayName))}]");
            }

            var archived=candidates.SelectMany(c=>archivedBySuccessor.GetValueOrDefault(c.Id)??Array.Empty<ModDescriptor>()).ToArray();
            families.Add(CreateFamily(group.Key, candidates, archived));
        }

        return families
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LogicalModFamily CreateFamily(string key,ModDescriptor[] candidates,ModDescriptor[]? archived=null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var members = candidates
            .OrderBy(CompositionRank)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var representative = ChooseRepresentative(members);
        var display = CleanDisplayName(representative.DisplayName);
        if (members.Length > 1 && display.Length == 0) display = CleanDisplayName(representative.Name);
        return new(
            "logical:" + key,
            representative.Name,
            display.Length == 0 ? representative.DisplayName : display,
            members,
            CommonCategory(members),
            archived);
    }

    private static bool IsComposableMember(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var clean=CleanName(mod.Name);
        return !StringComparer.OrdinalIgnoreCase.Equals(StripComposableSuffix(clean),clean);
    }

    private static bool IsTextureRevision(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var withoutNexus=NexusSuffixRegex().Replace(mod.Name,string.Empty).Trim();
        if(!VersionSuffixRegex().IsMatch(withoutNexus)&&!Tokenize(withoutNexus).Any(RevisionWords.Contains))return false;
        return Tokenize(CleanName(mod.Name)).Any(TextureWords.Contains);
    }

    public static int CompositionRank(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tokens = Tokenize(CleanName(mod.Name));
        if (tokens.Any(t => t is "hotfix" or "fixed" or "fix")) return 500;
        if (tokens.Any(RevisionWords.Contains)) return 450;
        if (tokens.Any(t => t is "patch")) return 400;
        if (tokens.Any(t => t is "optional" or "option" or "addon" or "add-on")) return 350;
        if (tokens.Any(ComposableWords.Contains)) return 300;
        return 100;
    }

    private static string FamilyKey(ModDescriptor mod, IReadOnlyDictionary<string,string> inferred)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.IsNullOrWhiteSpace(mod.FamilyId))
            return "saved:" + NormalizeKey(mod.FamilyId);

        // Source identity is stronger than author-entered file categories. A Nexus page can publish
        // several main files, optional files, patches, or mutually-exclusive variants that still belong
        // to one user-facing logical family. The physical packages stay independent family members.
        if (!string.IsNullOrWhiteSpace(mod.NexusModId))
            return "nexus:" + NormalizeKey(mod.NexusModId);

        if (inferred.TryGetValue(mod.Id, out var inferredKey))
            return inferredKey;

        var withoutNexus = NexusSuffixRegex().Replace(mod.Name, string.Empty).Trim();
        var hasVersionSuffix = VersionSuffixRegex().IsMatch(withoutNexus);
        var clean = CleanName(mod.Name);
        var stem = StripComposableSuffix(clean);
        if (stem.Length == 0) stem = clean;

        // Versioned texture resources are revisions of one logical resource and should collapse so
        // the newest texture provider wins automatically. Arbitrary versioned structural mods stay
        // separate unless they are also a recognizable component/patch of a family.
        var isComponentFamily = !StringComparer.OrdinalIgnoreCase.Equals(stem, clean);
        var textureRevision = Tokenize(clean).Any(TextureWords.Contains);
        if (hasVersionSuffix && !isComponentFamily && !textureRevision)
            return "versioned:" + NormalizeKey(withoutNexus);

        return "name:" + NormalizeKey(stem);
    }

    private static string StripComposableSuffix(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var separator in Separators)
        {
            var index = name.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index <= 0) continue;
            var suffix = name[(index + separator.Length)..].Trim();
            var suffixTokens = Tokenize(suffix);
            if (suffixTokens.Any(PickOneWords.Contains)) return name;
            if (suffixTokens.Any(ComposableWords.Contains) || suffixTokens.Any(RevisionWords.Contains))
                return name[..index].Trim();
        }

        var tokens = Tokenize(name);
        if (tokens.Length < 3) return name;
        var start = Math.Max(1, tokens.Length - 4);
        for (var i = start; i < tokens.Length; i++)
        {
            var suffix = tokens.Skip(i).ToArray();
            if (!suffix.Any(t => ComposableWords.Contains(t) || RevisionWords.Contains(t))) continue;
            if (suffix.Any(PickOneWords.Contains)) return name;
            if (suffix.All(BareSuffixWords.Contains)) return string.Join(' ', tokens.Take(i));
        }
        return name;
    }

    private static ModDescriptor ChooseRepresentative(ModDescriptor[] members)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return members
            .OrderBy(CompositionRank)
            .ThenBy(x => CleanName(x.Name).Length)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string? CommonCategory(ModDescriptor[] members)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var categories = members.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return categories.Length switch { 0 => null, 1 => categories[0], _ => "Composite" };
    }

    private static string CleanDisplayName(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return StripComposableSuffix(CleanName(name));
    }

    private static string CleanName(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var n = NexusSuffixRegex().Replace(name, string.Empty).Trim();
        n = VersionSuffixRegex().Replace(n, string.Empty).Trim();
        return WhitespaceRegex().Replace(n, " ");
    }

    private static string NormalizeKey(string value) =>
        string.Join('-', Tokenize(value)).ToLowerInvariant();

    private static string[] Tokenize(string name) =>
        TokenRegex().Matches(name.ToLowerInvariant()).Cast<Match>().Select(m => m.Value).ToArray();

    [GeneratedRegex(@"-\d{2,7}-\d+(?:-\d+){0,5}(?:\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NexusSuffixRegex();

    [GeneratedRegex(@"(?:\s+|[-_])v?\d+(?:\.\d+){0,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffixRegex();

    [GeneratedRegex(@"[a-z0-9]+(?:-[a-z0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
