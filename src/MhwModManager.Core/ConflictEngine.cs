using System.Diagnostics.CodeAnalysis;

namespace MhwModManager.Core;

public sealed class ConflictEngine
{
    private readonly bool richMhwSemantics;

    public ConflictEngine(GameProfile? game = null)
    {
        richMhwSemantics = GameAdapters.Resolve(game).SupportsMhwConflictSemantics;
    }
    /// <summary>Compatibility overload used by focused unit tests and external callers.</summary>
    public ConflictDecision Decide(
        string path,
        IReadOnlyList<ProviderCandidate> candidates,
        IReadOnlyList<ConflictRule> rules,
        IReadOnlyDictionary<string,string> exactWinners,
        IReadOnlyDictionary<string,string> resourceProviders,
        IReadOnlyDictionary<(string,string),PairStats> pairStats) =>
        Decide(path, candidates, ConflictRuleIndex.Create(rules), exactWinners, resourceProviders, pairStats);

    /// <summary>Compatibility overload for callers that already built the rule index.</summary>
    public ConflictDecision Decide(
        string path,
        IReadOnlyList<ProviderCandidate> candidates,
        ConflictRuleIndex ruleIndex,
        IReadOnlyDictionary<string,string> exactWinners,
        IReadOnlyDictionary<string,string> resourceProviders,
        IReadOnlyDictionary<(string,string),PairStats> pairStats)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = candidates
            .GroupBy(c => c.ModId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new ModDescriptor(g.Key, g.First().ModName, g.First().ModName, string.Empty, true, g.First().Priority), StringComparer.OrdinalIgnoreCase);
        var stats = candidates
            .GroupBy(c => c.ModId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new ModContentStats(
                    g.Count(),
                    g.Count(x => x.File.FileClass == FileClass.Texture),
                    g.Count(x => x.File.FileClass == FileClass.Structural),
                    g.Count(x => x.File.FileClass == FileClass.GameData),
                    g.Max(x => x.File.LastWriteUtc)),
                StringComparer.OrdinalIgnoreCase);
        return Decide(path, candidates, ruleIndex, exactWinners, resourceProviders, pairStats, mods, stats);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ConflictEngine is intentionally an injectable instance service used by DeploymentPlanner.")]
    public ConflictDecision Decide(
        string path,
        IReadOnlyList<ProviderCandidate> candidates,
        ConflictRuleIndex ruleIndex,
        IReadOnlyDictionary<string,string> exactWinners,
        IReadOnlyDictionary<string,string> resourceProviders,
        IReadOnlyDictionary<(string,string),PairStats> pairStats,
        IReadOnlyDictionary<string,ModDescriptor> enabledMods,
        IReadOnlyDictionary<string,ModContentStats> contentStats)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (candidates.Count == 0) throw new ArgumentException("At least one provider is required.", nameof(candidates));
        if (candidates.Count == 1)
            return new(path, ConflictKind.None, false, candidates[0].ModId, "single-provider", "Only one enabled mod supplies this path.", Confidence.High, ResolverScore:100, Evidence:"Only one enabled provider.");

