using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class LaunchHealthGateService(ManagerDatabase db, HealthService health, UnmanagedAdoptionService adoption, DependencyDoctorService dependencies, DeploymentPlanner planner, GameProfile? game = null)
{
    public async Task<LaunchHealthReport> EvaluateAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var findings = new List<AutomationFinding>();
        var healthIssues = await health.ScanAsync(ct);
        foreach (var issue in healthIssues) findings.Add(new("health", issue.Severity.Equals("Error",StringComparison.OrdinalIgnoreCase)?AutomationSeverity.Blocker:AutomationSeverity.Warning, issue.Summary, issue.Detail ?? issue.Path ?? issue.Code, issue.ModId));
        var unmanaged = game is null || game.IsMonsterHunterWorld || !string.IsNullOrWhiteSpace(game.ModRootRelativePath) ? await adoption.CountAsync(ct) : 0;
        if (unmanaged > 0) findings.Add(new("unmanaged", AutomationSeverity.Warning, $"{unmanaged} unmanaged live file(s)", "These can be adopted automatically before launch."));
        var deps = await dependencies.ScanAsync(ct);
        foreach (var d in deps.Where(x => !x.Ready)) findings.Add(new("dependency", AutomationSeverity.Blocker, $"Missing requirement for {d.ModName}", string.Join("; ", d.Missing), d.ModId));
        var snap = await db.LoadPlannerSnapshotAsync(ct);
        var plan = planner.Build(snap);
        foreach (var conflict in plan.Conflicts.Where(x => x.Blocking)) findings.Add(new("conflict", AutomationSeverity.Blocker, "Unresolved direct replacement", conflict.Explanation));
        foreach (var mod in snap.Mods.Where(x => x.Enabled && x.NeedsRevalidation)) findings.Add(new("revalidate", AutomationSeverity.Warning, $"{mod.DisplayName} needs revalidation", "The game executable changed since this mod was last validated.", mod.Id));
        return new(findings.All(x => x.Severity != AutomationSeverity.Blocker), findings);
    }
}
