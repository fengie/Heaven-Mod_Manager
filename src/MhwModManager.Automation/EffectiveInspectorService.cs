using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class EffectiveInspectorService(PlannerSnapshotRepository plannerSnapshots, DeploymentPlanner? planner = null)
{
    public async Task<EffectiveFileProvider?> ExplainAsync(string path, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = PathRules.Normalize(path);
        var snapshot = await plannerSnapshots.LoadAsync(ct);
        var mods = snapshot.Mods.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var providers = snapshot.Files
            .Where(x => PathRules.Comparer.Equals(x.Path, normalized) && mods.ContainsKey(x.ModId))
            .Select(x => (mod:mods[x.ModId], file:x))
            .OrderBy(x => x.mod.Priority)
            .ToArray();
        if (providers.Length == 0) return null;

        snapshot.CurrentManifest.TryGetValue(normalized, out var manifest);
        var winner = manifest?.ProviderModId;
        var winnerName = winner is not null && mods.TryGetValue(winner, out var winnerMod) ? winnerMod.DisplayName : null;
        return new(
            normalized,
            winner,
            winnerName,
            providers.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.mod.Id, winner)).Select(x => x.mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            manifest?.BlobSha256);
    }

    public async Task<EffectiveDecisionExplanation?> ExplainWhyAsync(string path, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (planner is null) throw new InvalidOperationException("Explain Why requires the application's configured deployment planner.");

        var normalized = PathRules.Normalize(path);
        var snapshot = await plannerSnapshots.LoadAsync(ct);
        var enabledMods = snapshot.Mods
            .Where(x => x.Enabled && !x.IsSuperseded)
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var providerFiles = snapshot.Files
            .Where(x => PathRules.Comparer.Equals(x.Path, normalized) && enabledMods.ContainsKey(x.ModId))
            .GroupBy(x => x.ModId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        if (providerFiles.Length == 0) return null;

        var plan = await Task.Run(() => planner.Build(snapshot), ct);
        var decision = plan.Conflicts.FirstOrDefault(x => PathRules.Comparer.Equals(x.Path, normalized));
        if (decision is null) return null;

        snapshot.CurrentManifest.TryGetValue(normalized, out var applied);
        var plannedName = decision.WinnerModId is not null && enabledMods.TryGetValue(decision.WinnerModId, out var plannedMod)
            ? plannedMod.DisplayName
            : null;
        var appliedName = applied?.ProviderModId is not null && snapshot.Mods.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, applied.ProviderModId)) is { } appliedMod
            ? appliedMod.DisplayName
            : null;
        var appliedMatchesPlan = !decision.Blocking
            && decision.WinnerModId is not null
            && applied?.ProviderModId is not null
            && StringComparer.OrdinalIgnoreCase.Equals(decision.WinnerModId, applied.ProviderModId);

        var persistentRule = decision.RuleId is not null
            ? snapshot.Rules.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, decision.RuleId))
            : snapshot.Rules.FirstOrDefault(x =>
                x.Kind == RuleKind.ExactWinner
                && x.Explicit
                && x.PathPattern is not null
                && PathRules.Comparer.Equals(x.PathPattern, normalized)
                && StringComparer.OrdinalIgnoreCase.Equals(x.WinnerModId, decision.WinnerModId));
        var ruleSource = persistentRule?.Explicit == true
            ? "Explicit human rule"
            : decision.Inferred
                ? "Automatic compatibility inference"
                : decision.Confidence == Confidence.Explicit
                    ? "Explicit resolver rule"
                    : "Deterministic resolver";

        var summary = decision.Blocking
            ? $"No provider is selected for '{normalized}' because the resolver stopped safely: {decision.Explanation}"
            : $"{plannedName ?? decision.WinnerModId ?? "The selected provider"} wins '{normalized}' because {decision.Explanation}";

        var details = providerFiles
            .Select(file =>
            {
                var mod = enabledMods[file.ModId];
                return new EffectiveProviderDetail(
                    mod.Id,
                    mod.DisplayName,
                    file.BlobSha256,
                    mod.Priority,
                    StringComparer.OrdinalIgnoreCase.Equals(mod.Id, decision.WinnerModId),
                    StringComparer.OrdinalIgnoreCase.Equals(mod.Id, applied?.ProviderModId),
                    mod.FamilyId,
                    mod.FamilyRole,
                    mod.NexusModId,
                    mod.NexusFileId,
                    mod.NexusCategory,
                    mod.ProvenanceSource,
                    mod.ProvenanceScore);
            })
            .OrderByDescending(x => x.PlannedWinner)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            normalized,
            summary,
            decision.Blocking,
            decision.WinnerModId,
            plannedName,
            applied?.ProviderModId,
            appliedName,
            appliedMatchesPlan,
            decision.Kind,
            decision.ReasonCode,
            decision.Explanation,
            decision.Confidence,
            decision.ResolverScore,
            decision.Evidence ?? string.Empty,
            decision.RuleId,
            ruleSource,
            decision.Inferred,
            details);
    }

    public async Task<IReadOnlyList<AssetHeatmapRow>> HeatmapAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snapshot = await plannerSnapshots.LoadAsync(ct);
        var enabled = snapshot.Mods.Where(x => x.Enabled).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var providers = new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in snapshot.Files)
        {
            if (!enabled.TryGetValue(file.ModId, out var mod)) continue;
            var key = AssetBundles.KeyForPath(file.Path);
            if (!providers.TryGetValue(key, out var providerSet)) providers[key] = providerSet = new(StringComparer.OrdinalIgnoreCase);
            if (!paths.TryGetValue(key, out var pathSet)) paths[key] = pathSet = new(StringComparer.OrdinalIgnoreCase);
            providerSet.Add(mod.DisplayName);
            pathSet.Add(file.Path);
        }

        return providers
            .Where(x => x.Value.Count > 1)
            .Select(x => new AssetHeatmapRow(
                x.Key,
                AssetBundles.DisplayNameForPath(x.Key),
                x.Value.Count,
                true,
                x.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                paths[x.Key].Order(StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(x => x.ProviderCount)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