        var candidateIds = candidates.Select(c => c.ModId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incompatible = ruleIndex.FindIncompatible(candidateIds);
        if (incompatible is not null)
            return new(path, ConflictKind.Incompatible, true, null, "explicit-incompatible", "Two enabled providers are explicitly marked incompatible.", Confidence.Explicit, incompatible.Id);

        if (exactWinners.TryGetValue(path, out var exact) && candidateIds.Contains(exact))
            return new(path, ConflictKind.UserOverlayRule, false, exact, "exact-file-winner", "A saved exact-file winner applies.", Confidence.Explicit, ResolverScore:100, Evidence:"Explicit human exact-path rule.");

        var firstHash = candidates[0].File.BlobSha256;
        if (candidates.All(c => StringComparer.OrdinalIgnoreCase.Equals(c.File.BlobSha256,firstHash)))
            return new(path, ConflictKind.Identical, false, candidates.MaxBy(c => c.Priority)!.ModId, "identical-bytes", "Every provider contains byte-identical content, so only one physical copy is needed.", Confidence.High, Inferred:true, ResolverScore:100, Evidence:"All SHA-256 hashes are identical.");

        var resourceNamespace = richMhwSemantics ? PathRules.ResourceNamespace(path) : null;
        if (resourceNamespace is not null && resourceProviders.TryGetValue(resourceNamespace, out var resourceWinner) && candidateIds.Contains(resourceWinner))
            return new(path, ConflictKind.SharedProvider, false, resourceWinner, "resource-provider", $"The shared resource namespace '{resourceNamespace}' has a pinned provider. All dependent mods remain enabled.", Confidence.Explicit);

        // A complete overlay chain means every colliding provider is a known base/option/patch
        // ancestor of one final provider. Resolve it as one virtual merged stack even for 3+ mods.
        var orderedOverlay = ruleIndex.FindFullyOrderedOverlayWinner(candidateIds);
        if (orderedOverlay is not null)
        {
            var inferred = !orderedOverlay.Rule.Explicit;
            return new(path,
                inferred ? ConflictKind.ModFamilyOption : ConflictKind.UserOverlayRule,
                false,
                orderedOverlay.WinnerModId,
                inferred ? "auto-composed-overlay" : "remembered-overlay",
                orderedOverlay.Rule.Reason,
                inferred ? Confidence.High : Confidence.Explicit,
                orderedOverlay.Rule.Id,
                inferred,
                inferred ? (orderedOverlay.Rule.ResolverScore>0?orderedOverlay.Rule.ResolverScore:94) : 100,
                inferred ? (orderedOverlay.Rule.Evidence??"High-confidence base/optional/patch precedence chain.") : "Explicit human overlay precedence rule.");
        }

        // Hard invariant: packages that have already been proven to belong to the same logical
        // family are composed *inside* that family before ordinary inter-mod conflict handling.
        // This prevents a base mod from surfacing as HARD_STRUCTURAL against its own optional
        // component merely because a naming heuristic missed the precise overwrite direction.
        // Explicit incompatible/exact-winner/resource-provider rules above still retain authority.
        if (TryResolveSameFamily(path, candidates, candidateIds, pairStats, enabledMods, contentStats, out var familyDecision))
            return familyDecision;

        string? possibleOverlayReason = null;

        // Direct pair fallback retained for callers that invoke ConflictEngine without the planner's
        // generated overlay rules. It uses the same high-confidence inference as the planner; old
        // prefix-only shortcuts are intentionally forbidden because "Alternative" can mean pick-one.
        if (richMhwSemantics && candidates.Count == 2)
        {
            var a = candidates[0];
            var b = candidates[1];
            var key = PairKey(a.ModId,b.ModId);
            var stats = pairStats.TryGetValue(key, out var knownStats) ? knownStats : new PairStats(1,1,1);
            var am = enabledMods.TryGetValue(a.ModId, out var knownA) ? knownA : new ModDescriptor(a.ModId,a.ModName,a.ModName,string.Empty,true,a.Priority);
            var bm = enabledMods.TryGetValue(b.ModId, out var knownB) ? knownB : new ModDescriptor(b.ModId,b.ModName,b.ModName,string.Empty,true,b.Priority);
            if (AutoCompatibility.TryInferOverlay(am,bm,stats,out _,out var child,out var reason,out var confidence))
            {
                if (confidence == Confidence.High)
                    return new(path, ConflictKind.PatchOverlay, false, child.Id, "high-confidence-auto-overlay", reason, Confidence.High, Inferred:true, ResolverScore:94, Evidence:"High-confidence package lineage/role inference.");
                if (confidence == Confidence.Medium) possibleOverlayReason = reason;
            }
        }

        var fileClass = PathRules.ClassifyFile(path);
        if (fileClass == FileClass.Texture && !richMhwSemantics)
        {
            return new(path, ConflictKind.TextureOverride, true, null, "generic-texture-choice",
                "Multiple enabled mods provide different bytes for the same texture path. This generic game profile will not guess a winner; choose or chain the intended provider.",
                Confidence.High, Inferred:false, ResolverScore:100, Evidence:"Generic adapter safety boundary: same-path different-byte texture collision.");
        }
        if (fileClass == FileClass.Texture)
        {
            // MHW can expose only one physical file at a nativePC path. Keep all mods enabled, first
            // remove known base providers through overlay precedence, then choose the correct texture
            // provider using semantic resource/revision rules. Priority is only the final fallback.
            var roots = ruleIndex.GetOverlayRoots(candidateIds);
            var eligible = (roots.Count == 0
                    ? candidates
                    : candidates.Where(c => roots.Contains(c.ModId, PathRules.Comparer)))
                .ToArray();
            var selection = AutoCompatibility.SelectTextureProvider(path, eligible, enabledMods, contentStats);
            var kind = resourceNamespace is not null ? ConflictKind.SharedTexture : ConflictKind.TextureOverride;
            var composedPrefix = roots.Count < candidates.Count
                ? "Known main/base providers were shadowed by their optional/patch descendants first. "
                : string.Empty;
            if(selection.WinnerModId is null)
                return new(path,ConflictKind.TextureOverride,true,null,selection.ReasonCode,composedPrefix+selection.Explanation,selection.Confidence,Inferred:true,ResolverScore:selection.Score,Evidence:selection.Evidence);
            return new(path, kind, false, selection.WinnerModId,
                selection.ReasonCode,
                composedPrefix + selection.Explanation + " All source mods remain enabled; only this exact path has one final provider in the composed nativePC tree.",
                selection.Confidence,
                Inferred:true,
                ResolverScore:selection.Score,
                Evidence:selection.Evidence);
        }

        if (possibleOverlayReason is not null)
            return new(path, ConflictKind.PossibleOverlay, true, null, "possible-overlay", possibleOverlayReason, Confidence.Medium, Inferred:true);

        return fileClass switch
        {
            FileClass.Structural => new(path, ConflictKind.HardStructural, true, null, "structural-collision", "Different model/material/physics files occupy the same path and no proven main→optional relationship exists.", Confidence.High),
            FileClass.GameData or FileClass.Plugin or FileClass.Executable => new(path, ConflictKind.HardGameData, true, null, "game-data-collision", "Different game/plugin data occupy the same path and cannot be safely combined automatically.", Confidence.High),
            _ => new(path, ConflictKind.HardUnknown, true, null, "unknown-binary-collision", "Different non-texture files occupy the same path and cannot be combined safely.", Confidence.High)
        };
    }

