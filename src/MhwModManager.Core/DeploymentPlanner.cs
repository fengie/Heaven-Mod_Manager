namespace MhwModManager.Core;

public sealed class DeploymentPlanner(ConflictEngine conflictEngine, GameProfile? game = null)
{
    public DeploymentPlan Build(PlannerSnapshot snapshot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var enabled = snapshot.Mods.Where(m => m.Enabled && !m.IsSuperseded).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        // PairKey lowercases both IDs on every call. Dense shared namespaces can produce
        // millions of pair observations, so intern the canonical spelling once per mod.
        var pairIds = enabled.Keys.ToDictionary(id => id, id => id.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
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

        // A file cannot safely coexist with a directory at the same virtual path. Priority cannot
        // make this deterministic because different consumers can traverse the tree differently.
        var topologyConflicts = FindFileDirectoryCollisions(providers);
        if (topologyConflicts.Length > 0)
            return new(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                [],
                topologyConflicts,
                ["Resolve file/directory path collisions before deployment; priority is intentionally ignored for this unsafe topology."]);

        // Count every pair sharing a path, not only paths with exactly two providers. Shared skin
        // namespaces can have many providers; ignoring those pairs made family inference disappear as
        // soon as a third related mod was enabled.
        foreach (var list in providers.Values)
        {
            if (list.Count < 2) continue;
            // Already canonical lowercase and ordered: never allocate two lowercased
            // strings for each pair of providers on each shared path.
            var ids = list.Select(x => pairIds[x.ModId]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            for (var i = 0; i < ids.Length - 1; i++)
            for (var j = i + 1; j < ids.Length; j++)
            {
                var key = (ids[i], ids[j]);
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
        var atomicBundleBlockers = new Dictionary<string,ConflictDecision>(PathRules.Comparer);

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
                // Two unrelated archives can carry exactly the same complete model/
                // material/physics payload. With identical target paths and verified
                // content, choosing either physical provider cannot mix generations.
                // Group identity and overwrite precedence are *not* inferred here.
                if (IsSafeAtomicBundleComposition(memberSet, ruleIndex, enabled) ||
                    HasEquivalentAtomicBundlePayloads(bundle, memberSet) ||
                    HasIndependentNamespacedAtomicPayloads(bundle, memberSet))
                    continue;

                // Sharing an armor model/part is NOT the same as overwriting an exact
                // game file. Both situations need review when structural dependencies
                // are unproven, but the evidence must say which one actually occurred.
                var byPath = bundle.GroupBy(file => file.Path, PathRules.Comparer)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
                var exactOverlaps = byPath.Where(group =>
                    group.Select(file => file.ModId).Distinct(PathRules.Comparer).Skip(1).Any()).ToArray();
                var differingOverlaps = exactOverlaps.Count(group =>
                    group.Any(file => file.BlobSha256.Length != 64 || !file.BlobSha256.All(Uri.IsHexDigit)) ||
                    group.Select(file => file.BlobSha256).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any());
                var pathDetails = byPath.Take(8).Select(group =>
                    $"{group.Key} [providers: {string.Join(", ", group.Select(file => enabled[file.ModId].DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}]");
                var pathEvidence = string.Join("; ", pathDetails) +
                    (byPath.Length > 8 ? $"; and {byPath.Length - 8} more path(s)" : string.Empty);

                var representative = paths[0];
                var names = memberIds.Select(id => enabled[id].DisplayName).ToArray();
                var overlapDescription = exactOverlaps.Length == 0
                    ? "No exact file paths overlap. However, their different structural files target the same MHW armor component, and model/material/physics references may cross filenames."
                    : $"{exactOverlaps.Length} exact file path(s) have multiple providers ({differingOverlaps} with different or unverified content). Other structural files in this armor component may also depend on them.";
                atomicBundleBlockers[representative] = new(
                    representative,
                    ConflictKind.HardStructural,
                    true,
                    null,
                    "bundle-mixed-providers",
                    $"MHW armor component '{AssetBundles.DisplayNameForPath(representative)}' is targeted by {memberIds.Length} enabled packages ({string.Join(", ", names)}). {overlapDescription} Without proven compatibility, the manager will not combine them automatically. Shared armor placement does not establish a mod-family relationship.",
                    Confidence.High,
                    Inferred:false,
                    ResolverScore:100,
                    Evidence:$"Atomic bundle {bundle.Key}: {paths.Length} structural paths, {memberIds.Length} providers, {exactOverlaps.Length} exact-path overlaps, {differingOverlaps} different-byte exact-path overlap{(differingOverlaps == 1 ? "" : "s")} (including unknown digests). Paths: {pathEvidence}. No verified complete overlay chain or one-main/dependent-family proof.");
            }
        }

        foreach (var (path,list) in providers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (atomicBundleBlockers.TryGetValue(path, out var bundleBlocker))
            {
                decisions.Add(bundleBlocker);
                decisionByPath[path] = bundleBlocker;
                continue;
            }

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

    private static ConflictDecision[] FindFileDirectoryCollisions(
        Dictionary<string,List<ProviderCandidate>> providers)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (providers.Count < 2) return [];

        var paths = providers.Keys.ToHashSet(PathRules.Comparer);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decisions = new List<ConflictDecision>();

        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 2; i < segments.Length; i++)
            {
                var ancestor = string.Join('\\', segments.Take(i));
                if (!paths.Contains(ancestor))
                    continue;

                var key = ancestor + "\n" + path;
                if (!seen.Add(key))
                    continue;

                var fileProviders = string.Join(", ", providers[ancestor]
                    .Select(x => x.ModName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase));
                var childProviders = string.Join(", ", providers[path]
                    .Select(x => x.ModName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase));

                decisions.Add(new(
                    path,
                    ConflictKind.HardUnknown,
                    true,
                    null,
                    "file-directory-collision",
                    $"Unsafe deployment topology: '{ancestor}' is a file from [{fileProviders}] but must also be a directory containing '{path}' from [{childProviders}]. No override order can make both meanings reliable.",
                    Confidence.High,
                    Inferred:false,
                    ResolverScore:100,
                    Evidence:"A destination path is simultaneously required to be a file and an ancestor directory."));
            }
        }

        return decisions.ToArray();
    }

    /// <summary>
    /// Relationship-free exception to MHW atomic structural protection: one
    /// provider contains the entire bundle, and any other provider contributes
    /// only byte-identical subsets of that bundle's paths. This guarantees the
    /// resulting physical output is already a complete single-source bundle.
    /// Complementary partial packages, changed bytes, unknown hashes and
    /// duplicate path rows remain blocked. It does not infer family or order.
    /// </summary>
    private static bool HasEquivalentAtomicBundlePayloads(
        IEnumerable<ModFileDescriptor> files, HashSet<string> memberIds)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (memberIds.Count < 2) return false;

        var observedPaths = 0;
        var coverageByProvider = memberIds.ToDictionary(id => id, _ => 0, PathRules.Comparer);
        foreach (var group in files.GroupBy(file => file.Path, PathRules.Comparer))
        {
            observedPaths++;
            var digest = group.First().BlobSha256;
            if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
                return false;
            var present = new HashSet<string>(PathRules.Comparer);
            foreach (var file in group)
            {
                if (!memberIds.Contains(file.ModId) ||
                    !present.Add(file.ModId) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(digest, file.BlobSha256))
                    return false;
                coverageByProvider[file.ModId]++;
            }
        }
        // A complete provider must cover *every* structural destination path.
        // Otherwise two individually partial packages could manufacture one
        // mixed bundle despite having no same-path byte conflicts.
        return observedPaths > 0 && coverageByProvider.Values.Any(count => count == observedPaths);
    }

