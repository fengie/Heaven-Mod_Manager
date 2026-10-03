using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed class UpdateMutexLease : IDisposable
{
    private readonly Thread ownerThread;
    private readonly ManualResetEventSlim ready = new(false);
    private readonly ManualResetEventSlim release = new(false);
    private Exception? acquireError;
    private bool ownsLease;
    private bool disposed;

    private UpdateMutexLease(string name, TimeSpan timeout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ownerThread = new Thread(() => OwnerMain(name, timeout))
        {
            IsBackground = true,
            Name = "MHWMM update mutex owner"
        };
        ownerThread.Start();
    }

    public static UpdateMutexLease Acquire(string installRoot, TimeSpan timeout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"installRoot={installRoot}");
        var normalized = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())));
        var lease = new UpdateMutexLease($"Global\\MHWMM.Update.{hash[..24]}", timeout);
        lease.ready.Wait();

        if (lease.acquireError is not null)
        {
            var error = lease.acquireError;
            lease.Dispose();
            throw error;
        }
        if (!lease.ownsLease)
        {
            lease.Dispose();
            throw new IOException("Another update is already applying to this installation.");
        }

        return lease;
    }

    private void OwnerMain(string name, TimeSpan timeout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            using var mutex = new Mutex(false, name);
            try
            {
                ownsLease = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // Windows grants ownership while reporting that the prior owner
                // terminated without releasing the mutex. That is exactly the
                // crash-recovery behavior required by the updater lease.
                ownsLease = true;
            }

            if (!ownsLease)
            {
                acquireError = new IOException("Another update is already applying to this installation.");
                return;
            }

            ready.Set();
            release.Wait();
            mutex.ReleaseMutex();
            ownsLease = false;
        }
        catch (Exception ex)
        {
            acquireError = ex;
        }
        finally
        {
            ready.Set();
        }
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed) return;
        disposed = true;
        release.Set();
        ownerThread.Join();
        ready.Dispose();
        release.Dispose();
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
    public static string GetPath(UpdateApplyRequest request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return request.JournalPath + ".launch.json";
    }

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

    public static string FormatArgumentsForDiagnostics(IReadOnlyList<string> args)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(args);
        var safe = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            safe.Add(argument);
            if (!IsSensitiveHealthArgument(argument) || i + 1 >= args.Count) continue;
            safe.Add("<redacted>");
            i++;
        }
        return string.Join(" ", safe);
    }

    private static bool IsSensitiveHealthArgument(string argument)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Equals(argument, TokenArgument, StringComparison.Ordinal)
            || string.Equals(argument, FileArgument, StringComparison.Ordinal)
            || string.Equals(argument, AttemptArgument, StringComparison.Ordinal);
    }

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
    private const int TopologyBindingHexLength = 24;

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

    public static string CreateBoundTransactionId(
        UpdateManifest manifest,
        string installRoot,
        string stagingRoot,
        string? managerHomeRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={manifest.BuildNumber}");
        manifest.Validate();
        var binding = ComputeTopologyBinding(
            manifest,
            installRoot,
            stagingRoot,
            managerHomeRoot);
        return $"{manifest.BuildNumber}-{binding}-{Guid.NewGuid():N}";
    }

    public static async Task<UpdateApplyRequest> ReadForHelperAsync(
        string requestPath,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={requestPath}");
        var updaterRoot = NormalizeDirectoryPath(UpdatePackageStager.GetUpdaterRoot());
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);

        var fullRequestPath = Path.GetFullPath(requestPath);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, fullRequestPath);
        var request = await ReadAsync(fullRequestPath, ct);
        return await ValidateHelperTopologyAsync(
            fullRequestPath,
            updaterRoot,
            request,
            ct);
    }

    private static async Task<UpdateApplyRequest> ValidateHelperTopologyAsync(
        string requestPath,
        string updaterRoot,
        UpdateApplyRequest request,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={request.Manifest.BuildNumber}");
        var transactionRoot = Path.GetDirectoryName(requestPath)
            ?? throw new InvalidDataException("Updater request has no transaction directory.");
        transactionRoot = NormalizeDirectoryPath(transactionRoot);
        var transactionsRoot = NormalizeDirectoryPath(Path.Combine(updaterRoot, "transactions"));
        RequireSamePath(
            Path.GetDirectoryName(transactionRoot)
                ?? throw new InvalidDataException("Updater transaction has no parent directory."),
            transactionsRoot,
            "transaction parent");
        RequireSamePath(
            requestPath,
            Path.Combine(transactionRoot, "apply-request.json"),
            "request file");
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, transactionRoot);

        var transactionName = Path.GetFileName(transactionRoot);
        var transactionParts = transactionName.Split('-', StringSplitOptions.None);
        if (transactionParts.Length != 3
            || !long.TryParse(transactionParts[0], out var transactionBuild)
            || transactionBuild != request.Manifest.BuildNumber
            || transactionParts[1].Length != TopologyBindingHexLength
            || transactionParts[1].Any(c => !Uri.IsHexDigit(c))
            || !Guid.TryParseExact(transactionParts[2], "N", out _))
            throw new InvalidDataException(
                $"Updater transaction identity is malformed or does not match build {request.Manifest.BuildNumber}.");

        var installRoot = NormalizeDirectoryPath(request.InstallRoot);
        RequireCanonicalInput(request.InstallRoot, installRoot, nameof(request.InstallRoot));
        if (!Directory.Exists(installRoot))
            throw new DirectoryNotFoundException($"Install root is missing: {installRoot}");
        if ((File.GetAttributes(installRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Install root may not be a reparse point: {installRoot}");
        if (UpdatePathSafety.IsDevelopmentLayout(installRoot))
            throw new InvalidOperationException(
                "Self-update is disabled for a repository/development release layout.");

        var stagingRoot = NormalizeDirectoryPath(request.StagingRoot);
        RequireCanonicalInput(request.StagingRoot, stagingRoot, nameof(request.StagingRoot));
        ValidateStagingRoot(updaterRoot, stagingRoot, request.Manifest.BuildNumber);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, stagingRoot);

        string? managerHomeRoot = null;
        if (!string.IsNullOrWhiteSpace(request.ManagerHomeRoot))
        {
            managerHomeRoot = NormalizeDirectoryPath(request.ManagerHomeRoot);
            RequireCanonicalInput(request.ManagerHomeRoot, managerHomeRoot, nameof(request.ManagerHomeRoot));
            if (!Directory.Exists(managerHomeRoot))
                throw new DirectoryNotFoundException(
                    $"Updater restart manager home is missing: {managerHomeRoot}");
            if ((File.GetAttributes(managerHomeRoot) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(
                    $"Updater restart manager home may not be a reparse point: {managerHomeRoot}");
        }

        var expectedBinding = ComputeTopologyBinding(
            request.Manifest,
            installRoot,
            stagingRoot,
            managerHomeRoot);
        if (!string.Equals(
                transactionParts[1],
                expectedBinding,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Updater request topology does not match its bound transaction identity.");

        var backupRoot = NormalizeDirectoryPath(Path.Combine(transactionRoot, "backup"));
        var journalPath = Path.GetFullPath(Path.Combine(transactionRoot, "journal.json"));
        var healthFile = Path.GetFullPath(Path.Combine(transactionRoot, "health.json"));
        var pendingPath = Path.GetFullPath(Path.Combine(updaterRoot, UpdateProtocol.PendingFileName));
        RequireSamePath(request.BackupRoot, backupRoot, nameof(request.BackupRoot));
        RequireSamePath(request.JournalPath, journalPath, nameof(request.JournalPath));
        RequireSamePath(request.HealthFile, healthFile, nameof(request.HealthFile));
        RequireSamePath(request.PendingPath, pendingPath, nameof(request.PendingPath));

        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, backupRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, journalPath);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, healthFile);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(updaterRoot, pendingPath);

        if (string.IsNullOrWhiteSpace(request.HealthToken)
            || request.HealthToken.Length != 64
            || request.HealthToken.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Updater request health token is malformed.");
        if (request.CurrentProcessId <= 0)
            throw new InvalidDataException("Updater request process id must be positive.");
        if (request.RestartArguments is null)
            throw new InvalidDataException("Updater request restart arguments are missing.");
        var sanitizedArguments = UpdateArgumentSanitizer.RemoveHealthArguments(request.RestartArguments);
        if (!sanitizedArguments.SequenceEqual(request.RestartArguments, StringComparer.Ordinal))
            throw new InvalidDataException(
                "Updater request restart arguments contain updater-owned health arguments.");

        if (File.Exists(pendingPath))
        {
            var pending = JsonSerializer.Deserialize<StagedUpdate>(
                              await File.ReadAllTextAsync(pendingPath, ct),
                              UpdateProtocol.Json)
                          ?? throw new InvalidDataException("Pending update state is empty.");
            pending.Manifest.Validate();
            if (pending.Manifest != request.Manifest)
                throw new InvalidDataException(
                    "Updater request manifest does not match canonical pending update state.");
            RequireSamePath(
                pending.StagingRoot,
                stagingRoot,
                "pending staging root");
        }
        else if (!await IsConfirmedTransactionAsync(journalPath, request.Manifest, ct))
        {
            throw new InvalidDataException(
                "Updater request has no matching canonical pending update state.");
        }

        return request with
        {
            InstallRoot = installRoot,
            StagingRoot = stagingRoot,
            BackupRoot = backupRoot,
            JournalPath = journalPath,
            PendingPath = pendingPath,
            HealthFile = healthFile,
            ManagerHomeRoot = managerHomeRoot
        };
    }

    private static async Task<bool> IsConfirmedTransactionAsync(
        string journalPath,
        UpdateManifest manifest,
        CancellationToken ct)
    {
        if (!File.Exists(journalPath)) return false;
        var journal = JsonSerializer.Deserialize<UpdateJournal>(
                          await File.ReadAllTextAsync(journalPath, ct),
                          UpdateProtocol.Json)
                      ?? throw new InvalidDataException("Updater recovery journal is empty.");
        return journal.Phase == UpdateJournalPhase.Confirmed
            && journal.TargetBuildNumber == manifest.BuildNumber
            && string.Equals(
                journal.TargetSourceSha,
                manifest.SourceSha,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateStagingRoot(
        string updaterRoot,
        string stagingRoot,
        long buildNumber)
    {
        var relative = Path.GetRelativePath(updaterRoot, stagingRoot)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3
            || !string.Equals(segments[0], "staging", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "payload", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Updater staging root does not match the canonical staging topology.");

        var attemptParts = segments[1].Split('-', StringSplitOptions.None);
        if (attemptParts.Length != 2
            || !long.TryParse(attemptParts[0], out var stagedBuild)
            || stagedBuild != buildNumber
            || !Guid.TryParseExact(attemptParts[1], "N", out _))
            throw new InvalidDataException(
                "Updater staging identity is malformed or belongs to another build.");
    }

    private static string ComputeTopologyBinding(
        UpdateManifest manifest,
        string installRoot,
        string stagingRoot,
        string? managerHomeRoot)
    {
        var material = string.Join(
            "\n",
            manifest.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.SourceSha.ToLowerInvariant(),
            NormalizeDirectoryPath(installRoot).ToUpperInvariant(),
            NormalizeDirectoryPath(stagingRoot).ToUpperInvariant(),
            string.IsNullOrWhiteSpace(managerHomeRoot)
                ? string.Empty
                : NormalizeDirectoryPath(managerHomeRoot).ToUpperInvariant());
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..TopologyBindingHexLength];
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void RequireCanonicalInput(
        string actual,
        string canonical,
        string label)
    {
        var trimmed = actual.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!Path.IsPathFullyQualified(actual)
            || !string.Equals(trimmed, canonical, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Updater request {label} is not a canonical absolute path.");
    }

    private static void RequireSamePath(
        string actual,
        string expected,
        string label)
    {
        var fullActual = Path.GetFullPath(actual)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullExpected = Path.GetFullPath(expected)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RequireCanonicalInput(actual, fullActual, label);
        if (!string.Equals(fullActual, fullExpected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Updater request {label} does not match its canonical transaction topology.");
    }
}
