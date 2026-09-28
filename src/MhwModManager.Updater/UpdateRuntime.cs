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

public sealed record UpdateTargetLaunchState(
    int SchemaVersion,
    string AttemptId,
    long BuildNumber,
    string SourceSha,
    int? ProcessId,
    DateTimeOffset? ProcessStartUtc,
    DateTimeOffset CreatedUtc)
{
    public void ValidateFor(UpdateManifest target)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"attempt={AttemptId}");
        if (SchemaVersion != UpdateProtocol.LaunchStateSchemaVersion)
            throw new InvalidDataException($"Unsupported updater launch-state schema {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(AttemptId))
            throw new InvalidDataException("Updater launch attempt id is missing.");
        if (BuildNumber != target.BuildNumber
            || !string.Equals(SourceSha, target.SourceSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Updater launch state does not match the target build.");
        if (ProcessId is <= 0)
            throw new InvalidDataException("Updater launch state contains an invalid process id.");
        if (ProcessId.HasValue != ProcessStartUtc.HasValue)
            throw new InvalidDataException("Updater launch state process identity is incomplete.");
    }
}

public static class UpdateLaunchStateStore
{
    public static string GetPath(UpdateApplyRequest request) => request.JournalPath + ".launch.json";

    public static async Task<UpdateTargetLaunchState> BeginAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var state = new UpdateTargetLaunchState(
            UpdateProtocol.LaunchStateSchemaVersion,
            Guid.NewGuid().ToString("N"),
            request.Manifest.BuildNumber,
            request.Manifest.SourceSha,
            null,
            null,
            DateTimeOffset.UtcNow);
        await WriteAsync(request, state, ct);
        return state;
    }

    public static async Task<UpdateTargetLaunchState> RecordStartedAsync(
        UpdateApplyRequest request,
        UpdateTargetLaunchState state,
        Process process,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={process.Id}");
        state.ValidateFor(request.Manifest);
        var started = state with
        {
            ProcessId = process.Id,
            ProcessStartUtc = new DateTimeOffset(process.StartTime.ToUniversalTime())
        };
        await WriteAsync(request, started, ct);
        return started;
    }

    public static async Task<UpdateTargetLaunchState?> ReadAsync(UpdateApplyRequest request, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var path = GetPath(request);
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, path);
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize<UpdateTargetLaunchState>(
                        await File.ReadAllTextAsync(path, ct), UpdateProtocol.Json)
                    ?? throw new InvalidDataException("Updater launch state is empty.");
        state.ValidateFor(request.Manifest);
        return state;
    }

    public static Process? TryOpenTrackedProcess(UpdateTargetLaunchState state)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={state.ProcessId}");
        if (!state.ProcessId.HasValue) return null;
        Process process;
        try
        {
            process = Process.GetProcessById(state.ProcessId.Value);
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            var actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (state.ProcessStartUtc is null
                || actualStart.UtcTicks != state.ProcessStartUtc.Value.UtcTicks)
            {
                process.Dispose();
                throw new InvalidDataException(
                    $"Updater target PID {state.ProcessId.Value} was reused by a different process.");
            }
            return process;
        }
        catch (InvalidOperationException)
        {
            process.Dispose();
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            process.Dispose();
            throw new InvalidDataException(
                $"Updater could not verify target PID {state.ProcessId.Value} identity.", ex);
        }
    }

    public static bool DeleteBestEffort(UpdateApplyRequest request, Action<string>? log = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var path = GetPath(request);
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"update launch-state cleanup deferred type={ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static Task WriteAsync(
        UpdateApplyRequest request,
        UpdateTargetLaunchState state,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"attempt={state.AttemptId}");
        state.ValidateFor(request.Manifest);
        var path = GetPath(request);
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, path);
        return UpdatePackageStager.WriteJsonAtomicallyAsync(path, state, ct);
    }
}

public sealed record UpdateStartupHealth(
    string Token,
    string AttemptId,
    int ProcessId,
    long BuildNumber,
    string SourceSha,
    DateTimeOffset HealthyUtc);

public static class UpdateHealthProtocol
{
    public const string TokenArgument = "--mhw-update-health-token";
    public const string FileArgument = "--mhw-update-health-file";
    public const string AttemptArgument = "--mhw-update-health-attempt";

    public static async Task AcknowledgeIfRequestedAsync(
        IReadOnlyList<string> args,
        UpdateBuildIdentity current,
        Action<string>? log,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var token = GetArgument(args, TokenArgument);
        var file = GetArgument(args, FileArgument);
        var attempt = GetArgument(args, AttemptArgument);
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(file)
            || string.IsNullOrWhiteSpace(attempt)) return;
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, file);
        var record = new UpdateStartupHealth(
            token,
            attempt,
            Environment.ProcessId,
            current.BuildNumber,
            current.SourceSha,
            DateTimeOffset.UtcNow);
        await UpdatePackageStager.WriteJsonAtomicallyAsync(file, record, ct);
        log?.Invoke(
            $"update startup health acknowledged build={current.BuildNumber} sha={current.ShortSha} attempt={attempt}");
    }

    public static async Task<bool> WaitForHealthyAsync(
        string path,
        string token,
        string attemptId,
        int? expectedProcessId,
        UpdateManifest target,
        Process? process,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={target.BuildNumber}; attempt={attemptId}");
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
                            && string.Equals(record.AttemptId, attemptId, StringComparison.Ordinal)
                            && (!expectedProcessId.HasValue || record.ProcessId == expectedProcessId.Value)
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

    public static IReadOnlyList<string> StripHealthArguments(IReadOnlyList<string> args)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"count={args.Count}");
        var cleaned = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            if (!IsHealthArgumentName(args[i]))
            {
                cleaned.Add(args[i]);
                continue;
            }

            if (i + 1 < args.Count && !IsHealthArgumentName(args[i + 1])) i++;
        }
        return cleaned;
    }

    private static bool IsHealthArgumentName(string value) =>
        string.Equals(value, TokenArgument, StringComparison.Ordinal)
        || string.Equals(value, FileArgument, StringComparison.Ordinal)
        || string.Equals(value, AttemptArgument, StringComparison.Ordinal);

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"name={name}");
        for (var i = args.Count - 2; i >= 0; i--)
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
