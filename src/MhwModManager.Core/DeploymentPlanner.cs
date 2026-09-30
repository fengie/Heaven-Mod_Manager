namespace MhwModManager.Core;

public sealed class DeploymentPlanner(ConflictEngine conflictEngine, GameProfile? game = null)
{
    public DeploymentPlan Build(PlannerSnapshot snapshot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var enabled = snapshot.Mods.Where(m => m.Enabled && !m.IsSuperseded).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var providers = new Dictionary<string,List<ProviderCandidate>>(StringComparer.OrdinalIgnoreCase);
        var fileCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var textureCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var structuralCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var gameDataCounts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var latestWrites = new Dictionary<string,DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var pairOverlap = new Dictionary<(string,string),int>();

        // One indexed pass over enabled files. Avoid GroupBy chains and repeated scans in the hot path.
        foreach (var file in snapshot.Files)
        {
            if (!enabled.TryGetValue(file.ModId, out var mod)) continue;
            fileCounts[file.ModId] = fileCounts.GetValueOrDefault(file.ModId) + 1;
            if (file.FileClass == FileClass.Texture) textureCounts[file.ModId] = textureCounts.GetValueOrDefault(file.ModId) + 1;
            if (file.FileClass == FileClass.Structural) structuralCounts[file.ModId] = structuralCounts.GetValueOrDefault(file.ModId) + 1;
            if (file.FileClass == FileClass.GameData) gameDataCounts[file.ModId] = gameDataCounts.GetValueOrDefault(file.ModId) + 1;
            if (!latestWrites.TryGetValue(file.ModId, out var latest) || file.LastWriteUtc > latest) latestWrites[file.ModId] = file.LastWriteUtc;
            if (!providers.TryGetValue(file.Path, out var list)) providers[file.Path] = list = [];
            list.Add(new(mod.Id, mod.Name, mod.Priority, file));
        }

        // Count every pair sharing a path, not only paths with exactly two providers. Shared skin
        // namespaces can have many providers; ignoring those pairs made family inference disappear as
        // soon as a third related mod was enabled.
        foreach (var list in providers.Values)
        {
            if (list.Count < 2) continue;
            var ids = list.Select(x => x.ModId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            for (var i = 0; i < ids.Length - 1; i++)
            for (var j = i + 1; j < ids.Length; j++)
            {
                var key = ConflictEngine.PairKey(ids[i], ids[j]);
                pairOverlap[key] = pairOverlap.GetValueOrDefault(key) + 1;
            }
        }

        var pairStats = pairOverlap.ToDictionary(
            kv => kv.Key,
            kv => new PairStats(fileCounts.GetValueOrDefault(kv.Key.Item1), fileCounts.GetValueOrDefault(kv.Key.Item2), kv.Value));

        var contentStats = enabled.Keys.ToDictionary(
            id => id,
            id => new ModContentStats(
                fileCounts.GetValueOrDefault(id),
                textureCounts.GetValueOrDefault(id),
                structuralCounts.GetValueOrDefault(id),
                gameDataCounts.GetValueOrDefault(id),
                latestWrites.GetValueOrDefault(id, DateTimeOffset.UnixEpoch)),
            StringComparer.OrdinalIgnoreCase);

        // High-confidence base/option/patch relationships become transient overlay rules. They are
        // recomputed from indexed source files every plan, so stale auto rules are never persisted.
        // Explicit user rules always win and suppress inference for that pair.
        var autoRules = GameAdapters.Resolve(game).SupportsMhwConflictSemantics
            ? AutoCompatibility.GenerateOverlayRules(enabled, pairStats, snapshot.Rules)
            : Array.Empty<ConflictRule>();
        var effectiveRules = snapshot.Rules.Concat(autoRules).ToArray();

        var invalidOverlay = effectiveRules.FirstOrDefault(r =>
            r.Kind == RuleKind.Overlay &&
            r.Scope == RuleScope.ModPair &&
            r.LeftModId is not null &&
            r.RightModId is not null &&
            enabled.ContainsKey(r.LeftModId) &&
            enabled.ContainsKey(r.RightModId) &&
            (PathRules.Comparer.Equals(r.LeftModId, r.RightModId) ||
             r.WinnerModId is null ||
             (!PathRules.Comparer.Equals(r.WinnerModId, r.LeftModId) &&
              !PathRules.Comparer.Equals(r.WinnerModId, r.RightModId))));
        if (invalidOverlay is not null)
        {
            var decision = new ConflictDecision(
                "<rules>",
                ConflictKind.Incompatible,
                true,
                null,
                "invalid-overlay-rule",
                $"Overlay rule '{invalidOverlay.Id}' is malformed: an enabled mod pair must name two different mods and the winner must be one endpoint.",
                invalidOverlay.Explicit ? Confidence.Explicit : Confidence.High,
                invalidOverlay.Id,
                !invalidOverlay.Explicit,
                100,
                "Fail-closed rule-schema validation prevented an invalid precedence edge from entering the resolver graph.");
            return new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, [], [decision], ["Repair or remove the malformed overlay rule before deployment."]);
        }

        var cycle = RuleGraph.FindCycle(effectiveRules);
        if (cycle is not null)
        {
            var decision = new ConflictDecision("<rules>", ConflictKind.Incompatible, true, null, "precedence-cycle", "Overlay precedence contains a cycle: " + string.Join(" -> ", cycle), Confidence.Explicit);
            return new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, [], [decision], ["Remove at least one rule in the displayed cycle."]);
        }

