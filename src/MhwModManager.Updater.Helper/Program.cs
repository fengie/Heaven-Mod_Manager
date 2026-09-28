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
        try
        {
            var requestPath = RequireArgument(args, "--request");
            var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
            UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, requestPath);
            var request = await UpdateRequestStore.ReadAsync(requestPath, CancellationToken.None);
            var logPath = Path.Combine(updaterRoot, "updater.log");
            Log(logPath, $"helper start build={request.Manifest.BuildNumber} source={request.Manifest.SourceSha}");

            using var lease = UpdateMutexLease.Acquire(request.InstallRoot, TimeSpan.Zero);
            await UpdateProcessWaiter.WaitForExitAsync(
                request.CurrentProcessId, TimeSpan.FromMinutes(5), CancellationToken.None);

            var installer = new UpdateInstaller(log: message => Log(logPath, message));
            var phase = await ReadJournalPhaseAsync(request.JournalPath);
            if (phase == UpdateJournalPhase.Confirmed)
            {
                Log(logPath, "helper found already-confirmed update; nothing to do");
                return 0;
            }
            if (phase == UpdateJournalPhase.AppliedAwaitingHealth)
            {
                Log(logPath, "helper resuming post-apply health confirmation");
                if (await UpdateHealthProtocol.WaitForHealthyAsync(
                        request.HealthFile, request.HealthToken, request.Manifest, null,
                        TimeSpan.FromSeconds(1), CancellationToken.None))
                {
                    await installer.ConfirmAsync(request, CancellationToken.None);
                    Log(logPath, "helper confirmed previously healthy update");
                    return 0;
                }
                return await LaunchAndConfirmAsync(installer, request, logPath);
            }

            try
            {
                await installer.ApplyAsync(request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log(logPath, $"helper apply failed type={ex.GetType().Name} message={ex.Message}");
                if (await ReadJournalPhaseAsync(request.JournalPath) == UpdateJournalPhase.RolledBack)
                {
                    Log(logPath, "helper rollback completed after apply failure; restarting previous application");
                    StartApplication(request, includeHealthArguments: false);
                }
                return 2;
            }

            return await LaunchAndConfirmAsync(installer, request, logPath);
        }
        catch (Exception ex)
        {
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
    }

    private static async Task<int> LaunchAndConfirmAsync(
        UpdateInstaller installer,
        UpdateApplyRequest request,
        string logPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        try { if (File.Exists(request.HealthFile)) File.Delete(request.HealthFile); } catch (IOException ex) { Log(logPath, $"health marker cleanup deferred: {ex.Message}"); }
        using var process = StartApplication(request, includeHealthArguments: true);
        Log(logPath, $"helper restarted target build={request.Manifest.BuildNumber} pid={process.Id}");
        var healthy = await UpdateHealthProtocol.WaitForHealthyAsync(
            request.HealthFile,
            request.HealthToken,
            request.Manifest,
            process,
            TimeSpan.FromSeconds(90),
            CancellationToken.None);
        if (healthy)
        {
            await installer.ConfirmAsync(request, CancellationToken.None);
            Log(logPath, $"helper confirmed target build={request.Manifest.BuildNumber}");
            return 0;
        }

        Log(logPath, $"helper target build={request.Manifest.BuildNumber} failed startup health; rolling back");
        await StopProcessBestEffortAsync(process, logPath);
        try
        {
            await installer.RollbackAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log(logPath, $"helper rollback after startup-health failure failed type={ex.GetType().Name} message={ex.Message}");
            return 4;
        }
        StartApplication(request, includeHealthArguments: false).Dispose();
        Log(logPath, "helper restarted previous application after rollback");
        return 5;
    }

    private static Process StartApplication(UpdateApplyRequest request, bool includeHealthArguments)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"health={includeHealthArguments}");
        var executable = UpdatePathSafety.CombineUnderRoot(
            request.InstallRoot, request.Manifest.ExecutableRelativePath);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(request.InstallRoot, executable);
        if (!File.Exists(executable))
            throw new FileNotFoundException("Updater restart executable is missing.", executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = request.InstallRoot
        };
        foreach (var argument in request.RestartArguments)
            start.ArgumentList.Add(argument);
        if (includeHealthArguments)
        {
            start.ArgumentList.Add(UpdateHealthProtocol.TokenArgument);
            start.ArgumentList.Add(request.HealthToken);
            start.ArgumentList.Add(UpdateHealthProtocol.FileArgument);
            start.ArgumentList.Add(request.HealthFile);
        }
        return Process.Start(start)
               ?? throw new InvalidOperationException("Failed to start MHW Manual Mod Manager after update.");
    }

    private static async Task StopProcessBestEffortAsync(Process process, string logPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={process.Id}");
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Log(logPath, $"could not fully stop failed updated process pid={process.Id}: {ex.Message}");
        }
    }

    private static async Task<UpdateJournalPhase?> ReadJournalPhaseAsync(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        if (!File.Exists(path)) return null;
        var journal = JsonSerializer.Deserialize<UpdateJournal>(
                          await File.ReadAllTextAsync(path), UpdateProtocol.Json)
                      ?? throw new InvalidDataException("Updater recovery journal is empty.");
        return journal.Phase;
    }

    private static string RequireArgument(string[] args, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"name={name}");
        for (var i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return Path.GetFullPath(args[i + 1]);
        throw new ArgumentException($"Missing required helper argument {name}.");
    }

    private static void Log(string path, string message)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var line = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Updater log write failed: {ex.GetType().Name}");
        }
    }
}
