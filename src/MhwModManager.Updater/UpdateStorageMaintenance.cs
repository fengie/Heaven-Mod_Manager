using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed record UpdateStorageCleanupResult(
    int DeletedStagingAttempts,
    int DeletedTransactions,
    int DeferredEntries);

public static class UpdateStorageMaintenance
{
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);

    public static Task<UpdateStorageCleanupResult> RunAsync(
        Action<string>? log,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return CleanupRootAsync(
            UpdatePackageStager.GetUpdaterRoot(),
            DateTimeOffset.UtcNow,
            DefaultMinimumAge,
            log,
            ct);
    }

    public static async Task<UpdateStorageCleanupResult> CleanupRootAsync(
        string updaterRoot,
        DateTimeOffset nowUtc,
        TimeSpan minimumAge,
        Action<string>? log,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={updaterRoot}");
        if (minimumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(minimumAge),
                "Updater cleanup minimum age may not be negative.");

        var fullRoot = NormalizeDirectory(updaterRoot);
        EnsureCleanupRootAllowed(fullRoot);

        string? protectedStagingAttempt = null;
        var skipStagingCleanup = false;
        var pendingPath = Path.Combine(
            fullRoot,
            UpdateProtocol.PendingFileName);
        if (File.Exists(pendingPath))
        {
            try
            {
                UpdatePathSafety.EnsureExistingComponentsNotReparse(
                    fullRoot,
                    pendingPath);
                var pending = JsonSerializer.Deserialize<StagedUpdate>(
                                  await File.ReadAllTextAsync(
                                      pendingPath,
                                      ct),
                                  UpdateProtocol.Json)
                              ?? throw new InvalidDataException(
                                  "Pending update state is empty.");
                pending.Manifest.Validate();
                protectedStagingAttempt = GetCanonicalStagingAttemptRoot(
                    fullRoot,
                    pending.StagingRoot,
                    pending.Manifest.BuildNumber);
                UpdatePathSafety.EnsureExistingComponentsNotReparse(
                    fullRoot,
                    pending.StagingRoot);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A malformed pending marker may represent recovery state we do
                // not fully understand. Fail closed for staging cleanup rather
                // than guessing which payload can be removed.
                skipStagingCleanup = true;
                log?.Invoke(
                    $"update storage cleanup preserved all staging because pending state could not be validated type={ex.GetType().Name}");
            }
        }

        var deletedStaging = 0;
        var deletedTransactions = 0;
        var deferred = 0;

        if (!skipStagingCleanup)
        {
            var stagingRoot = Path.Combine(fullRoot, "staging");
            if (Directory.Exists(stagingRoot))
            {
                UpdatePathSafety.EnsureExistingComponentsNotReparse(
                    fullRoot,
                    stagingRoot);
                foreach (var attempt in Directory.EnumerateDirectories(
                             stagingRoot,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    if (SamePath(attempt, protectedStagingAttempt))
                        continue;
                    if (!TryParseStagingAttemptName(
                            Path.GetFileName(attempt),
                            out _))
                    {
                        deferred++;
                        continue;
                    }

                    try
                    {
                        var newestWriteUtc = InspectSafeTreeNewestWriteUtc(
                            fullRoot,
                            attempt,
                            ct);
                        if (!IsOldEnough(
                                newestWriteUtc,
                                nowUtc,
                                minimumAge))
                        {
                            deferred++;
                            continue;
                        }

                        DeleteSafeTree(fullRoot, attempt, ct);
                        deletedStaging++;
                        log?.Invoke(
                            $"update storage cleanup removed orphan staging attempt={Path.GetFileName(attempt)}");
                    }
                    catch (Exception ex) when (
                        ex is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException)
                    {
                        deferred++;
                        log?.Invoke(
                            $"update storage cleanup deferred staging attempt={Path.GetFileName(attempt)} type={ex.GetType().Name}");
                    }
                }
            }
        }

        var transactionsRoot = Path.Combine(fullRoot, "transactions");
        if (Directory.Exists(transactionsRoot))
        {
            UpdatePathSafety.EnsureExistingComponentsNotReparse(
                fullRoot,
                transactionsRoot);
            foreach (var transaction in Directory.EnumerateDirectories(
                         transactionsRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!TryParseTransactionBuild(
                            Path.GetFileName(transaction),
                            out var transactionBuild))
                    {
                        deferred++;
                        continue;
                    }

                    var journalPath = Path.Combine(
                        transaction,
                        "journal.json");
                    if (!File.Exists(journalPath))
                    {
                        deferred++;
                        continue;
                    }

                    UpdatePathSafety.EnsureExistingComponentsNotReparse(
                        fullRoot,
                        journalPath);
                    var journal = JsonSerializer.Deserialize<UpdateJournal>(
                                      await File.ReadAllTextAsync(
                                          journalPath,
                                          ct),
                                      UpdateProtocol.Json)
                                  ?? throw new InvalidDataException(
                                      "Updater recovery journal is empty.");
                    if (journal.TargetBuildNumber != transactionBuild
                        || journal.Phase is not (
                            UpdateJournalPhase.Confirmed
                                or UpdateJournalPhase.RolledBack))
                    {
                        deferred++;
                        continue;
                    }

                    var newestWriteUtc = InspectSafeTreeNewestWriteUtc(
                        fullRoot,
                        transaction,
                        ct);
                    if (!IsOldEnough(
                            newestWriteUtc,
                            nowUtc,
                            minimumAge))
                    {
                        deferred++;
                        continue;
                    }

                    DeleteSafeTree(fullRoot, transaction, ct);
                    deletedTransactions++;
                    log?.Invoke(
                        $"update storage cleanup removed terminal transaction={Path.GetFileName(transaction)} phase={journal.Phase}");
                }
                catch (Exception ex) when (
                    ex is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or JsonException)
                {
                    deferred++;
                    log?.Invoke(
                        $"update storage cleanup deferred transaction={Path.GetFileName(transaction)} type={ex.GetType().Name}");
                }
            }
        }

        if (deletedStaging > 0 || deletedTransactions > 0)
            log?.Invoke(
                $"update storage cleanup completed staging={deletedStaging} transactions={deletedTransactions} deferred={deferred}");

        return new UpdateStorageCleanupResult(
            deletedStaging,
            deletedTransactions,
            deferred);
    }

    public static bool DeleteConfirmedStagingBestEffort(
        string stagingRoot,
        long buildNumber,
        Action<string>? log = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"build={buildNumber}; staging={stagingRoot}");
        try
        {
            var updaterRoot = NormalizeDirectory(
                UpdatePackageStager.GetUpdaterRoot());
            UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
            var attemptRoot = GetCanonicalStagingAttemptRoot(
                updaterRoot,
                stagingRoot,
                buildNumber);
            _ = InspectSafeTreeNewestWriteUtc(
                updaterRoot,
                attemptRoot,
                CancellationToken.None);
            DeleteSafeTree(
                updaterRoot,
                attemptRoot,
                CancellationToken.None);
            log?.Invoke(
                $"update confirmed staging cleanup removed attempt={Path.GetFileName(attemptRoot)}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke(
                $"update confirmed staging cleanup deferred type={ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void EnsureCleanupRootAllowed(string updaterRoot)
    {
        var canonicalRoot = NormalizeDirectory(
            UpdatePackageStager.GetUpdaterRoot());
        UpdatePackageStager.EnsureUpdaterRoot(canonicalRoot);
        var relative = Path.GetRelativePath(
            canonicalRoot,
            updaterRoot);
        if (Path.IsPathRooted(relative)
            || relative.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Updater cleanup root must be the canonical updater root or one of its descendants.");

        if (!Directory.Exists(updaterRoot))
            UpdatePathSafety.CreateDirectorySafely(
                canonicalRoot,
                updaterRoot);
        UpdatePathSafety.EnsureExistingComponentsNotReparse(
            canonicalRoot,
            updaterRoot);
    }

    private static string GetCanonicalStagingAttemptRoot(
        string updaterRoot,
        string stagingRoot,
        long buildNumber)
    {
        var fullStaging = NormalizeDirectory(stagingRoot);
        var trimmedInput = stagingRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!Path.IsPathFullyQualified(stagingRoot)
            || !string.Equals(
                trimmedInput,
                fullStaging,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Updater staging root is not a canonical absolute path.");

        var relative = Path.GetRelativePath(
                updaterRoot,
                fullStaging)
            .Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3
            || !string.Equals(
                segments[0],
                "staging",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                segments[2],
                "payload",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Updater staging root does not match the canonical staging topology.");

        var attemptParts = segments[1].Split(
            '-',
            StringSplitOptions.None);
        if (attemptParts.Length != 2
            || !long.TryParse(
                attemptParts[0],
                out var stagedBuild)
            || stagedBuild != buildNumber
            || !Guid.TryParseExact(
                attemptParts[1],
                "N",
                out _))
            throw new InvalidDataException(
                "Updater staging identity is malformed or belongs to another build.");

        return Path.Combine(
            updaterRoot,
            "staging",
            segments[1]);
    }

    private static bool TryParseStagingAttemptName(
        string attemptName,
        out long buildNumber)
    {
        buildNumber = 0;
        var parts = attemptName.Split(
            '-',
            StringSplitOptions.None);
        return parts.Length == 2
               && long.TryParse(
                   parts[0],
                   out buildNumber)
               && Guid.TryParseExact(
                   parts[1],
                   "N",
                   out _);
    }

    private static bool TryParseTransactionBuild(
        string transactionName,
        out long buildNumber)
    {
        buildNumber = 0;
        var parts = transactionName.Split(
            '-',
            StringSplitOptions.None);
        if (parts.Length == 2)
        {
            return long.TryParse(
                       parts[0],
                       out buildNumber)
                   && Guid.TryParseExact(
                       parts[1],
                       "N",
                       out _);
        }

        return parts.Length == 3
               && long.TryParse(
                   parts[0],
                   out buildNumber)
               && parts[1].Length == 24
               && parts[1].All(Uri.IsHexDigit)
               && Guid.TryParseExact(
                   parts[2],
                   "N",
                   out _);
    }

    private static DateTimeOffset InspectSafeTreeNewestWriteUtc(
        string updaterRoot,
        string directory,
        CancellationToken ct)
    {
        UpdatePathSafety.EnsureExistingComponentsNotReparse(
            updaterRoot,
            directory);
        var rootAttributes = File.GetAttributes(directory);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(
                $"Updater cleanup refuses reparse directory: {directory}");

        var newest = new DateTimeOffset(
            Directory.GetLastWriteTimeUtc(directory),
            TimeSpan.Zero);
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         current,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(
                        $"Updater cleanup refuses reparse entry: {entry}");

                var writeUtc = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(entry),
                    TimeSpan.Zero);
                if (writeUtc > newest)
                    newest = writeUtc;
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
            }
        }

        return newest;
    }

    private static void DeleteSafeTree(
        string updaterRoot,
        string directory,
        CancellationToken ct)
    {
        // First validate the whole tree so a discovered reparse point cannot
        // cause a partial recursive cleanup.
        _ = InspectSafeTreeNewestWriteUtc(
            updaterRoot,
            directory,
            ct);

        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            directories.Add(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         current,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(
                        $"Updater cleanup refuses reparse entry: {entry}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                File.Delete(entry);
            }
        }

        for (var i = directories.Count - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(directories[i]);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(
                    $"Updater cleanup refuses reparse directory: {directories[i]}");
            Directory.Delete(directories[i], recursive: false);
        }
    }

    private static bool IsOldEnough(
        DateTimeOffset newestWriteUtc,
        DateTimeOffset nowUtc,
        TimeSpan minimumAge) =>
        newestWriteUtc <= nowUtc - minimumAge;

    private static bool SamePath(
        string left,
        string? right) =>
        right is not null
        && string.Equals(
            NormalizeDirectory(left),
            NormalizeDirectory(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
}
