using System.Diagnostics;
using MhwModManager.Core;

namespace MhwModManager.Updater;

// The helper owns the process operations; this coordinator owns their recovery order.
public sealed class UpdateRestartCoordinator(
    Func<bool, Process> startApplication,
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
                process = startApplication(true);
                log?.Invoke($"update restarted target build={request.Manifest.BuildNumber} pid={process.Id}");
                if (await UpdateHealthProtocol.WaitForHealthyAsync(request.HealthFile, request.HealthToken,
                        request.Manifest, process, healthTimeout, CancellationToken.None))
                {
                    await installer.ConfirmAsync(request, CancellationToken.None);
                    log?.Invoke($"update confirmed build={request.Manifest.BuildNumber}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"update restart/confirmation failed type={ex.GetType().Name}");
            }

            // Failure to stop is a recovery blocker, not permission to replace loaded files.
            if (process is not null && !await stopApplication(process))
            {
                log?.Invoke("update recovery deferred: target process exit could not be proven; retain backup and journal");
                return 4;
            }
            try
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await installer.RollbackAsync(request, recovery.Token);
                using var previous = startApplication(false);
                log?.Invoke($"update restored previous application pid={previous.Id}");
                return 5;
            }
            catch (Exception ex)
            {
                log?.Invoke($"update recovery failed type={ex.GetType().Name}; retain backup and journal");
                return 4;
            }
        }
        finally
        {
            process?.Dispose();
        }
    }
}