        var explicitIncompatibilities = effectiveRules
            .Where(r => r.Kind == RuleKind.Incompatible &&
                        r.Scope == RuleScope.ModPair &&
                        r.LeftModId is not null &&
                        r.RightModId is not null &&
                        enabled.ContainsKey(r.LeftModId) &&
                        enabled.ContainsKey(r.RightModId))
            .Select(r => new ConflictDecision(
                "<rules>",
                ConflictKind.Incompatible,
                true,
                null,
                "explicit-incompatible",
                $"Enabled mods '{r.LeftModId}' and '{r.RightModId}' are explicitly marked incompatible.",
                Confidence.Explicit,
                r.Id))
            .ToArray();
        if (explicitIncompatibilities.Length > 0)
            return new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, [], explicitIncompatibilities, ["Disable one incompatible mod or remove the explicit incompatibility rule before deployment."]);

        var ruleIndex = ConflictRuleIndex.Create(effectiveRules);
        var decisions = new List<ConflictDecision>(providers.Count);
        var decisionByPath = new Dictionary<string,ConflictDecision>(providers.Count, StringComparer.OrdinalIgnoreCase);
        var desired = new Dictionary<string,ProviderCandidate>(providers.Count, StringComparer.OrdinalIgnoreCase);

        // MHW model/material/physics siblings are one atomic asset bundle even when filenames differ.
        // Exact-path-only planning can otherwise create a Frankenstein bundle from unrelated mods
        // without ever observing a direct collision. Require a proven overlay chain or strong
        // main->optional/update family evidence before composing distinct providers in one bundle.
        if (GameAdapters.Resolve(game).SupportsMhwConflictSemantics)
        {
            var atomicBundles = snapshot.Files
                .Where(f => enabled.ContainsKey(f.ModId) && f.FileClass == FileClass.Structural)
                .GroupBy(f => AssetBundles.KeyForPath(f.Path), StringComparer.OrdinalIgnoreCase);

            foreach (var bundle in atomicBundles.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var paths = bundle.Select(f => f.Path).Distinct(PathRules.Comparer).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var memberIds = bundle.Select(f => f.ModId).Distinct(PathRules.Comparer).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (paths.Length < 2 || memberIds.Length < 2) continue;

                var memberSet = memberIds.ToHashSet(PathRules.Comparer);
                if (IsSafeAtomicBundleComposition(memberSet, ruleIndex, enabled)) continue;

                var representative = paths[0];
                var names = memberIds.Select(id => enabled[id].DisplayName).ToArray();
                decisions.Add(new(
                    representative,
                    ConflictKind.HardStructural,
                    true,
                    null,
                    "bundle-mixed-providers",
                    $"The atomic asset bundle '{AssetBundles.DisplayNameForPath(representative)}' would mix files from unrelated or ambiguously related mods ({string.Join(", ", names)}). The manager will not build a partial model/material/physics bundle by guessing.",
                    Confidence.High,
                    Inferred:false,
                    ResolverScore:100,
                    Evidence:$"Atomic bundle {bundle.Key} contains {paths.Length} path(s) from {memberIds.Length} enabled provider(s) without a complete overlay chain or one-main/dependent-family proof."));
            }
        }

        foreach (var (path,list) in providers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var d = conflictEngine.Decide(path,list,ruleIndex,snapshot.ExactWinners,snapshot.ResourceProviders,pairStats,enabled,contentStats);
            if (!d.Blocking)
            {
                var chosen = list.FirstOrDefault(x => d.WinnerModId is not null && PathRules.Comparer.Equals(x.ModId,d.WinnerModId));
                if (chosen is null)
                {
                    d = new(
                        path,
                        ConflictKind.HardUnknown,
                        true,
                        null,
                        "resolver-missing-winner",
                        "The resolver reported this collision as safe but did not identify a valid provider. Deployment is blocked instead of falling back to arbitrary mod priority.",
                        Confidence.High,
                        Inferred:false,
                        ResolverScore:100,
                        Evidence:"Fail-closed resolver invariant: every non-blocking multi-provider path must identify one candidate winner.");
                }
                else
                {
                    desired[path] = chosen;
                }
            }
            decisions.Add(d);
            decisionByPath[path] = d;
        }

        if (decisions.Any(x => x.Blocking))
            return new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, [], decisions, ["Resolve the compacted blocking choices before deployment. Auto-composed overlays and shared textures require no action."]);

        var changes = new List<DeploymentChange>(Math.Max(desired.Count, snapshot.CurrentManifest.Count));
        var seq = 0;
        var allPaths = desired.Keys.Concat(snapshot.CurrentManifest.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        foreach (var path in allPaths)
        {
            snapshot.CurrentManifest.TryGetValue(path, out var current);
            desired.TryGetValue(path, out var target);
            if (target is not null)
            {
                if (current?.BlobSha256 is not null &&
                    StringComparer.OrdinalIgnoreCase.Equals(current.BlobSha256,target.File.BlobSha256) &&
                    PathRules.Comparer.Equals(current.ProviderModId,target.ModId))
                    continue;

                var decision = decisionByPath[path];
                changes.Add(new(++seq, current is null ? ChangeKind.Add : ChangeKind.Replace, path, current?.BlobSha256, target.File.BlobSha256, current?.ProviderModId, target.ModId, current?.ExpectedLiveSha256, decision.RuleId));
            }
            else if (current is not null)
            {
                snapshot.Originals.TryGetValue(path, out var original);
                changes.Add(new(++seq, original is null ? ChangeKind.Remove : ChangeKind.RestoreOriginal, path, current.BlobSha256, original, current.ProviderModId, null, current.ExpectedLiveSha256, null));
            }
        }

        return new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, changes, decisions, ["Live managed files must still match their expected hashes at commit time."]);
    }

    private static bool IsSafeAtomicBundleComposition(
        IReadOnlySet<string> memberIds,
        ConflictRuleIndex ruleIndex,
        IReadOnlyDictionary<string,ModDescriptor> enabled)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ruleIndex.FindFullyOrderedOverlayWinner(memberIds) is not null)
            return true;

        var members = memberIds.Select(id => enabled[id]).ToArray();
        var familyKey = StrongFamilyKey(members[0]);
        if (familyKey is null || members.Any(m => !StringComparer.OrdinalIgnoreCase.Equals(familyKey, StrongFamilyKey(m))))
            return false;

        var mains = members.Where(IsMainRole).ToArray();
        if (mains.Length != 1)
            return false;

        return members.Where(m => !PathRules.Comparer.Equals(m.Id, mains[0].Id)).All(IsDependentRole);
    }

    private static string? StrongFamilyKey(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.IsNullOrWhiteSpace(mod.FamilyId)) return "family:" + mod.FamilyId.Trim();
        if (!string.IsNullOrWhiteSpace(mod.NexusModUuid)) return "nexus-uuid:" + mod.NexusModUuid.Trim();
        if (!string.IsNullOrWhiteSpace(mod.NexusModId)) return "nexus-id:" + mod.NexusModId.Trim();
        return null;
    }

    private static bool IsMainRole(ModDescriptor mod) =>
        mod.NexusCategory == NexusFileCategory.Main ||
        ContainsFamilyRole(mod.FamilyRole, "main", "base", "core", "root");

    private static bool IsDependentRole(ModDescriptor mod) =>
        mod.NexusCategory is NexusFileCategory.Optional or NexusFileCategory.Update ||
        ContainsFamilyRole(mod.FamilyRole, "optional", "update", "patch", "addon", "add-on", "child");

    private static bool ContainsFamilyRole(string? value, params string[] roles) =>
        !string.IsNullOrWhiteSpace(value) && roles.Any(role => value.Contains(role, StringComparison.OrdinalIgnoreCase));
}
