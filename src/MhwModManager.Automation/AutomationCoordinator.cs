using System.Diagnostics;
using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

/// <summary>Measured stage feedback; no percentage is fabricated for unknown work.</summary>
public sealed record StartupMaintenanceProgress(
    string Stage, string Description, TimeSpan Elapsed, int? Completed = null, int? Total = null);

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

    public Task<StartupMaintenanceResult> RunStartupMaintenanceAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return RunStartupMaintenanceAsync(null, ct);
    }

    public Task<StartupMaintenanceResult> RunStartupMaintenanceAsync(
        Action<StartupMaintenanceProgress>? progress, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return RunStartupMaintenanceAsync(progress, shouldSkip: null, ct);
    }

    /// <summary>
    /// The splash requests a graceful stop only between complete, durable stages.
    /// Safety recovery happens before this method; the optional maintenance
    /// queue never cancels an in-flight publication or its catalog finalization.
    /// </summary>
    public async Task<StartupMaintenanceResult> RunStartupMaintenanceAsync(
        Action<StartupMaintenanceProgress>? progress,
        Func<bool>? shouldSkip,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var elapsed = Stopwatch.StartNew();
        void Report(string stage, string description, int? completed = null, int? total = null)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            try { progress?.Invoke(new StartupMaintenanceProgress(stage, description, elapsed.Elapsed, completed, total)); }
            catch (Exception ex) { UnifiedDebugLog.Write("AUTOMATION", "Nonfatal startup progress reporter failure.", ex); }
        }

        var failures = new List<Exception>();

        bool SkipRequested()
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return shouldSkip?.Invoke() == true;
        }

        StartupMaintenanceResult Deferred(
            InboxRunResult inboxResult,
            DuplicateAnalysis duplicateResult,
            int categoriesAssigned,
            IReadOnlyList<DependencyStatus> checkedDependencies,
            string stage)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            // A manual deferral must never turn a real maintenance failure into
            // a successful startup; those failures remain visible and fail closed.
            if (failures.Count > 0)
                throw new AggregateException("Optional maintenance failed before deferral.", failures);
            var summary = $"Deferred remaining automatic maintenance after {stage}; " +
                $"Inbox: {inboxResult.Imported} imported of {inboxResult.Found} found; " +
                $"categories assigned: {categoriesAssigned}; remaining Inbox packages and unchecked dependency checks require a later maintenance run.";
            startupDiagnostics?.Info("startup.automation.deferred", summary);
            Report("Deferred", summary);
            return new(inboxResult, duplicateResult, categoriesAssigned, checkedDependencies, summary);
        }

        var inboxProgress = progress is null ? null : new ImmediateProgress<InboxRunProgress>(item =>
            Report("Inbox", item.Total > 0
                ? $"Inbox: {item.Stage} — {item.CurrentItem} ({item.Completed}/{item.Total} packages)"
                : "Inbox: " + item.Stage, item.Completed, item.Total));
        Report("Inbox", "Checking Inbox packages and importing safe downloads…");
        var imported = await AttemptAsync(
            "startup.automation.inbox",
            token => inbox.ProcessAsync(inboxProgress, refreshMetadata: false,
                shouldStopAfterCurrentItem: shouldSkip, ct: token),
            new InboxRunResult(0, 0, 0, []),
            failures,
            ct);
        if (SkipRequested())
            return Deferred(imported, new DuplicateAnalysis([], [], 0), 0, [], "Inbox");

        Report("Categories", "Classifying uncategorized mods from the indexed library…");
        var assigned = await AttemptAsync(
            "startup.automation.categories",
            token => categories.AssignMissingAsync(token),
            0,
            failures,
            ct);
        if (SkipRequested())
            return Deferred(imported, new DuplicateAnalysis([], [], 0), assigned, [], "categories");

        Report("Duplicates", "Checking the visible package library…");
        var duplicateAnalysis = await AttemptAsync(
            "startup.automation.duplicate-analysis",
            token => duplicates.AnalyzeAsync(token),
            new DuplicateAnalysis([], [], 0),
            failures,
            ct);
        if (SkipRequested())
            return Deferred(imported, duplicateAnalysis, assigned, [], "duplicate analysis");

        Report("Dependencies", "Checking effective mod dependencies…");
        var deps = await AttemptAsync<IReadOnlyList<DependencyStatus>>(
            "startup.automation.dependencies",
            async token => await dependencies.ScanAsync(token),
            [],
            failures,
            ct);
        if (SkipRequested())
            return Deferred(imported, duplicateAnalysis, assigned, deps, "dependency validation");

        Report("Update diffs", "Recording changed mod relationships…");
        await AttemptAsync(
            "startup.automation.update-diffs",
            RecordUpdateDiffsAsync,
            failures,
            ct);
        if (SkipRequested())
            return Deferred(imported, duplicateAnalysis, assigned, deps, "update-diff recording");

        var dependencyIssues = deps.Count(x => !x.Ready);
        var summary = $"Inbox: {imported.Imported} imported; categories: {assigned} assigned; dependency issues: {dependencyIssues}; diagnostic failures: {failures.Count}.";
        Report("Finalizing", "Recording startup maintenance summary…");
        await AttemptAsync(
            "startup.automation.timeline",
            token => timeline.RecordAsync("startup.maintenance", failures.Count == 0 ? AutomationSeverity.Info : AutomationSeverity.Warning, summary, new { imported = imported.Imported, assigned, dependencyIssues, diagnosticFailures = failures.Count }, token),
            failures,
            ct);
        startupDiagnostics?.Info("startup.automation.maintenance-summary", summary);
        Report("Complete", summary);

        if (failures.Count > 0)
            throw new AggregateException($"Startup maintenance completed with {failures.Count} failure(s). See the startup diagnostic log for each attempted maintenance stage.", failures);

        return new(imported, duplicateAnalysis, assigned, deps, summary);
    }

    private sealed class ImmediateProgress<T>(Action<T> onProgress) : IProgress<T>
    {
        public void Report(T value)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            onProgress(value);
        }
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
        var prepared = await PrepareForPlayAsync(requireApplied: false, ct);
        return prepared.Report;
    }

    private async Task<(LaunchHealthReport Report, SaveSnapshotResult Snapshot)> PrepareForPlayAsync(bool requireApplied, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UnifiedDebugLog.Write("LAUNCH", "PrepareForPlay BEGIN");
        var unmanaged = game is null || game.IsMonsterHunterWorld || !string.IsNullOrWhiteSpace(game.ModRootRelativePath) ? await adoption.CountAsync(ct) : 0;
        if(unmanaged>0){var adopted=await adoption.AdoptAsync(ct);await timeline.RecordAsync("launch.auto-adopt",AutomationSeverity.Info,adopted.Message,null,ct);}
        var snapshot=await backups.CreateAsync("pre-launch",ct);
        await timeline.RecordAsync("launch.snapshot",snapshot.Success?AutomationSeverity.Info:AutomationSeverity.Warning,snapshot.Message,new{snapshot.SnapshotRoot,snapshot.SaveSource},ct);
        var report = await gate.EvaluateAsync(requireApplied, ct);
        UnifiedDebugLog.Write("LAUNCH", $"PrepareForPlay END Ready={report.Ready}; Findings={report.Findings.Count}");
        return (report, snapshot);
    }

    public async Task<LaunchObservation> LaunchAndObserveAsync(LaunchMode mode, TimeSpan startupWindow, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UnifiedDebugLog.Write("LAUNCH", $"LaunchAndObserve BEGIN mode={mode}; startupWindow={startupWindow}");
        ct.ThrowIfCancellationRequested();
        var exe=game?.ExecutablePath??Path.Combine(gameRoot,"MonsterHunterWorld.exe");
        if (!File.Exists(exe))
        {
            UnifiedDebugLog.Write("LAUNCH", "Launch blocked: registered executable unavailable.");
            return new(false,false,null,TimeSpan.Zero,"Launch blocked: registered game executable is unavailable. Review the game installation path.");
        }
        var (health, snapshot) = await PrepareForPlayAsync(requireApplied: mode == LaunchMode.Modded, ct);
        if(!health.Ready)
        {
            UnifiedDebugLog.Write("LAUNCH", "LaunchAndObserve BLOCKED by launch health gate.");
            var blockers = health.Findings.Where(x=>x.Severity==AutomationSeverity.Blocker).ToArray();
            var details = string.Join(" | ", blockers.Take(5).Select(x=>$"{x.Summary} — {x.Detail}"));
            if (blockers.Length > 5) details += $" | {blockers.Length - 5} additional blocker(s). Review diagnostics.";
            return new(false,false,null,TimeSpan.Zero,"Launch blocked: "+details);
        }
        var mods=await db.GetModsAsync(ct);
        var state=mods.ToDictionary(x=>x.Id,x=>new ModState(x.Enabled,x.Priority),StringComparer.OrdinalIgnoreCase);
        var enabled=state.Where(x=>x.Value.Enabled).Select(x=>x.Key).ToArray();
        var build=await db.GetGameBuildFingerprintAsync(ct);
        var launchId=Guid.NewGuid().ToString("N"); var startedAt=DateTimeOffset.UtcNow; var sw=Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();
        Process? process;
        try
        {
            process = ProcessDebug.Start(new ProcessStartInfo(exe){WorkingDirectory=gameRoot,UseShellExecute=true}, $"mhw-launch-{mode}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            UnifiedDebugLog.Write("LAUNCH", $"Process start failed: {ex.Message}");
            return new(false,false,null,TimeSpan.Zero,$"Windows could not start {game?.DisplayName??"Monster Hunter: World"}: {ex.Message}");
        }
        using var processHandle = process;
        if(process is null)
        {
            UnifiedDebugLog.Write("LAUNCH", "LaunchAndObserve FAILED: Windows returned no process.");
            return new(false,false,null,TimeSpan.Zero,$"Windows did not start {game?.DisplayName??"Monster Hunter: World"}.");
        }
        var delay = Task.Delay(startupWindow, ct);
        var exit = process.WaitForExitAsync(ct);
        var finished = await Task.WhenAny(delay, exit);
        // A canceled/faulted wait is not a completed startup observation.
        // Do not manufacture success (or a crash) from an interrupted wait.
        await finished;
        var exited = process.HasExited;
        var survived = finished == delay && !exited;
        int? exitCode = exited ? process.ExitCode : null;
        sw.Stop();
        var observation = new LaunchObservationRecord(
            launchId,
            startedAt,
            DateTimeOffset.UtcNow,
            mode,
            build?.Sha256,
            survived,
            exitCode,
            survived,
            state,
            $"Observed for {sw.Elapsed.TotalSeconds:F1}s");
        var gameName = game?.DisplayName ?? "Monster Hunter: World";
        var message = survived
            ? $"{gameName} launched and survived the startup observation window."
            : $"{gameName} exited during startup.";

        // The process outcome is already known. Persistence problems are
        // separate failures, not evidence that Windows did not launch the game.
        try
        {
            await launchObservations.PersistAsync(observation);
        }
        catch (Exception ex)
        {
            UnifiedDebugLog.Write("LAUNCH", "Observed process outcome, but launch history/trust could not be saved.", ex);
            return new(true, survived, exitCode, sw.Elapsed,
                message + " Launch history could not be saved; automatic recovery evidence was not updated.");
        }

        try
        {
            // Authoritative post-observation bookkeeping must not use a
            // canceled UI wait or a newly read catalog as its evidence.
            if (survived)
            {
                if (mode == LaunchMode.Modded)
                {
                    await lastGood.RecordObservedLaunchAsync(observation, snapshot.Success ? snapshot.SnapshotRoot : null);
                    await issues.RecordSuccessfulLaunchAsync(enabled, CancellationToken.None);
                }
                await timeline.RecordAsync("launch.success", AutomationSeverity.Info,
                    mode == LaunchMode.Modded
                        ? $"{gameName} survived the startup health window; recorded as last known good."
                        : $"{gameName} survived the startup health window.",
                    new { mode, startupSeconds = startupWindow.TotalSeconds }, CancellationToken.None);
            }
            else
            {
                await timeline.RecordAsync("launch.failure", AutomationSeverity.Warning,
                    $"{gameName} exited during the startup observation window.", new { mode, exitCode }, CancellationToken.None);
                if (mode == LaunchMode.Modded)
                {
                    await issues.RecordLaunchFailureAsync(launchId, ModIssueKind.StartupCrash, CancellationToken.None);
                    message += " Automatic crash diagnosis can use mods changed since the last known good launch.";
                }
            }
        }
        catch (Exception ex)
        {
            UnifiedDebugLog.Write("LAUNCH", "Launch observation saved, but follow-up recovery or diagnostics failed.", ex);
            message += " Launch history was saved, but recovery or diagnostic recording did not complete.";
        }

        UnifiedDebugLog.Write("LAUNCH", $"LaunchAndObserve END survived={survived}; exitCode={exitCode}; elapsed={sw.Elapsed}");
        return new(true, survived, exitCode, sw.Elapsed, message);
    }

}
