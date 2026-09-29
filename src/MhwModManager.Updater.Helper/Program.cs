using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Updater;

namespace MhwModManager.Updater.Helper;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UpdaterProgressHost? progress = null;
        try
        {
            var requestPath = RequireArgument(args, "--request");
            var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
            UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, requestPath);
            var request = await UpdateRequestStore.ReadAsync(requestPath, CancellationToken.None);
            var logPath = Path.Combine(updaterRoot, "updater.log");
            Log(logPath, $"helper start build={request.Manifest.BuildNumber} source={request.Manifest.SourceSha}");

            try
            {
                progress = new UpdaterProgressHost(request.Manifest.ProductVersion);
            }
            catch (Exception ex)
            {
                progress?.Dispose();
                progress = null;
                Log(logPath,
                    $"updater progress window unavailable type={ex.GetType().Name} message={ex.Message}");
            }

            progress?.ReportStep(
                1,
                "Preparing update",
                "Waiting for MHW Manual Mod Manager to close safely...");

            using var lease = UpdateMutexLease.Acquire(request.InstallRoot, TimeSpan.Zero);
            await UpdateProcessWaiter.WaitForExitAsync(
                request.CurrentProcessId, TimeSpan.FromMinutes(5), CancellationToken.None);

            var installer = new UpdateInstaller(log: message => Log(logPath, message));
            var phase = await ReadJournalPhaseAsync(request.JournalPath);
            if (phase == UpdateJournalPhase.Confirmed)
            {
                UpdateLaunchStateStore.DeleteBestEffort(request, message => Log(logPath, message));
                Log(logPath, "helper found already-confirmed update; nothing to do");
                progress?.Succeed(
                    "The update was already confirmed. Opening MHW Manual Mod Manager...");
                return 0;
            }

            if (phase == UpdateJournalPhase.AppliedAwaitingHealth)
            {
                Log(logPath, "helper resuming post-apply health confirmation");
                progress?.ReportStep(
                    4,
                    "Verifying the update",
                    "Resuming startup verification for the updated app...");
                var resumed = await ResumeAndConfirmAsync(
                    installer, request, logPath, progress);
                FinishProgress(progress, resumed);
                return resumed;
            }

            progress?.ReportStep(
                2,
                "Installing update",
                "Applying the verified update package. Your mods and user data are left untouched.");
            try
            {
                await installer.ApplyAsync(request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log(logPath, $"helper apply failed type={ex.GetType().Name} message={ex.Message}");
                if (await ReadJournalPhaseAsync(request.JournalPath) == UpdateJournalPhase.RolledBack)
                {
                    Log(logPath,
                        "helper rollback completed after apply failure; restarting previous application");
                    var previousMarker = await ReleaseInstallMarker.LoadAsync(
                        request.InstallRoot, CancellationToken.None);
                    StartApplication(
                        request,
                        previousMarker.ExecutableRelativePath,
                        includeHealthArguments: false,
                        attemptId: null).Dispose();
                    progress?.Fail(
                        "Update failed — previous version restored",
                        "The update could not be installed, so the previous version was restored safely. See updater.log for details.");
                }
                else
                {
                    progress?.Fail(
                        "Update failed",
                        "The update could not be applied safely. Recovery data was kept. See updater.log for details.");
                }

                return 2;
            }

            progress?.ReportStep(
                3,
                "Starting the updated app",
                "The update files are installed. Starting the new MHW Manual Mod Manager build...");
            var result = await LaunchAndConfirmAsync(
                installer, request, logPath, progress);
            FinishProgress(progress, result);
            return result;
        }
        catch (Exception ex)
        {
            progress?.Fail(
                "Updater error",
                $"The updater stopped because of {ex.GetType().Name}: {ex.Message}");
            try
            {
                var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
                Directory.CreateDirectory(updaterRoot);
                Log(Path.Combine(updaterRoot, "updater.log"),
                    $"helper fatal type={ex.GetType().Name} message={ex.Message}");
            }
            catch
            {
            }

            return 3;
        }
        finally
        {
            progress?.Dispose();
        }
    }

    private static void FinishProgress(UpdaterProgressHost? progress, int result)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"result={result}");
        if (progress is null) return;

        switch (result)
        {
            case 0:
                progress.Succeed(
                    "The update is installed and the new build started successfully.");
                break;
            case 5:
                progress.Fail(
                    "Update rolled back safely",
                    "The new build did not start correctly, so the previous version was restored and reopened.");
                break;
            case 4:
                progress.Fail(
                    "Update needs attention",
                    "The updater could not safely confirm or roll back the new build. Recovery data was kept. See updater.log for details.");
                break;
            default:
                progress.Fail(
                    "Update did not complete",
                    $"The updater stopped with result code {result}. See updater.log for details.");
                break;
        }
    }

    private static async Task<int> LaunchAndConfirmAsync(
        UpdateInstaller installer,
        UpdateApplyRequest request,
        string logPath,
        UpdaterProgressHost? progress)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var coordinator = CreateRestartCoordinator(request, logPath, progress);
        return await coordinator.RunAsync(
            installer, request, TimeSpan.FromSeconds(90));
    }

    private static async Task<int> ResumeAndConfirmAsync(
        UpdateInstaller installer,
        UpdateApplyRequest request,
        string logPath,
        UpdaterProgressHost? progress)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var coordinator = CreateRestartCoordinator(request, logPath, progress);
        return await coordinator.ResumeAsync(
            installer, request, TimeSpan.FromSeconds(90));
    }

    private static UpdateRestartCoordinator CreateRestartCoordinator(
        UpdateApplyRequest request,
        string logPath,
        UpdaterProgressHost? progress)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new UpdateRestartCoordinator(
            attemptId => StartApplication(
                request,
                request.Manifest.ExecutableRelativePath,
                includeHealthArguments: true,
                attemptId),
            executableRelativePath => StartApplication(
                request,
                executableRelativePath,
                includeHealthArguments: false,
                attemptId: null),
            process => StopProcessAsync(process, logPath),
            message => Log(logPath, message),
            stage =>
            {
                if (progress is not null)
                    progress.ReportRestartStage(stage);
            });
    }

    private static Process StartApplication(
        UpdateApplyRequest request,
        string executableRelativePath,
        bool includeHealthArguments,
        string? attemptId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"health={includeHealthArguments}; executable={executableRelativePath}");
        var executable = UpdatePathSafety.CombineUnderRoot(
            request.InstallRoot, executableRelativePath);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(
            request.InstallRoot, executable);
        if (!File.Exists(executable))
            throw new FileNotFoundException(
                "Updater restart executable is missing.", executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = request.InstallRoot
        };
        if (!string.IsNullOrWhiteSpace(request.ManagerHomeRoot))
        {
            var managerHome = Path.GetFullPath(request.ManagerHomeRoot);
            if (!Directory.Exists(managerHome))
                throw new DirectoryNotFoundException(
                    $"Updater restart manager home is missing: {managerHome}");
            start.Environment["MOD_MANAGER_HOME"] = managerHome;
            start.Environment["MHW_MANAGER_HOME"] = managerHome;
        }

        foreach (var argument in request.RestartArguments)
            start.ArgumentList.Add(argument);
        if (includeHealthArguments)
        {
            if (string.IsNullOrWhiteSpace(attemptId))
                throw new InvalidOperationException(
                    "Updater target launch requires a health attempt id.");
            start.ArgumentList.Add(UpdateHealthProtocol.TokenArgument);
            start.ArgumentList.Add(request.HealthToken);
            start.ArgumentList.Add(UpdateHealthProtocol.FileArgument);
            start.ArgumentList.Add(request.HealthFile);
            start.ArgumentList.Add(UpdateHealthProtocol.AttemptArgument);
            start.ArgumentList.Add(attemptId);
        }

        return ProcessDebug.Start(
            start,
            includeHealthArguments
                ? "restart updated MHW Manual Mod Manager"
                : "restart previous MHW Manual Mod Manager after rollback");
    }

    private static async Task<bool> StopProcessAsync(
        Process process,
        string logPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={process.Id}");
        try
        {
            if (process.HasExited) return true;
            process.Kill(entireProcessTree: true);
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(cts.Token);
            return process.HasExited;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or OperationCanceledException)
        {
            Log(logPath,
                $"could not fully stop failed updated process pid={process.Id}: {ex.Message}");
            return false;
        }
    }

    private static async Task<UpdateJournalPhase?> ReadJournalPhaseAsync(
        string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        if (!File.Exists(path)) return null;
        var journal = JsonSerializer.Deserialize<UpdateJournal>(
                          await File.ReadAllTextAsync(path),
                          UpdateProtocol.Json)
                      ?? throw new InvalidDataException(
                          "Updater recovery journal is empty.");
        return journal.Phase;
    }

    private static string RequireArgument(string[] args, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"name={name}");
        for (var i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return Path.GetFullPath(args[i + 1]);
        throw new ArgumentException(
            $"Missing required helper argument {name}.");
    }

    private static void Log(string path, string message)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var line =
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Updater log write failed: {ex.GetType().Name}");
        }
    }
}