    /// <summary>Canonical pair key so dictionary lookups remain case-insensitive without a custom tuple comparer.</summary>
    private static bool TryResolveSameFamily(
        string path,
        IReadOnlyList<ProviderCandidate> candidates,
        HashSet<string> candidateIds,
        IReadOnlyDictionary<(string,string),PairStats> pairStats,
        IReadOnlyDictionary<string,ModDescriptor> enabledMods,
        IReadOnlyDictionary<string,ModContentStats> contentStats,
        out ConflictDecision decision)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        decision = default!;
        if (candidateIds.Count < 2) return false;

        string? familyId = null;
        foreach (var id in candidateIds)
        {
            if (!enabledMods.TryGetValue(id, out var mod) || string.IsNullOrWhiteSpace(mod.FamilyId)) return false;
            if (familyId is null) familyId = mod.FamilyId;
            else if (!StringComparer.OrdinalIgnoreCase.Equals(familyId, mod.FamilyId)) return false;
        }
        if (familyId is null) return false;

        // Texture files are intrinsically single-provider at a nativePC path. Once every provider
        // has already been proven to belong to the same logical family, resolve that internal
        // texture layer deterministically instead of surfacing a self-conflict. Explicit overlay
        // rules were handled above, so configured priority is only the family-internal fallback.
        if (PathRules.ClassifyFile(path) == FileClass.Texture)
        {
            var winner = candidates
                .OrderByDescending(c => c.Priority)
                .ThenByDescending(c => c.ModId, StringComparer.OrdinalIgnoreCase)
                .First();
            MasterDebugLog.Write("FAMILY-CONFLICT", $"COMPOSE family={familyId}; path={path}; winner={winner.ModId}; reason=family-texture-priority");
            decision = new(path, ConflictKind.ModFamilyOption, false, winner.ModId,
                "family-texture-priority",
                $"All providers of this texture are members of logical family '{familyId}'. The configured family priority selects '{winner.ModName}' for this exact path while keeping the family enabled.",
                Confidence.High,
                Inferred:true,
                ResolverScore:92,
                Evidence:$"Shared logical family '{familyId}'; deterministic family-internal texture priority.");
            return true;
        }

