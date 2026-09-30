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

        foreach (var (path,list) in providers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var d = conflictEngine.Decide(path,list,ruleIndex,snapshot.ExactWinners,snapshot.ResourceProviders,pairStats,enabled,contentStats);
            decisions.Add(d);
            decisionByPath[path] = d;
            if (!d.Blocking)
            {
                var chosen = list.FirstOrDefault(x => d.WinnerModId is not null && PathRules.Comparer.Equals(x.ModId,d.WinnerModId))
                             ?? list.MaxBy(x => x.Priority)!;
                desired[path] = chosen;
            }
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
        IReadOnlyDictionary<string,List<ProviderCandidate>> providers)
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
}
