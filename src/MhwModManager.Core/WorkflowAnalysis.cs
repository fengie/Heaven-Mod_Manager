namespace MhwModManager.Core;

public sealed record DecisionEvidence(string Source, string Description, bool Decisive = false);
public sealed record EffectiveAsset(string Path, string? WinnerId, string? WinnerName, string? Sha256,
    bool Blocking, IReadOnlyList<ModDescriptor> Providers, ConflictDecision? Decision, IReadOnlyList<DecisionEvidence> Evidence)
{
    public string? AppliedProviderName { get; init; }
    public string? AppliedDigest { get; init; }
}
public sealed record ProfileStateChange(string ModId, string Name, bool BeforeEnabled, bool AfterEnabled, int BeforePriority, int AfterPriority);
public sealed record ProfileComparison(IReadOnlyList<ProfileStateChange> Mods, IReadOnlyList<string> ProviderChanges,
    IReadOnlyList<string> IntroducedConflicts, IReadOnlyList<string> ResolvedConflicts, IReadOnlyList<string> ChangedArmorComponents)
{
    public IReadOnlyList<UnavailableProfileMod> UnavailableMods { get; init; } = [];
    public IReadOnlyList<ProfileProviderTransition> ProviderTransitions { get; init; } = [];
    public bool BeforeBlocked { get; init; }
    public bool AfterBlocked { get; init; }
}
public sealed record UnavailableProfileMod(string ModId, string Name, bool BeforeEnabled, bool AfterEnabled, string Reason);
public sealed record ProfileProviderTransition(string Path, string? BeforeId, string? AfterId, string BeforeName, string AfterName, string Reason);

