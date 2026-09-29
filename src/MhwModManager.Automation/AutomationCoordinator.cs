using System.Diagnostics;
using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class AutomationCoordinator(
    ManagerDatabase db,
    SaveBackupService backups,
    LastKnownGoodService lastGood,
    ChangeTimelineService timeline,
    UpdateDiffService updateDiff,
    SmartInboxService inbox,
    DuplicateCleanupService duplicates,
    AutoCategoryService categories,
    DependencyDoctorService dependencies,
    LaunchHealthGateService gate,
    ModIssueFallbackService issues,
    UnmanagedAdoptionService adoption,
    string gameRoot,
    GameProfile? game = null,
    StartupDiagnosticSession? startupDiagnostics = null)
{
    private readonly LaunchObservationRepository launchObservations = new(db);

    public async Task<StartupMaintenanceResult> RunStartupMaintenanceAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var failures = new List<Exception>();
        var imported = await AttemptAsync(
            "startup.automation.inbox",
            token => inbox.ProcessAsync(token),
            new InboxRunResult(0, 0, 0, []),
            failures,
            ct);
        var assigned = await AttemptAsync(
            "startup.automation.categories",
            token => categories.AssignMissingAsync(token),
            0,
            failures,
            ct);
        var archived = await AttemptAsync(
            "startup.automation.duplicate-archive",
            token => duplicates.ArchiveSafeAsync(token),
            0,
            failures,
            ct);
        var duplicateAnalysis = await AttemptAsync(
            "startup.automation.duplicate-analysis",
            token => duplicates.AnalyzeAsync(token),
            new DuplicateAnalysis([], [], 0),
            failures,
            ct);
        var deps = await AttemptAsync<IReadOnlyList<DependencyStatus>>(
            "startup.automation.dependencies",
            async token => await dependencies.ScanAsync(token),
            [],
            failures,
            ct);
        await AttemptAsync(
            "startup.automation.update-diffs",
            RecordUpdateDiffsAsync,
            failures,
            ct);

        var dependencyIssues = deps.Count(x => !x.Ready);
        var summary=$"Inbox: {imported.Imported} imported; categories: {assigned} assigned; safe archives: {archived}; dependency issues: {dependencyIssues}; diagnostic failures: {failures.Count}.";
        await AttemptAsync(
            "startup.automation.timeline",
            token => timeline.RecordAsync("startup.maintenance", failures.Count == 0 ? AutomationSeverity.Info : AutomationSeverity.Warning, summary, new{imported=imported.Imported,assigned,archived,dependencyIssues,diagnosticFailures=failures.Count}, token),
            failures,
            ct);
        startupDiagnostics?.Info("startup.automation.maintenance-summary", summary);

        if (failures.Count > 0)
            throw new AggregateException($"Startup maintenance completed with {failures.Count} failure(s). See the startup diagnostic log for each attempted maintenance stage.", failures);

        return new(imported,duplicateAnalysis,assigned,deps,summary);
    }

    private async Task<T> AttemptAsync<T>(string stage, Func<CancellationToken, Task<T>> action, T fallback, List<Exception> failures, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            UnifiedDebugLog.Write("AUTOMATION", $"START {stage}");
            var result = startupDiagnostics is null ? await action(ct) : await startupDiagnostics.RunAsync(stage, action, ct:ct);
            UnifiedDebugLog.Write("AUTOMATION", $"PASS {stage}");
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnifiedDebugLog.Write("AUTOMATION", $"FAIL {stage}", ex);
            failures.Add(new InvalidOperationException(stage + " failed: " + ex.Message, ex));
            if (startupDiagnostics is null) throw;
            return fallback;
        }
    }

    private async Task AttemptAsync(string stage, Func<CancellationToken, Task> action, List<Exception> failures, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            UnifiedDebugLog.Write("AUTOMATION", $"START {stage}");
            if (startupDiagnostics is null) await action(ct);
            else await startupDiagnostics.RunAsync(stage, action, ct:ct);
            UnifiedDebugLog.Write("AUTOMATION", $"PASS {stage}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnifiedDebugLog.Write("AUTOMATION", $"FAIL {stage}", ex);
            failures.Add(new InvalidOperationException(stage + " failed: " + ex.Message, ex));
            if (startupDiagnostics is null) throw;
        }
    }


    private async Task RecordUpdateDiffsAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var pairs=new List<(string older,string newer)>();
        UnifiedDebugLog.Write("AUTOMATION", "Scanning mod supersession pairs for update diffs.");
        await using(var c=await db.OpenAsync(ct)){await using var cmd=c.CreateCommand();cmd.CommandText="SELECT older_mod_id,newer_mod_id FROM mod_supersession";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))pairs.Add((r.GetString(0),r.GetString(1)));}
        foreach(var pair in pairs)
        {
            ct.ThrowIfCancellationRequested();
            UnifiedDebugLog.Write("AUTOMATION", $"Update diff BEGIN older={pair.older}; newer={pair.newer}");
            var diff=await updateDiff.CompareAsync(pair.older,pair.newer,ct);
            var severity=diff.StructuralChanged>0?AutomationSeverity.Warning:AutomationSeverity.Info;
            await timeline.RecordAsync("update.diff",severity,$"Update diff {pair.older} → {pair.newer}: +{diff.Added} / -{diff.Removed} / {diff.Changed} changed ({diff.StructuralChanged} structural, {diff.TextureChanged} texture).",diff,ct);
            UnifiedDebugLog.Write("AUTOMATION", $"Update diff PASS older={pair.older}; newer={pair.newer}; added={diff.Added}; removed={diff.Removed}; changed={diff.Changed}");
        }
    }

    public async Task<LaunchHealthReport> PrepareForPlayAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UnifiedDebugLog.Write("LAUNCH", "PrepareForPlay BEGIN");
        var unmanaged = game is null || game.IsMonsterHunterWorld || !string.IsNullOrWhiteSpace(game.ModRootRelativePath) ? await adoption.CountAsync(ct) : 0;
        if(unmanaged>0){var adopted=await adoption.AdoptAsync(ct);await timeline.RecordAsync("launch.auto-adopt",AutomationSeverity.Info,adopted.Message,null,ct);}
        var snapshot=await backups.CreateAsync("pre-launch",ct);
        await timeline.RecordAsync("launch.snapshot",snapshot.Success?AutomationSeverity.Info:AutomationSeverity.Warning,snapshot.Message,new{snapshot.SnapshotRoot,snapshot.SaveSource},ct);
        var report = await gate.EvaluateAsync(ct);
        UnifiedDebugLog.Write("LAUNCH", $"PrepareForPlay END Ready={report.Ready}; Findings={report.Findings.Count}");
        return report;
    }

    public async Task<LaunchObservation> LaunchAndObserveAsync(LaunchMode mode, TimeSpan startupWindow, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UnifiedDebugLog.Write("LAUNCH", $"LaunchAndObserve BEGIN mode={mode}; startupWindow={startupWindow}");
        var health=await PrepareForPlayAsync(ct);
        if(!health.Ready)
        {
            UnifiedDebugLog.Write("LAUNCH", "LaunchAndObserve BLOCKED by launch health gate.");
            return new(false,false,null,TimeSpan.Zero,"Launch blocked: "+string.Join(" | ",health.Findings.Where(x=>x.Severity==AutomationSeverity.Blocker).Select(x=>x.Summary)));
        }
        var exe=game?.ExecutablePath??Path.Combine(gameRoot,"MonsterHunterWorld.exe");
        var mods=await db.GetModsAsync(ct);
        var state=mods.ToDictionary(x=>x.Id,x=>new ModState(x.Enabled,x.Priority),StringComparer.OrdinalIgnoreCase);
        var enabled=state.Where(x=>x.Value.Enabled).Select(x=>x.Key).ToArray();
        var build=await db.GetGameBuildFingerprintAsync(ct);
        var launchId=Guid.NewGuid().ToString("N"); var startedAt=DateTimeOffset.UtcNow; var sw=Stopwatch.StartNew();
        using var process=ProcessDebug.Start(new ProcessStartInfo(exe){WorkingDirectory=gameRoot,UseShellExecute=true}, $"mhw-launch-{mode}");
        if(process is null)
        {
            UnifiedDebugLog.Write("LAUNCH", "LaunchAndObserve FAILED: Windows returned no process.");
            return new(false,false,null,TimeSpan.Zero,$"Windows did not start {game?.DisplayName??"Monster Hunter: World"}.");
        }
        var delay=Task.Delay(startupWindow,ct); var exit=process.WaitForExitAsync(ct); var finished=await Task.WhenAny(delay,exit);
        if(finished.IsCanceled&&!process.HasExited)
            ct.ThrowIfCancellationRequested();
        var survived=finished==delay&&!process.HasExited; int? exitCode=process.HasExited?process.ExitCode:null; sw.Stop();
        await launchObservations.PersistAsync(new(
            launchId,
            startedAt,
            DateTimeOffset.UtcNow,
            mode,
            build?.Sha256,
            survived,
            exitCode,
            survived,
            state,
            $"Observed for {sw.Elapsed.TotalSeconds:F1}s"));
        if(survived)
        {
            await issues.RecordSuccessfulLaunchAsync(enabled,ct);
            var latest=Directory.Exists(backups.SnapshotRoot)?Directory.EnumerateDirectories(backups.SnapshotRoot).OrderByDescending(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault():null;
            await lastGood.RecordAsync(latest,ct);
            await timeline.RecordAsync("launch.success",AutomationSeverity.Info,$"{game?.DisplayName??"Monster Hunter: World"} survived the startup health window; recorded as last known good.",new{mode,startupSeconds=startupWindow.TotalSeconds},ct);
        }
        else
        {
            await timeline.RecordAsync("launch.failure",AutomationSeverity.Warning,$"{game?.DisplayName??"Monster Hunter: World"} exited during the startup observation window.",new{mode,exitCode},ct);
            if(mode==LaunchMode.Modded)await issues.RecordLaunchFailureAsync(launchId,ModIssueKind.StartupCrash,ct);
        }
        UnifiedDebugLog.Write("LAUNCH", $"LaunchAndObserve END survived={survived}; exitCode={exitCode}; elapsed={sw.Elapsed}");
        return new(true,survived,exitCode,sw.Elapsed,survived?$"{game?.DisplayName??"Monster Hunter: World"} launched and survived the startup observation window.":$"{game?.DisplayName??"Monster Hunter: World"} exited during startup; automatic crash diagnosis can use mods changed since the last known good launch.");
    }

}
