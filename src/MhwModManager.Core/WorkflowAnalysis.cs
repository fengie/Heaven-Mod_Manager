namespace MhwModManager.Core;

public sealed record DecisionEvidence(string Source, string Description, bool Decisive = false);
public sealed record EffectiveAsset(string Path, string? WinnerId, string? WinnerName, string? Sha256,
    bool Blocking, IReadOnlyList<ModDescriptor> Providers, ConflictDecision? Decision, IReadOnlyList<DecisionEvidence> Evidence);
public sealed record ProfileStateChange(string ModId, string Name, bool BeforeEnabled, bool AfterEnabled, int BeforePriority, int AfterPriority);
public sealed record ProfileComparison(IReadOnlyList<ProfileStateChange> Mods, IReadOnlyList<string> ProviderChanges,
    IReadOnlyList<string> IntroducedConflicts, IReadOnlyList<string> ResolvedConflicts, IReadOnlyList<string> ChangedArmorComponents);

/// <summary>Read-only projections of the same planner used by deployment. No independent winner algorithm.</summary>
public static class WorkflowAnalysis
{
    public static IReadOnlyList<EffectiveAsset> Explore(PlannerSnapshot snapshot, DeploymentPlanner planner)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var plan = planner.Build(snapshot);
        var decisions = plan.Conflicts.ToDictionary(d => d.Path, PathRules.Comparer);
        var mods = snapshot.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var rulesById = snapshot.Rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var exactRules = snapshot.Rules.Where(r => r.Kind == RuleKind.ExactWinner && r.PathPattern is not null).ToLookup(r => r.PathPattern!, PathRules.Comparer);
        var cycle = plan.Conflicts.FirstOrDefault(d => d.Path == "<rules>");
        var globalBlocks = plan.Conflicts.Where(d => d.Blocking && d.Path.StartsWith("<mods:", StringComparison.Ordinal)).ToArray();
        var rows = new List<EffectiveAsset>();
        foreach (var group in snapshot.Files.GroupBy(f => f.Path, PathRules.Comparer).OrderBy(g => g.Key, PathRules.Comparer))
        {
            var providers = group.Where(f => mods.ContainsKey(f.ModId)).Select(f => mods[f.ModId]).OrderBy(m => m.Priority).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!providers.Any(m => m.Enabled && !m.IsSuperseded)) continue;
            decisions.TryGetValue(group.Key, out var decision);
            decision ??= cycle;
            var winner = decision?.Blocking == false ? decision.WinnerModId : null;
            var evidence = new List<DecisionEvidence>();
            foreach (var block in globalBlocks) evidence.Add(new("Global incompatibility", block.Explanation, true));
            foreach (var mod in providers)
            {
                evidence.Add(new("Captured file overlap", $"{mod.DisplayName}: priority {mod.Priority}; {(mod.IsSuperseded ? "superseded" : mod.Enabled ? "enabled" : "disabled")}; family {mod.FamilyId ?? "none"}."));
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
            rows.Add(new(group.Key, winner, winner is not null && mods.TryGetValue(winner, out var winningMod) ? winningMod.DisplayName : null,
                file?.BlobSha256, decision?.Blocking != false || globalBlocks.Length > 0, providers, decision, evidence));
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
        var a = planner.Build(left).Conflicts.ToDictionary(d => d.Path, PathRules.Comparer);
        var b = planner.Build(right).Conflicts.ToDictionary(d => d.Path, PathRules.Comparer);
        var paths = a.Keys.Union(b.Keys, PathRules.Comparer).Order(PathRules.Comparer).ToArray();
        var providers = paths.Where(p => !StringComparer.OrdinalIgnoreCase.Equals(a.GetValueOrDefault(p)?.WinnerModId, b.GetValueOrDefault(p)?.WinnerModId)).ToArray();
        var components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in providers.Where(p => p != "<rules>"))
            if (PathRules.TryGetArmorComponent(path, out var model, out var component)) components.Add(model + ":" + component);
        return new(changes, providers,
            paths.Where(p => a.GetValueOrDefault(p)?.Blocking != true && b.GetValueOrDefault(p)?.Blocking == true).ToArray(),
            paths.Where(p => a.GetValueOrDefault(p)?.Blocking == true && b.GetValueOrDefault(p)?.Blocking != true).ToArray(), components.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
