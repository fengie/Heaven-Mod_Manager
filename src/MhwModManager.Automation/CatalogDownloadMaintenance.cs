using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Automation;

public enum CatalogDownloadOwnerState
{
    Active,
    Exited,
    Unknown
}

public sealed record CatalogDownloadLease(
    int SchemaVersion,
    string FileName,
    int OwnerProcessId,
    DateTimeOffset? OwnerProcessStartUtc,
    DateTimeOffset CreatedUtc)
{
    public const int CurrentSchemaVersion = 1;

    public void ValidateFor(
        string downloadRoot,
        string archivePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"root={downloadRoot}; archive={archivePath}");
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported catalog download lease schema {SchemaVersion}.");
        if (OwnerProcessId <= 0)
            throw new InvalidDataException(
                "Catalog download lease process id must be positive.");
        if (CreatedUtc == default)
            throw new InvalidDataException(
                "Catalog download lease timestamp is missing.");

        var fullRoot = CatalogDownloadMaintenance.NormalizeRoot(downloadRoot);
        var fullArchive = Path.GetFullPath(archivePath);
        if (!string.Equals(
                Path.GetDirectoryName(fullArchive),
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Catalog download lease archive is outside its owned root.");
        if (!CatalogDownloadMaintenance.IsOwnedArchiveName(
                Path.GetFileName(fullArchive)))
            throw new InvalidDataException(
                "Catalog download lease archive name is not manager-owned.");
        if (!string.Equals(
                FileName,
                Path.GetFileName(fullArchive),
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Catalog download lease identity does not match its archive.");
    }
}

public sealed record CatalogDownloadCleanupResult(
    int DeletedDownloads,
    int DeletedLeases,
    int DeferredEntries,
    long ReclaimedBytes);

public static class CatalogDownloadMaintenance
{
    public const long DefaultMaxOwnedBytes = 512L * 1024L * 1024L;
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);

    private const string ArchivePrefix = "catalog-";
    private const string LeaseSuffix = ".lease.json";
    private const string TemporaryLeaseMarker = LeaseSuffix + ".tmp-";

    public static Task<CatalogDownloadCleanupResult> RunAsync(
        string downloadRoot,
        Action<string>? log,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={downloadRoot}");
        return CleanupRootAsync(
            downloadRoot,
            DateTimeOffset.UtcNow,
            DefaultMinimumAge,
            DefaultMaxOwnedBytes,
            log,
            ct);
    }

    public static async Task<string> CreateLeaseAsync(
        string downloadRoot,
        string archivePath,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"root={downloadRoot}; archive={archivePath}");
        var fullRoot = NormalizeRoot(downloadRoot);
        EnsureSafeRoot(fullRoot);

        var fullArchive = Path.GetFullPath(archivePath);
        var ownerProcessStartUtc = TryGetProcessStartUtc(Environment.ProcessId)
            ?? throw new IOException(
                "Could not certify the catalog download owner process identity; refusing to create reclaimable scratch.");
        var lease = new CatalogDownloadLease(
            CatalogDownloadLease.CurrentSchemaVersion,
            Path.GetFileName(fullArchive),
            Environment.ProcessId,
            ownerProcessStartUtc,
            DateTimeOffset.UtcNow);
        lease.ValidateFor(fullRoot, fullArchive);

        if (File.Exists(fullArchive))
            throw new IOException(
                $"Catalog download archive already exists before lease publication: {fullArchive}");

        var leasePath = fullArchive + LeaseSuffix;
        if (File.Exists(leasePath))
            throw new IOException(
                $"Catalog download lease already exists: {leasePath}");

        var temporaryLease = leasePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(lease);
            await using (var output = new FileStream(
                             temporaryLease,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await output.WriteAsync(payload, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryLease, leasePath, overwrite: false);
            return leasePath;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryLease))
                    File.Delete(temporaryLease);
            }
            catch
            {
            }
        }
    }

    public static void DeleteLeaseBestEffort(
        string leasePath,
        Action<string>? log = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"lease={leasePath}");
        try
        {
            if (File.Exists(leasePath))
                File.Delete(leasePath);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke(
                $"catalog download lease cleanup deferred type={ex.GetType().Name}: {ex.Message}");
        }
    }

    public static async Task<CatalogDownloadCleanupResult> CleanupRootAsync(
        string downloadRoot,
        DateTimeOffset nowUtc,
        TimeSpan minimumAge,
        long maxOwnedBytes,
        Action<string>? log,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"root={downloadRoot}; max={maxOwnedBytes}");
        if (minimumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(minimumAge),
                "Catalog download cleanup minimum age may not be negative.");
        if (maxOwnedBytes < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxOwnedBytes),
                "Catalog download cleanup byte quota may not be negative.");

        var fullRoot = NormalizeRoot(downloadRoot);
        EnsureSafeRoot(fullRoot);

        var candidates = new List<CleanupCandidate>();
        var deferred = 0;
        var leasedArchives = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var temporaryLeasePath in Directory.EnumerateFiles(
                     fullRoot,
                     ArchivePrefix + "*" + TemporaryLeaseMarker + "*",
                     SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                RejectReparsePoint(temporaryLeasePath);
                if (!TryGetArchivePathFromTemporaryLease(
                        fullRoot,
                        temporaryLeasePath,
                        out var archivePath))
                {
                    deferred++;
                    continue;
                }

                var lease = JsonSerializer.Deserialize<CatalogDownloadLease>(
                                await File.ReadAllTextAsync(temporaryLeasePath, ct)
                                    .ConfigureAwait(false))
                            ?? throw new InvalidDataException(
                                "Catalog download temporary lease is empty.");
                lease.ValidateFor(fullRoot, archivePath);

                var ownerState = GetOwnerState(lease);
                if (ownerState is not CatalogDownloadOwnerState.Exited)
                {
                    deferred++;
                    log?.Invoke(
                        $"catalog download cleanup preserved temporary lease={Path.GetFileName(temporaryLeasePath)} owner={ownerState}");
                    continue;
                }

                var temporaryLeaseBytes = new FileInfo(temporaryLeasePath).Length;
                var newestWriteUtc = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(temporaryLeasePath),
                    TimeSpan.Zero);
                if (lease.CreatedUtc > newestWriteUtc)
                    newestWriteUtc = lease.CreatedUtc;

                candidates.Add(
                    new CleanupCandidate(
                        ArchivePath: null,
                        LeasePath: temporaryLeasePath,
                        NewestWriteUtc: newestWriteUtc,
                        Bytes: temporaryLeaseBytes));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException)
            {
                deferred++;
                log?.Invoke(
                    $"catalog download cleanup deferred temporary-lease={Path.GetFileName(temporaryLeasePath)} type={ex.GetType().Name}");
            }
        }

        foreach (var leasePath in Directory.EnumerateFiles(
                     fullRoot,
                     ArchivePrefix + "*" + LeaseSuffix,
                     SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                RejectReparsePoint(leasePath);
                var archivePath = leasePath[..^LeaseSuffix.Length];
                if (!IsOwnedArchiveName(Path.GetFileName(archivePath)))
                {
                    deferred++;
                    continue;
                }

                var lease = JsonSerializer.Deserialize<CatalogDownloadLease>(
                                await File.ReadAllTextAsync(leasePath, ct)
                                    .ConfigureAwait(false))
                            ?? throw new InvalidDataException(
                                "Catalog download lease is empty.");
                lease.ValidateFor(fullRoot, archivePath);
                leasedArchives.Add(archivePath);

                var ownerState = GetOwnerState(lease);
                if (ownerState is not CatalogDownloadOwnerState.Exited)
                {
                    deferred++;
                    log?.Invoke(
                        $"catalog download cleanup preserved archive={lease.FileName} owner={ownerState}");
                    continue;
                }

                long bytes = new FileInfo(leasePath).Length;
                var newestWriteUtc = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(leasePath),
                    TimeSpan.Zero);
                if (File.Exists(archivePath))
                {
                    RejectReparsePoint(archivePath);
                    var archive = new FileInfo(archivePath);
                    bytes = checked(bytes + archive.Length);
                    var archiveWriteUtc = new DateTimeOffset(
                        archive.LastWriteTimeUtc,
                        TimeSpan.Zero);
                    if (archiveWriteUtc > newestWriteUtc)
                        newestWriteUtc = archiveWriteUtc;
                }

                candidates.Add(
                    new CleanupCandidate(
                        archivePath,
                        leasePath,
                        newestWriteUtc,
                        bytes));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JsonException)
            {
                deferred++;
                log?.Invoke(
                    $"catalog download cleanup deferred lease={Path.GetFileName(leasePath)} type={ex.GetType().Name}");
            }
        }

        foreach (var archivePath in Directory.EnumerateFiles(
                     fullRoot,
                     ArchivePrefix + "*",
                     SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            if (archivePath.EndsWith(
                    LeaseSuffix,
                    StringComparison.OrdinalIgnoreCase)
                || archivePath.Contains(
                    TemporaryLeaseMarker,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsOwnedArchiveName(Path.GetFileName(archivePath)))
                continue;
            if (!leasedArchives.Contains(archivePath))
                deferred++;
        }

        var remainingOwnedBytes = candidates.Sum(candidate => candidate.Bytes);
        var deletedDownloads = 0;
        var deletedLeases = 0;
        long reclaimedBytes = 0;

        foreach (var candidate in candidates
                     .OrderBy(candidate => candidate.NewestWriteUtc)
                     .ThenBy(candidate => candidate.ArchivePath ?? candidate.LeasePath, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var oldEnough =
                nowUtc - candidate.NewestWriteUtc >= minimumAge;
            var overQuota = remainingOwnedBytes > maxOwnedBytes;
            if (!oldEnough && !overQuota)
            {
                deferred++;
                continue;
            }

            try
            {
                if (candidate.ArchivePath is not null
                    && File.Exists(candidate.ArchivePath))
                {
                    RejectReparsePoint(candidate.ArchivePath);
                    var archiveBytes = new FileInfo(candidate.ArchivePath).Length;
                    File.Delete(candidate.ArchivePath);
                    reclaimedBytes = checked(reclaimedBytes + archiveBytes);
                    deletedDownloads++;
                }

                if (File.Exists(candidate.LeasePath))
                {
                    RejectReparsePoint(candidate.LeasePath);
                    var leaseBytes = new FileInfo(candidate.LeasePath).Length;
                    File.Delete(candidate.LeasePath);
                    reclaimedBytes = checked(reclaimedBytes + leaseBytes);
                    deletedLeases++;
                }

                remainingOwnedBytes = Math.Max(
                    0,
                    remainingOwnedBytes - candidate.Bytes);
                var candidateName = Path.GetFileName(
                    candidate.ArchivePath ?? candidate.LeasePath);
                log?.Invoke(
                    $"catalog download cleanup reclaimed entry={candidateName} bytes={candidate.Bytes}");
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                deferred++;
                var candidateName = Path.GetFileName(
                    candidate.ArchivePath ?? candidate.LeasePath);
                log?.Invoke(
                    $"catalog download cleanup deferred entry={candidateName} type={ex.GetType().Name}");
            }
        }

        return new CatalogDownloadCleanupResult(
            deletedDownloads,
            deletedLeases,
            deferred,
            reclaimedBytes);
    }

    internal static string NormalizeRoot(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static bool IsOwnedArchiveName(string fileName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"file={fileName}");
        if (!fileName.StartsWith(
                ArchivePrefix,
                StringComparison.Ordinal)
            || fileName.Length <= ArchivePrefix.Length + 33)
            return false;

        var guidStart = ArchivePrefix.Length;
        if (!Guid.TryParseExact(
                fileName.Substring(guidStart, 32),
                "N",
                out _))
            return false;
        if (fileName[guidStart + 32] != '.')
            return false;

        var extension = fileName[(guidStart + 33)..];
        return extension.Length is >= 1 and <= 11
               && extension.All(char.IsLetterOrDigit);
    }

    private static bool TryGetArchivePathFromTemporaryLease(
        string fullRoot,
        string temporaryLeasePath,
        out string archivePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"root={fullRoot}; temporaryLease={temporaryLeasePath}");
        archivePath = string.Empty;

        var fileName = Path.GetFileName(temporaryLeasePath);
        var markerIndex = fileName.IndexOf(
            TemporaryLeaseMarker,
            StringComparison.Ordinal);
        if (markerIndex <= 0)
            return false;

        var temporaryId = fileName[(markerIndex + TemporaryLeaseMarker.Length)..];
        if (!Guid.TryParseExact(temporaryId, "N", out _))
            return false;

        var archiveName = fileName[..markerIndex];
        if (!IsOwnedArchiveName(archiveName))
            return false;

        archivePath = Path.Combine(fullRoot, archiveName);
        return true;
    }

    private static void EnsureSafeRoot(string fullRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={fullRoot}");
        // Validate the nearest existing ancestor before creating anything so a
        // configured junction cannot redirect root creation into an external
        // tree. Create missing components one at a time and re-check each one.
        var missing = new Stack<string>();
        var current = new DirectoryInfo(fullRoot);
        while (!current.Exists)
        {
            missing.Push(current.FullName);
            current = current.Parent
                ?? throw new IOException(
                    $"Catalog download root has no existing filesystem ancestor: {fullRoot}");
        }

        EnsureDirectoryAncestryNotReparse(current);

        while (missing.Count > 0)
        {
            var next = missing.Pop();
            Directory.CreateDirectory(next);
            var created = new DirectoryInfo(next);
            if ((created.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(
                    $"Catalog download cleanup refuses reparse directory component: {created.FullName}");
        }

        EnsureDirectoryAncestryNotReparse(new DirectoryInfo(fullRoot));
    }

    private static void EnsureDirectoryAncestryNotReparse(
        DirectoryInfo directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"directory={directory.FullName}");
        for (var current = directory;
             current is not null;
             current = current.Parent)
        {
            if (!current.Exists)
                continue;
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(
                    $"Catalog download cleanup refuses reparse directory component: {current.FullName}");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(
                $"Catalog download cleanup refuses reparse path: {path}");
    }

    private static CatalogDownloadOwnerState GetOwnerState(
        CatalogDownloadLease lease)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={lease.OwnerProcessId}");
        if (lease.OwnerProcessStartUtc is null)
            return CatalogDownloadOwnerState.Unknown;

        try
        {
            using var process = Process.GetProcessById(lease.OwnerProcessId);
            var actualStartUtc = new DateTimeOffset(
                process.StartTime.ToUniversalTime());
            return actualStartUtc == lease.OwnerProcessStartUtc.Value
                ? CatalogDownloadOwnerState.Active
                : CatalogDownloadOwnerState.Exited;
        }
        catch (ArgumentException)
        {
            return CatalogDownloadOwnerState.Exited;
        }
        catch (InvalidOperationException)
        {
            return CatalogDownloadOwnerState.Exited;
        }
        catch (Win32Exception)
        {
            return CatalogDownloadOwnerState.Unknown;
        }
        catch (NotSupportedException)
        {
            return CatalogDownloadOwnerState.Unknown;
        }
    }

    private static DateTimeOffset? TryGetProcessStartUtc(int processId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"pid={processId}");
        try
        {
            using var process = Process.GetProcessById(processId);
            return new DateTimeOffset(
                process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record CleanupCandidate(
        string? ArchivePath,
        string LeasePath,
        DateTimeOffset NewestWriteUtc,
        long Bytes);
}
