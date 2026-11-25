using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class LaunchHealthGateService(PlannerSnapshotRepository plannerSnapshots, HealthService health, UnmanagedAdoptionService adoption, DependencyDoctorService dependencies, DeploymentPlanner planner, GameProfile? game = null)
{
    public Task<LaunchHealthReport> EvaluateAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return EvaluateAsync(requireApplied: false, ct);
    }

    public async Task<LaunchHealthReport> EvaluateAsync(bool requireApplied, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var findings = new List<AutomationFinding>();
        var unmanaged = game is null || game.IsMonsterHunterWorld || !string.IsNullOrWhiteSpace(game.ModRootRelativePath) ? await adoption.CountAsync(ct) : 0;
        if (unmanaged > 0) findings.Add(new("unmanaged", AutomationSeverity.Warning, $"{unmanaged} unmanaged live file(s)", "These can be adopted automatically before launch."));
        var snap = await plannerSnapshots.LoadAsync(ct);
        var plan = planner.Build(snap);
        var appliedTextures = requireApplied && !plan.IsBlocked && plan.Changes.Count == 0 &&
            (game is null || game.IsMonsterHunterWorld)
            ? snap.CurrentManifest.Values.Where(entry => entry.Path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.Path, PathRules.Comparer)
            : null;
        var healthIssues = await health.ScanAsync(appliedTextures, ct);
        foreach (var issue in healthIssues) findings.Add(new("health", issue.Severity.Equals("Error",StringComparison.OrdinalIgnoreCase)?AutomationSeverity.Blocker:AutomationSeverity.Warning, issue.Summary, issue.Detail ?? issue.Path ?? issue.Code, issue.ModId));
        if (requireApplied && !plan.IsBlocked && plan.Changes.Count > 0)
            findings.Add(new("pending-deployment", AutomationSeverity.Blocker, "Saved mod choices are not applied. Apply mod changes before launching.", $"{plan.Changes.Count} pending file change(s)."));
        var enabled = snap.Mods.Where(x => x.Enabled && !x.IsSuperseded).Select(x => x.Id).ToHashSet(PathRules.Comparer);
        var deps = await dependencies.ScanStageAsync(enabled, plan, ct);
        foreach (var d in deps.Where(x => !x.Ready)) findings.Add(new("dependency", AutomationSeverity.Blocker, $"Missing requirement for {d.ModName}", string.Join("; ", d.Missing), d.ModId));
        foreach (var conflict in plan.Conflicts.Where(x => x.Blocking)) findings.Add(new("conflict", AutomationSeverity.Blocker, $"Unresolved direct replacement: {conflict.Path}", conflict.Explanation));
        foreach (var mod in snap.Mods.Where(x => x.Enabled && x.NeedsRevalidation)) findings.Add(new("revalidate", AutomationSeverity.Warning, $"{mod.DisplayName} needs revalidation", "The game executable changed since this mod was last validated.", mod.Id));
        return new(findings.All(x => x.Severity != AutomationSeverity.Blocker), findings);
    }
}
