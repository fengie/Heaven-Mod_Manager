using System.Diagnostics;
using MhwModManager.Core;

namespace MhwModManager.Updater;

// The helper owns the process operations; this coordinator owns their recovery order.
public sealed class UpdateRestartCoordinator(
    Func<string, Process> startTargetApplication,
    Func<string, Process> startPreviousApplication,
    Func<Process, Task<bool>> stopApplication,
    Action<string>? log = null)
{
    public async Task<int> RunAsync(UpdateInstaller installer, UpdateApplyRequest request, TimeSpan healthTimeout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        Process? process = null;
        try
        {
            try
            {
                if (File.Exists(request.HealthFile)) File.Delete(request.HealthFile);
                var launch = await UpdateLaunchStateStore.BeginAsync(request, CancellationToken.None);
                process = startTargetApplication(launch.AttemptId);
                launch = await UpdateLaunchStateStore.RecordStartedAsync(
                    request, launch, process, CancellationToken.None);
                log?.Invoke(
                    $"update restarted target build={request.Manifest.BuildNumber} pid={process.Id} attempt={launch.AttemptId}");
                if (await UpdateHealthProtocol.WaitForHealthyAsync(
                        request.HealthFile,
                        request.HealthToken,
                        launch.AttemptId,
                        process.Id,
                        request.Manifest,
                        process,
                        healthTimeout,
                        CancellationToken.None))
                {
                    await installer.ConfirmAsync(request, CancellationToken.None);
                    UpdateLaunchStateStore.DeleteBestEffort(request, log);
                    log?.Invoke($"update confirmed build={request.Manifest.BuildNumber}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"update restart/confirmation failed type={ex.GetType().Name}");
            }

            return await RecoverAsync(installer, request, process);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public async Task<int> ResumeAsync(UpdateInstaller installer, UpdateApplyRequest request, TimeSpan healthTimeout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var launch = await UpdateLaunchStateStore.ReadAsync(request, CancellationToken.None);
        if (launch is null)
        {
            log?.Invoke("update resume found no prior target launch; starting a new launch attempt");
            return await RunAsync(installer, request, healthTimeout);
        }

        Process? process = null;
        try
        {
            if (!launch.ProcessId.HasValue)
            {
                log?.Invoke(
                    $"update resume found ambiguous launch attempt={launch.AttemptId}; waiting for its health without launching a duplicate");
                if (await UpdateHealthProtocol.WaitForHealthyAsync(
                        request.HealthFile,
                        request.HealthToken,
                        launch.AttemptId,
                        null,
                        request.Manifest,
                        null,
                        healthTimeout,
                        CancellationToken.None))
                {
                    await installer.ConfirmAsync(request, CancellationToken.None);
                    UpdateLaunchStateStore.DeleteBestEffort(request, log);
                    log?.Invoke($"update confirmed resumed launch build={request.Manifest.BuildNumber}");
                    return 0;
                }

                log?.Invoke(
                    "update recovery deferred: original target launch outcome is ambiguous; retain backup and journal");
                return 4;
            }

            try
            {
                process = UpdateLaunchStateStore.TryOpenTrackedProcess(launch);
            }
            catch (InvalidDataException ex)
            {
                log?.Invoke($"update recovery deferred: {ex.Message}");
                return 4;
            }

            if (process is not null)
            {
                log?.Invoke(
                    $"update resume attached to target pid={process.Id} attempt={launch.AttemptId}");
                if (await UpdateHealthProtocol.WaitForHealthyAsync(
                        request.HealthFile,
                        request.HealthToken,
                        launch.AttemptId,
                        launch.ProcessId,
                        request.Manifest,
                        process,
                        healthTimeout,
                        CancellationToken.None))
                {
                    await installer.ConfirmAsync(request, CancellationToken.None);
                    UpdateLaunchStateStore.DeleteBestEffort(request, log);
                    log?.Invoke($"update confirmed resumed launch build={request.Manifest.BuildNumber}");
                    return 0;
                }
            }
            else
            {
                log?.Invoke(
                    $"update resume proved prior target pid={launch.ProcessId} is no longer running");
            }

            return await RecoverAsync(installer, request, process);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private async Task<int> RecoverAsync(
        UpdateInstaller installer,
        UpdateApplyRequest request,
        Process? process)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        if (process is not null && !process.HasExited && !await stopApplication(process))
        {
            log?.Invoke(
                "update recovery deferred: target process exit could not be proven; retain backup and journal");
            return 4;
        }

        try
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await installer.RollbackAsync(request, recovery.Token);
            var previousMarker = await ReleaseInstallMarker.LoadAsync(request.InstallRoot, recovery.Token);
            UpdateLaunchStateStore.DeleteBestEffort(request, log);
            using var previous = startPreviousApplication(previousMarker.ExecutableRelativePath);
            log?.Invoke(
                $"update restored previous application pid={previous.Id} executable={previousMarker.ExecutableRelativePath}");
            return 5;
        }
        catch (Exception ex)
        {
            log?.Invoke($"update recovery failed type={ex.GetType().Name}; retain backup and journal");
            return 4;
        }
    }
}