        // For a two-member family, keep using semantic/provenance inference when it can establish
        // a real parent -> optional/patch direction. Proven family membership then acts as a hard
        // boundary preventing that internal overlap from degrading into an unrelated-mod conflict.
        if (candidateIds.Count == 2)
        {
            var ids = candidateIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var a = enabledMods[ids[0]];
            var b = enabledMods[ids[1]];
            var key = PairKey(a.Id,b.Id);
            var stats = pairStats.TryGetValue(key, out var knownStats)
                ? knownStats
                : new PairStats(contentStats.GetValueOrDefault(a.Id)?.TotalFiles ?? 0,
                                contentStats.GetValueOrDefault(b.Id)?.TotalFiles ?? 0,
                                1);

            if (AutoCompatibility.TryInferOverlay(a,b,stats,out _,out var child,out var reason,out var confidence) &&
                confidence == Confidence.High)
            {
                MasterDebugLog.Write("FAMILY-CONFLICT", $"COMPOSE family={familyId}; path={path}; winner={child.Id}; reason=semantic-overlay; confidence={confidence}");
                decision = new(path, ConflictKind.ModFamilyOption, false, child.Id,
                    "family-internal-overlay",
                    reason + " Because both providers are proven members of the same logical family, this overlap is composed internally rather than reported as an inter-mod conflict.",
                    Confidence.High,
                    Inferred:true,
                    ResolverScore:96,
                    Evidence:$"Shared logical family '{familyId}' plus package/provenance overlay evidence.");
                return true;
            }

            // A smaller package that is mostly contained by a larger sibling is the generic shape of
            // an optional component, even when its author uses an unfamiliar future naming scheme.
            var ac = contentStats.GetValueOrDefault(a.Id)?.TotalFiles ?? stats.LeftFiles;
            var bc = contentStats.GetValueOrDefault(b.Id)?.TotalFiles ?? stats.RightFiles;
            var smaller = ac <= bc ? a : b;
            var larger = ReferenceEquals(smaller,a) ? b : a;
            var smallerCount = Math.Min(ac,bc);
            if (smallerCount > 0 && stats.SmallerOverlapRatio >= .75 && ac != bc)
            {
                MasterDebugLog.Write("FAMILY-CONFLICT", $"COMPOSE family={familyId}; path={path}; winner={smaller.Id}; reason=subset-component; overlap={stats.SmallerOverlapRatio:F3}");
                decision = new(path, ConflictKind.ModFamilyOption, false, smaller.Id,
                    "family-subset-component",
                    $"'{smaller.DisplayName}' is a mostly-overlapping smaller member of the same logical family as '{larger.DisplayName}'. It is treated as a family component and wins only the paths it supplies.",
                    Confidence.High,
                    Inferred:true,
                    ResolverScore:94,
                    Evidence:$"Shared logical family '{familyId}'; smaller-package overlap ratio {stats.SmallerOverlapRatio:P0}.");
                return true;
            }
        }

        // Remaining same-family collisions are ambiguous sibling variants/components. They must never
        // degrade into HARD_STRUCTURAL/HARD_GAME_DATA against their own family, but we also must not
        // silently combine mutually-exclusive variants. Surface one family-internal choice instead.
        MasterDebugLog.Write("FAMILY-CONFLICT", $"CHOICE family={familyId}; path={path}; reason=ambiguous-siblings; members=[{string.Join(" | ", candidateIds.Order(StringComparer.OrdinalIgnoreCase))}]");
        decision = new(path, ConflictKind.ModFamilyOption, true, null,
            "family-internal-choice",
            $"Multiple members of logical family '{familyId}' replace the same path, but no safe base/optional direction is proven. Choose the intended family variant; this is an internal family choice, not a conflict between unrelated mods.",
            Confidence.High,
            Inferred:true,
            ResolverScore:90,
            Evidence:"All colliding providers share one persisted logical family, but their overwrite direction is ambiguous.");
        return true;
    }

    public static (string,string) PairKey(string a, string b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var left = a.ToLowerInvariant();
        var right = b.ToLowerInvariant();
        return StringComparer.Ordinal.Compare(left,right) <= 0 ? (left,right) : (right,left);
    }

}
