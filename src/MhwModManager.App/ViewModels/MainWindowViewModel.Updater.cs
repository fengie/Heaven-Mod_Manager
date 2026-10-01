using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;
using MhwModManager.Updater;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly SemaphoreSlim programUpdateGate = new(1, 1);
    private readonly UpdateHandoffGate programUpdateHandoffGate = new();
    private StagedUpdate? stagedProgramUpdate;
    private Task? stagedProgramHandoffTask;
    private bool programUpdaterStarted;
    private bool stagedProgramUpdateRequestedManually;

    [ObservableProperty] private string currentProgramBuildText = "Program build unavailable";
    [ObservableProperty] private string programUpdateStatus = "Program updater has not checked yet.";
    [ObservableProperty] private string latestProgramBuildText = "Latest build: not checked";
    [ObservableProperty] private bool programUpdateCheckInProgress;

    public void StartProgramUpdater()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed || programUpdaterStarted || !AutoUpdateEnabled)
        {
            if (!AutoUpdateEnabled && UpdateClientService.CanSelfUpdate(UpdateClientService.GetInstallRoot()))
                ProgramUpdateStatus = "Automatic program updates are off. Manual checks remain available.";
            return;
        }
        programUpdaterStarted = true;
        _ = ProgramUpdateLoopAsync(backgroundCts.Token);
    }

    [RelayCommand]
    private async Task CheckForProgramUpdates()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await CheckAndStageProgramUpdateAsync(manual: true, backgroundCts.Token);
    }

    private async Task ProgramUpdateLoopAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if (AutoUpdateEnabled)
                await CheckAndStageProgramUpdateAsync(manual: false, ct);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromHours(6), ct);
                if (AutoUpdateEnabled)
                    await CheckAndStageProgramUpdateAsync(manual: false, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ProgramUpdateStatus =
                $"Background program updater stopped ({ex.GetType().Name}). The current build remains active.";
            s.Log.Warning(ex,"Background program updater loop stopped non-fatally.");
        }
    }

    private async Task CheckAndStageProgramUpdateAsync(bool manual, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"manual={manual}");
        var installRoot = UpdateClientService.GetInstallRoot();
        if (!UpdateClientService.CanSelfUpdate(installRoot))
        {
            ProgramUpdateStatus =
                "Self-update is disabled for this development/unmanaged installation.";
            return;
        }

        bool entered;
        try
        {
            entered = await programUpdateGate.WaitAsync(0, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        if (!entered)
        {
            if (manual) ProgramUpdateStatus = "An update check is already running.";
            return;
        }

        ProgramUpdateCheckInProgress = true;
        try
        {
            ProgramUpdateStatus = "Checking for a verified program update…";
            var staged = await s.Updater.CheckAndStageAsync(s.BuildIdentity, ct);
            if (staged is null)
            {
                LatestProgramBuildText =
                    $"Latest build: {s.BuildIdentity.DisplayId}";
                ProgramUpdateStatus = "Program is up to date.";
                return;
            }

            stagedProgramUpdate = staged;
            stagedProgramUpdateRequestedManually = manual;
            LatestProgramBuildText =
                $"Latest build: {staged.Manifest.ProductVersion} · build {staged.Manifest.BuildNumber} · {ShortSha(staged.Manifest.SourceSha)}";
            if (AutoUpdateEnabled || stagedProgramUpdateRequestedManually)
            {
                ProgramUpdateStatus =
                    $"Verified update build {staged.Manifest.BuildNumber} is staged safely. Waiting for a safe restart point.";
                ScheduleStagedProgramHandoff();
            }
            else
            {
                ProgramUpdateStatus =
                    $"Verified update build {staged.Manifest.BuildNumber} is staged, but automatic installation is paused because automatic updates are off.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (manual) ProgramUpdateStatus = "Program update check cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            ProgramUpdateStatus =
                $"Automatic updates need one-time GitHub access in Windows Credential Manager ({UpdateProtocol.CredentialTarget}). The manager will continue normally.";
            s.Log.Warning("Program updater authentication is not configured or was rejected.");
        }
        catch (Exception ex)
        {
            ProgramUpdateStatus =
                $"Program update check failed ({ex.GetType().Name}). The current build remains active.";
            s.Log.Warning(ex,"Program updater check/stage failed non-fatally.");
        }
        finally
        {
            ProgramUpdateCheckInProgress = false;
            programUpdateGate.Release();
        }
    }

    private void ScheduleStagedProgramHandoff()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (stagedProgramUpdate is null || disposed) return;
        if (stagedProgramHandoffTask is { IsCompleted: false }) return;
        stagedProgramHandoffTask =
            ApplyStagedProgramUpdateWhenSafeAsync(backgroundCts.Token);
    }

    private async Task ApplyStagedProgramUpdateWhenSafeAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            PreparedUpdateHandoff? prepared = null;
            StagedUpdate? preparedFor = null;
            while (!ct.IsCancellationRequested && stagedProgramUpdate is not null)
            {
                var staged = stagedProgramUpdate;
                if (staged is null) return;
                if (prepared is not null && !IsPreparedHandoffCurrent(prepared, preparedFor, staged))
                {
                    prepared = null;
                    preparedFor = null;
                }

                if (!AutoUpdateEnabled && !stagedProgramUpdateRequestedManually)
                {
                    ProgramUpdateStatus =
                        $"Verified update build {staged.Manifest.BuildNumber} remains staged; automatic installation is off.";
                    return;
                }

                if (CriticalOperation || BusyVisibility == Visibility.Visible)
                {
                    ProgramUpdateStatus =
                        $"Verified update build {staged.Manifest.BuildNumber} is staged; waiting for the current operation to finish.";
                    await DelayForSafeUpdateRetryAsync(ct);
                    continue;
                }

                if (HasActiveGameProcess())
                {
                    ProgramUpdateStatus =
                        $"Verified update build {staged.Manifest.BuildNumber} is staged; it will install after the game exits.";
                    await DelayForSafeUpdateRetryAsync(ct);
                    continue;
                }

                try
                {
                    if (prepared is null)
                    {
                        prepared = await s.Updater.PrepareHandoffAsync(
                            staged,
                            UpdateClientService.GetInstallRoot(),
                            s.StartupArguments,
                            Environment.ProcessId,
                            ct,
                            managerHomeRoot:s.Paths.ToolRoot);
                        preparedFor = staged;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ProgramUpdateStatus =
                        $"Update build {staged.Manifest.BuildNumber} is staged, but restart preparation failed ({ex.GetType().Name}). It will be retried on a later launch.";
                    s.Log.Warning(ex,"Program updater handoff preparation failed.");
                    return;
                }

                if (prepared is null || !IsPreparedHandoffCurrent(prepared, preparedFor, stagedProgramUpdate))
                {
                    prepared = null;
                    preparedFor = null;
                    continue;
                }

                if (CriticalOperation
                    || BusyVisibility == Visibility.Visible
                    || HasActiveGameProcess())
                {
                    await DelayForSafeUpdateRetryAsync(ct);
                    continue;
                }

                if (!AutoUpdateEnabled && !stagedProgramUpdateRequestedManually)
                {
                    ProgramUpdateStatus =
                        $"Verified update build {staged.Manifest.BuildNumber} remains staged; automatic installation is off.";
                    return;
                }

                // Update checks replace stagedProgramUpdate under programUpdateGate.
                // Hold the same gate across the final identity/policy checks and
                // synchronous helper launch so a newer staged candidate cannot
                // cross the prepared-handoff boundary.
                await programUpdateGate.WaitAsync(ct);
                try
                {
                    if (!IsPreparedHandoffCurrent(prepared, preparedFor, stagedProgramUpdate))
                    {
                        prepared = null;
                        preparedFor = null;
                        continue;
                    }

                    if (!AutoUpdateEnabled && !stagedProgramUpdateRequestedManually)
                    {
                        ProgramUpdateStatus =
                            $"Verified update build {staged.Manifest.BuildNumber} remains staged; automatic installation is off.";
                        return;
                    }

                    if (CriticalOperation
                        || BusyVisibility == Visibility.Visible
                        || HasActiveGameProcess())
                        continue;

                    if (!programUpdateHandoffGate.TryArmHandoff())
                    {
                        ProgramUpdateStatus =
                            $"Verified update build {staged.Manifest.BuildNumber} is staged; waiting for the current operation to finish.";
                        continue;
                    }

                    var keepHandoffArmed = false;
                    try
                    {
                        // No RunBusy operation can begin after the handoff gate is armed,
                        // and no update check can replace stagedProgramUpdate while
                        // programUpdateGate is held. Do not yield before helper launch.
                        if (CriticalOperation
                            || BusyVisibility == Visibility.Visible
                            || HasActiveGameProcess()
                            || !AutoUpdateEnabled && !stagedProgramUpdateRequestedManually
                            || !IsPreparedHandoffCurrent(prepared, preparedFor, stagedProgramUpdate))
                            continue;

                        using var helper = s.Updater.LaunchHelper(prepared);
                        keepHandoffArmed = true;
                        ProgramUpdateStatus =
                            $"Installing verified update build {staged.Manifest.BuildNumber}; restarting…";
                        s.Log.Information(
                            "Program updater helper launched for build {Build}; shutting down current PID {Pid}.",
                            staged.Manifest.BuildNumber,
                            Environment.ProcessId);
                        Application.Current.Shutdown();
                        return;
                    }
                    catch (Exception ex)
                    {
                        ProgramUpdateStatus =
                            $"Update build {staged.Manifest.BuildNumber} remains staged because the updater helper could not start ({ex.GetType().Name}).";
                        s.Log.Warning(ex,"Program updater helper launch failed; current application remains active.");
                        return;
                    }
                    finally
                    {
                        if (!keepHandoffArmed)
                            programUpdateHandoffGate.DisarmHandoff();
                    }
                }
                finally
                {
                    programUpdateGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ProgramUpdateStatus =
                $"Staged program update handoff stopped ({ex.GetType().Name}). It will be retried on a later launch.";
            s.Log.Warning(ex,"Program updater safe-handoff loop stopped non-fatally.");
        }
    }

    private static bool IsPreparedHandoffCurrent(
        PreparedUpdateHandoff? prepared,
        StagedUpdate? preparedFor,
        StagedUpdate? current)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return prepared is not null && preparedFor is not null && ReferenceEquals(preparedFor, current);
    }

    private bool HasActiveGameProcess()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            return s.ProcessGuard.GetKnownBlockers().Count > 0;
        }
        catch (Exception ex)
        {
            s.Log.Warning(ex,"Could not prove the game is stopped; deferring program-update restart.");
            return true;
        }
    }

    private static Task DelayForSafeUpdateRetryAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Task.Delay(TimeSpan.FromSeconds(2), ct);
    }

    private static string ShortSha(string sha)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return sha.Length <= 12 ? sha : sha[..12];
    }
}
