using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed class UpdateMutexLease : IDisposable
{
    private readonly Semaphore semaphore;
    private bool ownsLease;

    private UpdateMutexLease(Semaphore semaphore, bool ownsLease)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.semaphore = semaphore;
        this.ownsLease = ownsLease;
    }

    public static UpdateMutexLease Acquire(string installRoot, TimeSpan timeout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"installRoot={installRoot}");
        var normalized = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())));
        var semaphore = new Semaphore(1, 1, $"Local\\MHWMM.Update.{hash[..24]}");
        try
        {
            var acquired = semaphore.WaitOne(timeout);
            if (!acquired)
                throw new IOException("Another update is already applying to this installation.");
            return new UpdateMutexLease(semaphore, true);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ownsLease)
        {
            ownsLease = false;
            semaphore.Release();
        }
        semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}

public static class UpdateProcessWaiter
{
    public static async Task WaitForExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={processId}");
        if (processId <= 0) return;
        Process? process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        using (process)
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for process {processId} to exit.");
            }
        }
    }
}

public sealed record UpdateStartupHealth(
    string Token,
    long BuildNumber,
    string SourceSha,
    DateTimeOffset HealthyUtc);

public static class UpdateHealthProtocol
{
    public const string TokenArgument = "--mhw-update-health-token";
    public const string FileArgument = "--mhw-update-health-file";

    public static async Task AcknowledgeIfRequestedAsync(
        IReadOnlyList<string> args,
        UpdateBuildIdentity current,
        Action<string>? log,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var token = GetArgument(args, TokenArgument);
        var file = GetArgument(args, FileArgument);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(file)) return;
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, file);
        var record = new UpdateStartupHealth(token, current.BuildNumber, current.SourceSha, DateTimeOffset.UtcNow);
        await UpdatePackageStager.WriteJsonAtomicallyAsync(file, record, ct);
        log?.Invoke($"update startup health acknowledged build={current.BuildNumber} sha={current.ShortSha}");
    }

    public static async Task<bool> WaitForHealthyAsync(
        string path,
        string token,
        UpdateManifest target,
        Process? process,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={target.BuildNumber}");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                if (File.Exists(path))
                {
                    try
                    {
                        var record = JsonSerializer.Deserialize<UpdateStartupHealth>(
                            await File.ReadAllTextAsync(path, timeoutCts.Token), UpdateProtocol.Json);
                        if (record is not null
                            && string.Equals(record.Token, token, StringComparison.Ordinal)
                            && record.BuildNumber == target.BuildNumber
                            && string.Equals(record.SourceSha, target.SourceSha, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch (JsonException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }

                if (process is not null && process.HasExited) return false;
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"name={name}");
        for (var i = 0; i + 1 < args.Count; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}

public static class UpdateRequestStore
{
    public static async Task WriteAsync(string path, UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        await UpdatePackageStager.WriteJsonAtomicallyAsync(path, request, ct);
    }

    public static async Task<UpdateApplyRequest> ReadAsync(string path, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        var value = JsonSerializer.Deserialize<UpdateApplyRequest>(
                        await File.ReadAllTextAsync(path, ct), UpdateProtocol.Json)
                    ?? throw new InvalidDataException("Updater apply request is empty.");
        value.Manifest.Validate();
        return value;
    }
}
