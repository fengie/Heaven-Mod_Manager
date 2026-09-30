using System.Globalization;
using System.Text.RegularExpressions;

namespace MhwModManager.Core;

public sealed record AutoProviderSelection(
    string? WinnerModId,
    string ReasonCode,
    string Explanation,
    Confidence Confidence,
    int Score = 0,
    string Evidence = "");

/// <summary>
/// Deterministic, conservative inference for common Monster Hunter: World packaging patterns:
/// install a main file, then let an optional component/patch overwrite only the files it changes;
/// and let dedicated/newer texture resources replace stale copies embedded in older armor packs.
/// Structural/game-data alternatives are never ordered merely because one has a newer timestamp.
/// </summary>
public static partial class AutoCompatibility
{
    private static readonly HashSet<string> VariantWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "patch", "fix", "hotfix", "update", "updated", "optional", "option", "addon", "add-on",
        "variant", "alternative", "alt", "top", "waist", "wst", "waste", "leg", "legs", "helm", "helmet",
        "head", "chest", "body", "arms", "arm", "cape", "nocape", "no-cape", "open", "opened",
        "skimpy", "thong", "invisible", "belt", "skirt", "bra", "jacket", "physics", "ctc", "ccl",
        "size", "sleeve", "sleeves", "fur", "pants", "shorts", "corset"
    };

    private static readonly HashSet<string> SafeOverlayWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "patch", "fix", "hotfix", "update", "updated", "optional", "option", "addon", "add-on",
        "top", "waist", "wst", "waste", "leg", "legs", "helm", "helmet", "head", "chest", "body",
        "arms", "arm", "cape", "nocape", "no-cape", "open", "opened", "skimpy", "thong",
        "invisible", "belt", "skirt", "bra", "jacket", "physics", "ctc", "ccl", "size",
        "sleeve", "sleeves", "fur", "pants", "shorts", "corset"
    };

    private static readonly HashSet<string> StrongPatchWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "patch", "fix", "hotfix", "update", "updated", "optional", "option", "addon", "add-on"
    };

    private static readonly HashSet<string> BaseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "base", "main", "default", "core", "original"
    };

    private static readonly HashSet<string> TextureWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "texture", "textures", "retexture", "retextures", "skin", "skins", "4k", "8k", "2k",
        "diffuse", "normal", "normals", "cleaner", "hd", "hires", "highres"
    };

    private static readonly HashSet<string> UpdateWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "update", "updated", "new", "newer", "latest", "revised", "revision", "refresh", "remaster",
        "fix", "fixed", "hotfix"
    };

    // Generic descriptive words are not a lineage identity. Two unrelated packages called
    // "Red Recolor" and "Blue Recolor" must not become one shared-resource family merely
    // because both contain the word "recolor" and happen to touch the same mod_* namespace.
    private static readonly HashSet<string> ResourceIdentityNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "recolor", "recolour", "color", "colour", "colors", "colours", "style", "styles",
        "armor", "armour", "outfit", "costume", "clothes", "clothing", "dress", "set", "pack",
        "mod", "mods", "replacement", "replacer", "female", "male", "girl", "boy", "character",
        "red", "blue", "green", "black", "white", "pink", "purple", "yellow", "orange", "brown",
        "variant", "alternative", "alternate", "option", "optional", "default", "main", "base"
    };

    private static readonly string[] FamilySeparators = [" - ", " – ", " — ", ": ", " | "];

    public static IReadOnlyList<ConflictRule> GenerateOverlayRules(
        IReadOnlyDictionary<string, ModDescriptor> enabledMods,
        IReadOnlyDictionary<(string,string), PairStats> pairStats,
        IReadOnlyList<ConflictRule> existingRules)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var explicitPairs = existingRules
            .Where(r => r.Explicit && r.Scope == RuleScope.ModPair && r.LeftModId is not null && r.RightModId is not null)
            .Select(r => ConflictEngine.PairKey(r.LeftModId!, r.RightModId!))
            .ToHashSet();

        var output = new List<ConflictRule>();
        var acceptedRules = existingRules.ToList();
        foreach (var (key, stats) in pairStats.OrderBy(x => x.Key.Item1, StringComparer.Ordinal).ThenBy(x => x.Key.Item2, StringComparer.Ordinal))
        {
            if (explicitPairs.Contains(key)) continue;
            if (!enabledMods.TryGetValue(key.Item1, out var left) || !enabledMods.TryGetValue(key.Item2, out var right)) continue;
            if (!TryInferOverlay(left, right, stats, out var parent, out var child, out var reason, out var confidence)) continue;
            if (confidence != Confidence.High) continue;

            var resolverScore=94;
            var evidence="High-confidence local package lineage/role inference.";
            if(ProvenanceIntelligence.TryNexusOverlay(left,right,stats,out _,out _,out _,out var nexusScore))
            {
                resolverScore=nexusScore;
                evidence="Nexus mod/file category or version-chain metadata establishes the overwrite direction.";
            }
            var rule = new ConflictRule(
                $"auto-overlay:{parent.Id.ToLowerInvariant()}:{child.Id.ToLowerInvariant()}",
                RuleKind.Overlay,
                RuleScope.ModPair,
                parent.Id,
                child.Id,
                child.Id,
                null,
                reason,
                false,
                DateTimeOffset.UnixEpoch,
                resolverScore,
                evidence);

            // Never let inference create a precedence cycle around remembered human rules.
            if (RuleGraph.FindCycle(acceptedRules.Append(rule)) is not null) continue;
            output.Add(rule);
            acceptedRules.Add(rule);
        }
        return output;
    }

    public static bool TryInferOverlay(
        ModDescriptor a,
        ModDescriptor b,
        PairStats stats,
        out ModDescriptor parent,
        out ModDescriptor child,
        out string reason,
        out Confidence confidence)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        parent = a;
        child = b;
        reason = string.Empty;
        confidence = Confidence.Low;

        if (ProvenanceIntelligence.TryNexusOverlay(a,b,stats,out parent,out child,out reason,out var nexusScore))
        {
            confidence = nexusScore >= 98 ? Confidence.High : Confidence.Medium;
            return true;
        }

        var an = CleanName(a.Name);
        var bn = CleanName(b.Name);

        if (TryPrefixFamily(a, an, b, bn, stats, out parent, out child, out var suffix))
        {
            var suffixTokens = Tokenize(suffix);
            var safeOverlaySignal = suffixTokens.Any(SafeOverlayWords.Contains);
            var ambiguousVariantSignal = suffixTokens.Any(VariantWords.Contains);
            confidence = safeOverlaySignal ? Confidence.High : Confidence.Medium;
            reason = safeOverlaySignal
                ? $"Auto-composed main + optional family: '{child.DisplayName}' is an overwrite-style option/patch of '{parent.DisplayName}' ({suffix}). Both stay enabled; the optional package wins only the files it intentionally replaces."
                : ambiguousVariantSignal
                    ? $"'{child.DisplayName}' looks like an alternative/variant of '{parent.DisplayName}', but that label alone is not enough to auto-order structural files."
                    : $"Likely base + child family: '{child.DisplayName}' extends '{parent.DisplayName}' and overlaps {stats.SharedPaths} file(s), but the relationship is not explicit enough to auto-order structural files.";
            return true;
        }

        if (TryRoleFamily(a, b, stats, out parent, out child, out reason))
        {
            confidence = Confidence.High;
            return true;
        }

        var smaller = stats.LeftFiles <= stats.RightFiles ? a : b;
        var larger = ReferenceEquals(smaller, a) ? b : a;
        var smallerName = CleanName(smaller.Name);
        var largerName = CleanName(larger.Name);
        var smallerTokens = Tokenize(smallerName);
        var hasStrongPatchWord = smallerTokens.Any(StrongPatchWords.Contains);
        var similarity = TokenSimilarity(Tokenize(largerName), smallerTokens);

        // Do not invent an order between same-labelled packages (for example several different
        // "Fatalis Patch" option archives). They need a stronger family/revision signal.
        var equivalentLabels = StringComparer.OrdinalIgnoreCase.Equals(smallerName, largerName);
        if (!equivalentLabels && hasStrongPatchWord && stats.SmallerOverlapRatio >= .75 && Math.Min(stats.LeftFiles, stats.RightFiles) <= 64)
        {
            parent = larger;
            child = smaller;
            confidence = similarity >= .35 || stats.SmallerOverlapRatio >= .95 ? Confidence.High : Confidence.Medium;
            reason = $"Auto-composed patch subset: '{child.DisplayName}' looks like a patch/optional package and replaces {stats.SharedPaths}/{Math.Min(stats.LeftFiles, stats.RightFiles)} of its files in '{parent.DisplayName}'.";
            return true;
        }

        // Saved family IDs are a grouping hint, but automatic precedence still requires a smaller,
        // variant-shaped child so two legitimate alternatives are not silently ordered.
        if (!string.IsNullOrWhiteSpace(a.FamilyId) &&
            StringComparer.OrdinalIgnoreCase.Equals(a.FamilyId, b.FamilyId) &&
            stats.SmallerOverlapRatio >= .85 && Math.Min(stats.LeftFiles, stats.RightFiles) <= 64)
        {
            if (smallerTokens.Any(SafeOverlayWords.Contains))
            {
                parent = larger;
                child = smaller;
                confidence = Confidence.High;
                reason = $"Auto-composed saved mod family: '{child.DisplayName}' is a small variant within family '{a.FamilyId}' and overrides its parent on shared files.";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Chooses the physical provider for one texture path after known base/option overlay losers
    /// have already been removed. Dedicated texture packs beat incidental copies embedded in armor
    /// packages. Within a confidently related texture lineage, newer revisions beat older ones.
    /// Otherwise explicit mod priority remains the deterministic fallback.
    /// </summary>
    public static AutoProviderSelection SelectTextureProvider(
        string path,
        IReadOnlyList<ProviderCandidate> candidates,
        IReadOnlyDictionary<string, ModDescriptor> mods,
        IReadOnlyDictionary<string, ModContentStats> contentStats)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (candidates.Count == 0) throw new ArgumentException("At least one texture provider is required.", nameof(candidates));
        if (candidates.Count == 1)
            return new(candidates[0].ModId, "single-texture-root", "Only one eligible texture provider remains after overlay composition.", Confidence.High, 100, "Single eligible provider after composition.");

        // Resolve dedicated resource packs globally before pairwise lineage checks so 3+ provider
        // outcomes never depend on mod-id ordering. One dedicated pack intentionally replaces stale
        // copies embedded in broader armor mods; multiple independent dedicated replacers are a real
        // visual choice and remain blocking below.
        var dedicated = candidates.Where(c =>
        {
            var mod=GetMod(c,mods);
            contentStats.TryGetValue(c.ModId,out var stats);
            return IsDedicatedTexturePack(mod,stats);
        }).OrderBy(c=>c.ModId,StringComparer.OrdinalIgnoreCase).ToArray();
        if(dedicated.Length==1)
        {
            var chosen=dedicated[0];
            return new(chosen.ModId,"dedicated-texture-provider",
                $"'{chosen.ModName}' is the only dedicated texture/skin package among {candidates.Count} providers, so it overrides incidental shared copies embedded in broader mods. Every source mod remains enabled.",
                Confidence.High,94,"Single dedicated texture pack globally dominates incidental embedded texture copies.");
        }

        // Many armor/outfit packages embed a copy of the same shared body/skin texture namespace.
        // Those copies are resources used by otherwise independent structural mods, not proof that
        // the outfits themselves conflict. When every provider is a broader non-texture package,
        // pick one exact-path provider deterministically and keep every source mod enabled.
        // Dedicated texture/recolor packs intentionally do NOT use this fallback.
        if (PathRules.ResourceNamespace(path) is not null && dedicated.Length == 0 &&
            candidates.All(c => IsIncidentalEmbeddedTexturePack(GetMod(c, mods), contentStats.GetValueOrDefault(c.ModId))))
        {
            var chosen = candidates
                .OrderByDescending(c => c.Priority)
                .ThenBy(c => c.ModId, StringComparer.OrdinalIgnoreCase)
                .First();
            return new(chosen.ModId, "shared-embedded-resource",
                $"{candidates.Count} broader armor/outfit packages embed this shared texture resource. '{chosen.ModName}' wins only this exact texture path by configured priority; the outfit/model portions of every mod remain enabled.",
                Confidence.High, 93, "Shared resource namespace plus every provider being a broader structural/outfit package rather than a dedicated texture replacer.");
        }

        var ordered = (dedicated.Length>1?dedicated:candidates)
            .OrderBy(c => c.ModId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Two providers can be decided directly. For 3+ providers, a sequential tournament is unsafe:
        // a later challenger can replace the provisional winner without ever being compared against
        // providers that the provisional winner already beat. Require one provider to beat every
        // other provider under the same pairwise semantics before choosing it automatically.
        if (ordered.Length == 2)
        {
            var pair = CompareTextureProviders(path, ordered[0], ordered[1], mods, contentStats);
            if (pair.WinnerModId is not null)
                return pair;

            return new(null, "independent-texture-replacement",
                $"'{ordered[0].ModName}' and '{ordered[1].ModName}' both replace the same texture but no trustworthy main/optional/update/shared-resource relationship was proven.",
                Confidence.High, 100, "Independent mods provide different bytes at the exact same texture path.");
        }

        var wins = ordered.ToDictionary(c => c.ModId, _ => 0, PathRules.Comparer);
        var pairProof = new List<AutoProviderSelection>();
        for (var i = 0; i < ordered.Length - 1; i++)
        for (var j = i + 1; j < ordered.Length; j++)
        {
            var left = ordered[i];
            var right = ordered[j];
            var preference = CompareTextureProviders(path, left, right, mods, contentStats);
            if (preference.WinnerModId is null)
            {
                return new(null, "independent-texture-replacement",
                    $"'{left.ModName}' and '{right.ModName}' both replace the same texture but no trustworthy main/optional/update/shared-resource relationship was proven. The full provider set remains unresolved.",
                    Confidence.High, 100, "At least one provider pair is incomparable; automatic multi-provider ordering is unsafe.");
            }

            if (!PathRules.Comparer.Equals(preference.WinnerModId, left.ModId) &&
                !PathRules.Comparer.Equals(preference.WinnerModId, right.ModId))
            {
                return new(null, "invalid-texture-precedence",
                    "Texture precedence evidence named a winner that is not one of the compared providers. Deployment is blocked rather than falling back to priority.",
                    Confidence.High, 100, "Resolver invariant violation: pairwise winner must belong to the compared pair.");
            }

            if (preference.Confidence is Confidence.Medium or Confidence.Low)
            {
                return new(null, "texture-evidence-insufficient",
                    $"'{left.ModName}' and '{right.ModName}' have only {preference.Confidence.ToString().ToLowerInvariant()}-confidence precedence evidence. Multi-provider automatic ordering requires high or explicit evidence for every supporting pair.",
                    Confidence.High,
                    Math.Max(95, preference.Score),
                    string.IsNullOrWhiteSpace(preference.Evidence)
                        ? "At least one required pairwise texture preference was below high confidence."
                        : preference.Evidence);
            }

            wins[preference.WinnerModId]++;
            pairProof.Add(preference);
        }

        var requiredWins = ordered.Length - 1;
        var dominant = ordered.Where(c => wins[c.ModId] == requiredWins).ToArray();
        if (dominant.Length != 1)
        {
            return new(null, "ambiguous-texture-precedence",
                $"{ordered.Length} texture providers have pairwise precedence evidence, but no single provider is proven to dominate every other provider. Choose the intended provider instead of relying on order-dependent inference.",
                Confidence.High, 100, "Complete all-pairs dominance proof failed; precedence may be cyclic or otherwise non-transitive.");
        }

        var winner = dominant[0];
        var winnerProof = pairProof
            .Where(p => p.WinnerModId is not null && PathRules.Comparer.Equals(p.WinnerModId, winner.ModId))
            .ToArray();
        var score = winnerProof.Length == 0 ? 90 : winnerProof.Min(p => p.Score);
        var evidence = string.Join(" | ", winnerProof
            .Select(p => string.IsNullOrWhiteSpace(p.Evidence) ? p.ReasonCode : p.Evidence)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return new(winner.ModId, "texture-complete-dominance",
            $"'{winner.ModName}' is the only provider proven to beat every other eligible provider for this texture path. All source mods remain enabled; only this exact path uses the proven dominant provider.",
            Confidence.High, score, evidence);
    }

    private static AutoProviderSelection CompareTextureProviders(
        string path,
        ProviderCandidate a,
        ProviderCandidate b,
        IReadOnlyDictionary<string, ModDescriptor> mods,
        IReadOnlyDictionary<string, ModContentStats> contentStats)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var am = GetMod(a, mods);
        var bm = GetMod(b, mods);
        contentStats.TryGetValue(a.ModId, out var aStats);
        contentStats.TryGetValue(b.ModId, out var bStats);

        var aDedicated = IsDedicatedTexturePack(am, aStats);
        var bDedicated = IsDedicatedTexturePack(bm, bStats);
        if (aDedicated != bDedicated)
        {
            var chosen = aDedicated ? a : b;
            var other = aDedicated ? b : a;
            return new(chosen.ModId, "dedicated-texture-provider",
                $"'{chosen.ModName}' is a dedicated texture/skin package, so its texture overrides the incidental older/shared copy supplied by '{other.ModName}'. Both mods remain enabled.",
                Confidence.High, 94, "Dedicated texture pack beats an incidental texture copy embedded in a broader armor/package mod.");
        }

        var sameLineage = AreTextureLineageRelated(path, am, bm);
        if (sameLineage)
        {
            var revision = CompareRevisionMetadata(am, bm);
            if (revision != 0)
            {
                var chosen = revision > 0 ? a : b;
                var other = revision > 0 ? b : a;
                var metadataScore = HasStrongNexusRevisionEvidence(am,bm) ? 99 : 96;
                return new(chosen.ModId, "newer-texture-revision",
                    $"'{chosen.ModName}' is the newer revision of the same texture/resource lineage, so it supersedes '{other.ModName}' for this shared texture path.",
                    Confidence.High, metadataScore, metadataScore >= 99 ? "Nexus version-chain/upload metadata identifies the newer resource revision." : "Version/date metadata identifies the newer resource revision within a proven texture lineage.");
            }

            var aUpdated = HasUpdateSignal(am.Name);
            var bUpdated = HasUpdateSignal(bm.Name);
            if (aUpdated != bUpdated)
            {
                var chosen = aUpdated ? a : b;
                var other = aUpdated ? b : a;
                return new(chosen.ModId, "updated-texture-provider",
                    $"'{chosen.ModName}' is explicitly labelled as an updated/fixed texture resource and therefore overrides the older shared copy from '{other.ModName}'.",
                    Confidence.High, 92, "Same texture lineage plus explicit updated/fix naming.");
            }

            // File timestamps are a last-resort revision hint only inside an already proven texture
            // lineage. Explicit "updated/fix" naming wins before timestamp metadata because archives
            // may preserve source-file times from older authoring sessions.
            if (aStats is not null && bStats is not null && aStats.LatestWriteUtc != bStats.LatestWriteUtc)
            {
                var chosen = aStats.LatestWriteUtc > bStats.LatestWriteUtc ? a : b;
                var other = ReferenceEquals(chosen, a) ? b : a;
                return new(chosen.ModId, "newer-texture-filetime",
                    $"'{chosen.ModName}' has the newer file revision inside the same proven texture lineage and supersedes '{other.ModName}' for this path.",
                    Confidence.High, 86, "Same proven texture lineage plus newer source file timestamp.");
            }

            var priorityChosen = a.Priority == b.Priority
                ? (StringComparer.OrdinalIgnoreCase.Compare(a.ModId,b.ModId)>=0?a:b)
                : (a.Priority>b.Priority?a:b);
            return new(priorityChosen.ModId,"same-lineage-priority",
                $"'{a.ModName}' and '{b.ModName}' are proven members of the same shared texture lineage; configured priority breaks the tie without disabling either source mod.",
                Confidence.Medium,82,"Same proven texture lineage; no newer revision signal, so profile priority is a safe tie-breaker inside that lineage only.");
        }

        return new(null, string.Empty, string.Empty, Confidence.Low, 0, "No trustworthy semantic winner was found.");
    }

    private static ModDescriptor GetMod(ProviderCandidate candidate, IReadOnlyDictionary<string, ModDescriptor> mods) =>
        mods.TryGetValue(candidate.ModId, out var mod)
            ? mod
            : new(candidate.ModId, candidate.ModName, candidate.ModName, string.Empty, true, candidate.Priority);

    private static bool IsDedicatedTexturePack(ModDescriptor mod, ModContentStats? stats)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tokens = Tokenize(mod.Name);
        var textureNamed = tokens.Any(TextureWords.Contains) ||
                           mod.Name.Contains("body texture", StringComparison.OrdinalIgnoreCase) ||
                           mod.Name.Contains("face texture", StringComparison.OrdinalIgnoreCase);
        if (!textureNamed) return false;
        return stats is null || stats.TotalFiles == 0 || stats.TextureRatio >= .60;
    }

    private static bool IsIncidentalEmbeddedTexturePack(ModDescriptor mod, ModContentStats? stats)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (IsDedicatedTexturePack(mod, stats)) return false;
        if (stats is null || stats.TotalFiles == 0) return false;
        return stats.StructuralFiles > 0 || stats.GameDataFiles > 0 || stats.TextureRatio < .60;
    }

    private static bool AreTextureLineageRelated(string path, ModDescriptor a, ModDescriptor b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.IsNullOrWhiteSpace(a.FamilyId) && StringComparer.OrdinalIgnoreCase.Equals(a.FamilyId, b.FamilyId)) return true;
        if (!string.IsNullOrWhiteSpace(a.NexusModId) && StringComparer.OrdinalIgnoreCase.Equals(a.NexusModId, b.NexusModId) &&
            StringComparer.OrdinalIgnoreCase.Equals(FamilyStem(a.Name), FamilyStem(b.Name))) return true;

        var aStem = FamilyStem(a.Name);
        var bStem = FamilyStem(b.Name);
        if (aStem.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(aStem, bStem)) return true;

        // Shared resource namespaces are useful supporting evidence, but never enough on their own.
        // Require a distinctive series/name token shared by both providers so this works for future
        // body/texture ecosystems without hard-coding a specific author, body system, or namespace.
        var resourceNamespace = PathRules.ResourceNamespace(path);
        if (resourceNamespace is not null)
        {
            var at = IdentityTokens(Tokenize(a.Name));
            var bt = IdentityTokens(Tokenize(b.Name));
            var sharedDistinctive = at.Intersect(bt, StringComparer.OrdinalIgnoreCase)
                .Any(t => t.Length >= 4 && !TextureWords.Contains(t) && !ResourceIdentityNoiseWords.Contains(t));
            if (sharedDistinctive) return true;
        }

        return false;
    }

    private static int CompareRevisionMetadata(ModDescriptor a, ModDescriptor b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(ProvenanceIntelligence.SameNexusMod(a,b))
        {
            if(!string.IsNullOrWhiteSpace(a.NexusVersionId)&&StringComparer.OrdinalIgnoreCase.Equals(a.NexusPreviousVersionId,b.NexusVersionId))return 1;
            if(!string.IsNullOrWhiteSpace(b.NexusVersionId)&&StringComparer.OrdinalIgnoreCase.Equals(b.NexusPreviousVersionId,a.NexusVersionId))return -1;
            if(a.NexusUploadedAt.HasValue&&b.NexusUploadedAt.HasValue&&a.NexusUploadedAt!=b.NexusUploadedAt)
                return a.NexusUploadedAt.Value.CompareTo(b.NexusUploadedAt.Value);
        }

        if (TryGetUnixTimestamp(a, out var at) && TryGetUnixTimestamp(b, out var bt) && at != bt)
            return at.CompareTo(bt);

        if (TryGetDateStamp(a, out var ad) && TryGetDateStamp(b, out var bd) && ad != bd)
            return ad.CompareTo(bd);

        if (TryGetVersion(a, out var av) && TryGetVersion(b, out var bv))
        {
            var compare = CompareVersion(av, bv);
            if (compare != 0) return compare;
        }

        return 0;
    }

    private static bool HasStrongNexusRevisionEvidence(ModDescriptor a,ModDescriptor b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!ProvenanceIntelligence.SameNexusMod(a,b))return false;
        if(!string.IsNullOrWhiteSpace(a.NexusVersionId)&&StringComparer.OrdinalIgnoreCase.Equals(a.NexusPreviousVersionId,b.NexusVersionId))return true;
        if(!string.IsNullOrWhiteSpace(b.NexusVersionId)&&StringComparer.OrdinalIgnoreCase.Equals(b.NexusPreviousVersionId,a.NexusVersionId))return true;
        return a.NexusUploadedAt.HasValue&&b.NexusUploadedAt.HasValue&&a.NexusUploadedAt!=b.NexusUploadedAt;
    }

    private static bool TryRoleFamily(
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
        if (stats.SmallerOverlapRatio < .65) return false;

        var aTokens = Tokenize(CleanName(a.Name));
        var bTokens = Tokenize(CleanName(b.Name));
        var aBase = aTokens.Any(BaseWords.Contains);
        var bBase = bTokens.Any(BaseWords.Contains);

        if (aBase == bBase) return false;
        var baseMod = aBase ? a : b;
        var variantMod = aBase ? b : a;
        var variantTokens = aBase ? bTokens : aTokens;
        if (!variantTokens.Any(SafeOverlayWords.Contains)) return false;

        var baseIdentity = IdentityTokens(aBase ? aTokens : bTokens);
        var variantIdentity = IdentityTokens(variantTokens);
        if (TokenSimilarity(baseIdentity, variantIdentity) < .55) return false;

        parent = baseMod;
        child = variantMod;
        reason = $"Auto-composed explicit main/base + optional variant: '{child.DisplayName}' overrides '{parent.DisplayName}' only where their files overlap.";
        return true;
    }

    private static string[] IdentityTokens(string[] tokens) =>
        tokens.Where(t => !BaseWords.Contains(t) && !VariantWords.Contains(t) && !UpdateWords.Contains(t))
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToArray();

    private static bool TryPrefixFamily(
        ModDescriptor a, string an,
        ModDescriptor b, string bn,
        PairStats stats,
        out ModDescriptor parent,
        out ModDescriptor child,
        out string suffix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (TryChildOf(an, bn, out suffix))
        {
            parent = a;
            child = b;
            return true;
        }
        if (TryChildOf(bn, an, out suffix))
        {
            parent = b;
            child = a;
            return true;
        }

        // Common Nexus packaging: "Main Name" + "Main Name Optional Thing" without punctuation.
        if (stats.SmallerOverlapRatio >= .90)
        {
            if (bn.Length > an.Length && bn.StartsWith(an + " ", StringComparison.OrdinalIgnoreCase))
            {
                parent = a; child = b; suffix = bn[(an.Length + 1)..]; return true;
            }
            if (an.Length > bn.Length && an.StartsWith(bn + " ", StringComparison.OrdinalIgnoreCase))
            {
                parent = b; child = a; suffix = an[(bn.Length + 1)..]; return true;
            }
        }

        parent = a;
        child = b;
        suffix = string.Empty;
        return false;
    }

    private static bool TryChildOf(string parent, string child, out string suffix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var separator in FamilySeparators)
        {
            var prefix = parent + separator;
            if (!child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            suffix = child[prefix.Length..].Trim();
            return suffix.Length > 0;
        }
        suffix = string.Empty;
        return false;
    }

    private static bool HasUpdateSignal(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Tokenize(name).Any(UpdateWords.Contains);
    }

    private static string FamilyStem(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tokens = Tokenize(CleanName(name));
        return string.Join(' ', tokens.Where(t => !BaseWords.Contains(t) && !VariantWords.Contains(t) &&
                                                   !UpdateWords.Contains(t) && !VersionTokenRegex().IsMatch(t)));
    }

    private static string CleanName(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var n = NexusSuffixRegex().Replace(name, string.Empty).Trim();
        n = VersionSuffixRegex().Replace(n, string.Empty).Trim();
        return WhitespaceRegex().Replace(n, " ");
    }

    private static string[] Tokenize(string name) =>
        TokenRegex().Matches(name.ToLowerInvariant()).Cast<Match>().Select(m => m.Value).Where(x => x.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static double TokenSimilarity(string[] left, string[] right)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (left.Length == 0 || right.Length == 0) return 0;
        var l = left.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var r = right.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intersection = l.Count(r.Contains);
        var union = l.Count + r.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static bool TryGetUnixTimestamp(ModDescriptor mod, out long timestamp)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var source in CandidateRevisionStrings(mod))
        {
            var matches = UnixTimestampRegex().Matches(source);
            for (var i = matches.Count - 1; i >= 0; i--)
                if (long.TryParse(matches[i].Value, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp)) return true;
        }
        timestamp = 0;
        return false;
    }

    private static bool TryGetDateStamp(ModDescriptor mod, out int stamp)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var source in CandidateRevisionStrings(mod))
        {
            var m = DateStampRegex().Match(source);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
                !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
                !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var day)) continue;
            if (month is < 1 or > 12 || day is < 1 or > 31) continue;
            stamp = checked(year * 10000 + month * 100 + day);
            return true;
        }
        stamp = 0;
        return false;
    }

    private static bool TryGetVersion(ModDescriptor mod, out int[] version)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var source in CandidateRevisionStrings(mod))
        {
            var matches = VersionRegex().Matches(source);
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var value = matches[i].Groups[1].Success ? matches[i].Groups[1].Value : matches[i].Groups[2].Value;
                var parts = value.Split('.');
                var parsed = new int[parts.Length];
                var ok = true;
                for (var p = 0; p < parts.Length; p++)
                {
                    if (!int.TryParse(parts[p], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[p])) { ok = false; break; }
                }
                if (ok) { version = parsed; return true; }
            }
        }
        version = [];
        return false;
    }

    private static int CompareVersion(int[] a, int[] b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var count = Math.Max(a.Length, b.Length);
        for (var i = 0; i < count; i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    private static IEnumerable<string> CandidateRevisionStrings(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        yield return mod.Name;
        yield return mod.DisplayName;
        if (!string.IsNullOrWhiteSpace(mod.SourcePath)) yield return mod.SourcePath;
    }


    [GeneratedRegex(@"-\d{2,7}-\d+(?:-\d+){0,5}(?:\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NexusSuffixRegex();

    [GeneratedRegex(@"(?:\s+|[-_])v?\d+(?:\.\d+){1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffixRegex();

    [GeneratedRegex(@"[a-z0-9]+(?:-[a-z0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<!\d)(?:1[5-9]\d{8}|2\d{9})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex UnixTimestampRegex();

    [GeneratedRegex(@"(?<!\d)(20\d{2})[-_. ](0?[1-9]|1[0-2])[-_. ](0?[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DateStampRegex();

    [GeneratedRegex(@"(?:^|[\s_\-])(?:v(\d{1,3}(?:\.\d{1,4}){0,3})|(\d{1,3}(?:\.\d{1,4}){1,3}))(?:$|[\s_\-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"^v?\d+(?:\.\d+){0,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionTokenRegex();
}