    /// <summary>
    /// Separate mod_* resource namespaces are physically isolated within an MHW
    /// armor component. They may coexist without any file or namespace overwrite.
    /// This is NOT evidence of a family, a model/material merge, or both packages
    /// rendering in game. Standard armor files, shared namespaces, partial
    /// namespace assignment and exact-path collisions remain fail-closed.
    /// </summary>
    private static bool HasIndependentNamespacedAtomicPayloads(
        IEnumerable<ModFileDescriptor> files, IReadOnlySet<string> memberIds)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (memberIds.Count < 2) return false;

        var ownerByNamespace = new Dictionary<string, string>(PathRules.Comparer);
        var namespaceByOwner = new Dictionary<string, string>(PathRules.Comparer);
        var targetPaths = new HashSet<string>(PathRules.Comparer);
        foreach (var file in files)
        {
            if (!memberIds.Contains(file.ModId) || !targetPaths.Add(file.Path))
                return false; // Even case-only collisions cannot silently pass.

            var resourceNamespace = PathRules.ResourceNamespace(file.Path);
            var normalizedPath = PathRules.Normalize(file.Path);
            if (resourceNamespace is null ||
                !normalizedPath.StartsWith(resourceNamespace + "\\", StringComparison.OrdinalIgnoreCase))
                return false; // Require a real mod_* directory, not just a mod_*.mod3 filename.

            if (namespaceByOwner.TryGetValue(file.ModId, out var existingNamespace) &&
                !PathRules.Comparer.Equals(existingNamespace, resourceNamespace))
                return false; // One package crossing namespaces needs full bundle review.

            if (ownerByNamespace.TryGetValue(resourceNamespace, out var existingOwner) &&
                !PathRules.Comparer.Equals(existingOwner, file.ModId))
                return false; // Two packages cannot independently own one namespace.

            namespaceByOwner[file.ModId] = resourceNamespace;
            ownerByNamespace[resourceNamespace] = file.ModId;
        }

        return namespaceByOwner.Count == memberIds.Count;
    }

    private static bool IsSafeAtomicBundleComposition(
        IReadOnlySet<string> memberIds,
        ConflictRuleIndex ruleIndex,
        Dictionary<string,ModDescriptor> enabled)
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

    private static bool IsMainRole(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return AutoCompatibility.IsMainPackage(mod) ||
               ContainsFamilyRole(mod.FamilyRole, "main", "base", "core", "root");
    }

    private static bool IsDependentRole(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return AutoCompatibility.IsOptionalPackage(mod) ||
               AutoCompatibility.TryGetPackageVersion(mod, out _) ||
               ContainsFamilyRole(mod.FamilyRole, "optional", "update", "patch", "addon", "add-on", "child");
    }

    private static bool ContainsFamilyRole(string? value, params string[] roles)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return !string.IsNullOrWhiteSpace(value) &&
               roles.Any(role => value.Contains(role, StringComparison.OrdinalIgnoreCase));
    }
}