/// <summary>Read-only projections of the same planner used by deployment. No independent winner algorithm.</summary>
public static class WorkflowAnalysis
{
    public static IReadOnlyList<EffectiveAsset> Explore(PlannerSnapshot snapshot, DeploymentPlanner planner)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var plan = planner.Build(snapshot);
        var decisions = plan.Conflicts.ToLookup(d => d.Path, PathRules.Comparer);
        var mods = snapshot.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var rulesById = snapshot.Rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var exactRules = snapshot.Rules.Where(r => r.Kind == RuleKind.ExactWinner && r.PathPattern is not null).ToLookup(r => r.PathPattern!, PathRules.Comparer);
        var globalBlocks = plan.Conflicts.Where(d => d.Blocking && d.Path.StartsWith('<')).ToArray();
        var rows = new List<EffectiveAsset>();
        foreach (var group in snapshot.Files.GroupBy(f => f.Path, PathRules.Comparer).OrderBy(g => g.Key, PathRules.Comparer))
        {
            var providers = group.Where(f => mods.ContainsKey(f.ModId)).Select(f => mods[f.ModId]).OrderBy(m => m.Priority).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!providers.Any(m => m.Enabled && !m.IsSuperseded)) continue;
            var decision = decisions[group.Key].FirstOrDefault() ?? globalBlocks.FirstOrDefault();
            var winner = decision?.Blocking == false ? decision.WinnerModId : null;
            var evidence = new List<DecisionEvidence>();
            foreach (var block in globalBlocks) evidence.Add(new("Global deployment blocker", block.Explanation, true));
            foreach (var mod in providers)
            {
                evidence.Add(new("Captured file overlap", $"{mod.DisplayName}: priority {mod.Priority}; {(mod.IsSuperseded ? "superseded" : mod.Enabled ? "enabled" : "disabled")}; family {mod.FamilyId ?? "none"}; role {mod.FamilyRole ?? "unclassified"}; choice group {mod.ChoiceGroup ?? "none"}."));
                if (mod.NexusModId is not null) evidence.Add(new("Nexus lineage", $"{mod.DisplayName}: mod {mod.NexusModId}, file {mod.NexusFileId ?? "unknown"}, version {mod.NexusVersion ?? "unknown"}; source {mod.ProvenanceSource}."));
            }
            if (decision is not null)
            {
                var storedRule = decision.RuleId is not null ? rulesById.GetValueOrDefault(decision.RuleId) : null;
                if (decision.ReasonCode == "exact-file-winner") storedRule ??= exactRules[group.Key].LastOrDefault(r => StringComparer.OrdinalIgnoreCase.Equals(r.WinnerModId, winner));
                var source = decision.ReasonCode == "resource-provider" ? "Manual shared-provider pin" : storedRule?.Explicit == true ? "Manual rule" : decision.Inferred ? "Inferred evidence" : decision.RuleId is not null ? "Resolver rule" : "Planner decision";
                evidence.Add(new(source, decision.Explanation, true));
                if (!string.IsNullOrWhiteSpace(decision.Evidence)) evidence.Add(new("Decision evidence", decision.Evidence, true));
                evidence.Add(new("Decision code", $"{decision.ReasonCode}; confidence {decision.Confidence}; rule {decision.RuleId ?? "none"}.", true));
            }
            var file = group.FirstOrDefault(f => StringComparer.OrdinalIgnoreCase.Equals(f.ModId, winner));
            snapshot.CurrentManifest.TryGetValue(group.Key, out var applied);
            rows.Add(new(group.Key, winner, winner is not null && mods.TryGetValue(winner, out var winningMod) ? winningMod.DisplayName : null,
                file?.BlobSha256, decision?.Blocking != false || globalBlocks.Length > 0, providers, decision, evidence)
            {
                AppliedProviderName = applied?.ProviderModId is { } id ? mods.GetValueOrDefault(id)?.DisplayName ?? id : null,
                AppliedDigest = applied?.BlobSha256
            });
        }
        return rows;
    }

    public static PlannerSnapshot WithState(PlannerSnapshot snapshot, IReadOnlyDictionary<string, (bool enabled, int priority)> state)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return snapshot with { Mods = snapshot.Mods.Select(m => state.TryGetValue(m.Id, out var value)
            ? m with { Enabled = value.enabled, Priority = value.priority } : m with { Enabled = false }).ToArray() };
    }

    public static ProfileComparison Compare(PlannerSnapshot snapshot, DeploymentPlanner planner,
        IReadOnlyDictionary<string, (bool enabled, int priority)> before, IReadOnlyDictionary<string, (bool enabled, int priority)> after)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var left = WithState(snapshot, before); var right = WithState(snapshot, after);
        var leftMods = left.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var changes = right.Mods.Where(m => leftMods[m.Id].Enabled != m.Enabled || leftMods[m.Id].Priority != m.Priority)
            .Select(m => new ProfileStateChange(m.Id, m.DisplayName, leftMods[m.Id].Enabled, m.Enabled, leftMods[m.Id].Priority, m.Priority)).ToArray();
        var leftPlan = planner.Build(left);
        var rightPlan = planner.Build(right);
        var a = leftPlan.Conflicts.ToLookup(d => d.Path, PathRules.Comparer);
        var b = rightPlan.Conflicts.ToLookup(d => d.Path, PathRules.Comparer);
        var paths = a.Select(group => group.Key).Union(b.Select(group => group.Key), PathRules.Comparer)
            .Where(path => !path.StartsWith('<')).Order(PathRules.Comparer).ToArray();
        var providers = paths.Where(p => !PathRules.Comparer.Equals(a[p].FirstOrDefault()?.WinnerModId,
            b[p].FirstOrDefault()?.WinnerModId)).ToArray();
        var components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in providers)
            if (PathRules.TryGetArmorComponent(path, out var model, out var component)) components.Add(model + ":" + component);
        var installed = snapshot.Mods.ToDictionary(mod => mod.Id, PathRules.Comparer);
        var unavailable = before.Keys.Union(after.Keys, PathRules.Comparer)
            .Where(id => (before.GetValueOrDefault(id).enabled || after.GetValueOrDefault(id).enabled) &&
                (!installed.TryGetValue(id, out var mod) || mod.IsSuperseded || mod.RequiresLayoutReview))
            .Order(PathRules.Comparer).Select(id => new UnavailableProfileMod(id, installed.GetValueOrDefault(id)?.DisplayName ?? id,
                before.GetValueOrDefault(id).enabled, after.GetValueOrDefault(id).enabled,
                !installed.TryGetValue(id, out var mod) ? "Missing local package" : mod.IsSuperseded ? "Superseded package" : "Needs layout review")).ToArray();
        var transitions = providers.Select(path =>
        {
            var old = a[path].FirstOrDefault();
            var next = b[path].FirstOrDefault();
            return new ProfileProviderTransition(path, old?.WinnerModId, next?.WinnerModId,
                old?.WinnerModId is { } oldId ? installed.GetValueOrDefault(oldId)?.DisplayName ?? oldId : "No proven winner",
                next?.WinnerModId is { } newId ? installed.GetValueOrDefault(newId)?.DisplayName ?? newId : "No proven winner",
                next?.Explanation ?? "No provider in the right profile.");
        }).ToArray();
        var oldBlockers = BlockerKeys(leftPlan);
        var newBlockers = BlockerKeys(rightPlan);
        return new(changes, providers,
            newBlockers.Except(oldBlockers, PathRules.Comparer).Order(PathRules.Comparer).ToArray(),
            oldBlockers.Except(newBlockers, PathRules.Comparer).Order(PathRules.Comparer).ToArray(),
            components.Order(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            UnavailableMods = unavailable, ProviderTransitions = transitions,
            BeforeBlocked = leftPlan.IsBlocked, AfterBlocked = rightPlan.IsBlocked
        };
    }

    private static HashSet<string> BlockerKeys(DeploymentPlan plan)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return plan.Conflicts.Where(d => d.Blocking).Select(d =>
            d.Path.StartsWith('<') ? $"{d.Path} {d.ReasonCode}: {d.Explanation}" : d.Path).ToHashSet(PathRules.Comparer);
    }
}
