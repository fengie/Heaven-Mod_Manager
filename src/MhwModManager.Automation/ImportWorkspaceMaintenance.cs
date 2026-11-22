using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Automation;

public sealed record ImportWorkspaceLease(
    int SchemaVersion,
    string StagingName,
    string WorkspaceRoot,
    int OwnerProcessId,
    DateTimeOffset? OwnerProcessStartUtc,
    DateTimeOffset CreatedUtc)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ImportWorkspaceCleanupResult(
    int DeletedWorkspaces,
    int DeletedLeases,
    int DeferredEntries,
    long ReclaimedBytes);

/// <summary>
/// Reclaims only certified, exited-owner import scratch. Legacy/unleased directories,
/// active or uncertain owners, and reparse trees remain untouched.
/// </summary>
public static class ImportWorkspaceMaintenance
{
    public const string LeaseSuffix = ".lease.json";
    public const long DefaultMaxOwnedBytes = 512L * 1024 * 1024;
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);

    private static readonly string[] Kinds = ["archive", "fomod-source", "fomod-install", "inbox"];

    public static string Reserve(string modsRoot, string kind)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Kinds.Contains(kind, StringComparer.Ordinal))
            throw new ArgumentException("Unknown import workspace kind.", nameof(kind));

        var root = ImportPublicationWorkspace.RootFor(modsRoot);
        var stagingName = kind + "-" + Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, stagingName);
        var leasePath = staging + LeaseSuffix;
        if (Directory.Exists(staging) || File.Exists(staging))
            throw new IOException("An import workspace already exists under this identity.");

        DateTimeOffset processStartUtc;
        try
        {
            using var process = Process.GetCurrentProcess();
            processStartUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            throw new IOException("Cannot certify the import process identity before extraction.", ex);
        }

        var lease = new ImportWorkspaceLease(
            ImportWorkspaceLease.CurrentSchemaVersion, stagingName, root,
            Environment.ProcessId, processStartUtc, DateTimeOffset.UtcNow);
        // The sidecar is outside the extracted payload and is durably written
        // before any bytes can enter staging. Publish never copies it into Mods.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(lease);
        using (var stream = new FileStream(leasePath, FileMode.CreateNew, FileAccess.Write,
                   FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        return staging;
    }

    public static void ReleaseOwnLease(string workspaceRoot, string staging)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var fullStage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging));
        if (!string.Equals(Path.GetDirectoryName(fullStage), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Import workspace lease release escaped its root.");

        ImportPublicationWorkspace.RejectReparseAncestors(root);
        var leasePath = fullStage + LeaseSuffix;
        if (!File.Exists(leasePath)) return;

        var lease = ReadValidLease(root, leasePath);
        if (lease.OwnerProcessId != Environment.ProcessId
            || OwnerState(lease) != ImportOwnerState.Active)
            throw new InvalidDataException("Import workspace lease is not held by this process.");
        ImportPublicationWorkspace.RejectReparseAncestors(leasePath);
        File.Delete(leasePath);
    }

    public static ImportWorkspaceCleanupResult Run(
        string modsRoot, Action<string>? log, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return CleanupRoot(modsRoot, DateTimeOffset.UtcNow, DefaultMinimumAge,
            DefaultMaxOwnedBytes, log, ct);
    }

    public static ImportWorkspaceCleanupResult CleanupRoot(
        string modsRoot, DateTimeOffset nowUtc, TimeSpan minimumAge,
        long maxOwnedBytes, Action<string>? log, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumAge, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maxOwnedBytes);

        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(ImportPublicationWorkspace.LocationFor(modsRoot)));
        ImportPublicationWorkspace.RejectReparseAncestors(root);
        if (!Directory.Exists(root)) return new(0, 0, 0, 0);

        var candidates = new List<CleanupCandidate>();
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deferred = 0;
        long ownedBytes = 0;

        foreach (var leasePath in Directory.EnumerateFiles(root, "*" + LeaseSuffix,
                     SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lease = ReadValidLease(root, leasePath);
                var staging = Path.Combine(root, lease.StagingName);
                observed.Add(staging);
                if (OwnerState(lease) != ImportOwnerState.Exited)
                {
                    deferred++;
                    continue;
                }

                var inspection = InspectLeaseAndWork(root, leasePath, staging, lease, ct);
                ownedBytes = checked(ownedBytes + inspection.Bytes);
                candidates.Add(new CleanupCandidate(staging, leasePath, lease,
                    inspection.Bytes, inspection.NewestUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or JsonException or OverflowException)
            {
                deferred++;
                log?.Invoke("import cleanup preserved unreadable or unsafe lease="
                    + Path.GetFileName(leasePath) + " type=" + ex.GetType().Name);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            if (!observed.Contains(directory)) deferred++;
        }

        var deletedWorkspaces = 0;
        var deletedLeases = 0;
        long reclaimedBytes = 0;

        foreach (var candidate in candidates.OrderBy(c => c.NewestUtc)
                     .ThenBy(c => c.LeasePath, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (nowUtc - candidate.NewestUtc < minimumAge && ownedBytes <= maxOwnedBytes)
            {
                deferred++;
                continue;
            }

            try
            {
                // Revalidate the lease and the entire tree immediately before unlink.
                // Never delete a workspace whose owner has returned, changed or is unknown.
                var current = ReadValidLease(root, candidate.LeasePath);
                if (current != candidate.Lease || OwnerState(current) != ImportOwnerState.Exited)
                {
                    deferred++;
                    continue;
                }
                var inspection = InspectLeaseAndWork(root, candidate.LeasePath,
                    candidate.StagingPath, current, ct);
                if (inspection.Bytes != candidate.Bytes || inspection.NewestUtc > candidate.NewestUtc)
                {
                    deferred++;
                    continue;
                }

                if (Directory.Exists(candidate.StagingPath))
                {
                    ImportPublicationWorkspace.RejectReparseAncestors(candidate.StagingPath);
                    SafeRecursiveTraversal.EnsureNoReparsePoints(candidate.StagingPath, ct);
                    Directory.Delete(candidate.StagingPath, recursive: true);
                    deletedWorkspaces++;
                }
                ImportPublicationWorkspace.RejectReparseAncestors(candidate.LeasePath);
                File.Delete(candidate.LeasePath);
                deletedLeases++;
                reclaimedBytes = checked(reclaimedBytes + inspection.Bytes);
                ownedBytes = Math.Max(0, ownedBytes - candidate.Bytes);
                log?.Invoke("import cleanup reclaimed workspace="
                    + Path.GetFileName(candidate.StagingPath) + " bytes=" + inspection.Bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or JsonException or OverflowException)
            {
                deferred++;
                log?.Invoke("import cleanup deferred workspace="
                    + Path.GetFileName(candidate.StagingPath) + " type=" + ex.GetType().Name);
            }
        }

        return new(deletedWorkspaces, deletedLeases, deferred, reclaimedBytes);
    }

    private static ImportWorkspaceLease ReadValidLease(string root, string leasePath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ImportPublicationWorkspace.RejectReparseAncestors(leasePath);
        var metadata = new FileInfo(leasePath);
        if (!metadata.Exists || metadata.Length is <= 0 or > 4096)
            throw new InvalidDataException("Import workspace lease size is invalid.");

        var lease = JsonSerializer.Deserialize<ImportWorkspaceLease>(
                        File.ReadAllText(leasePath))
                    ?? throw new InvalidDataException("Import workspace lease is empty.");
        if (lease.SchemaVersion != ImportWorkspaceLease.CurrentSchemaVersion
            || !string.Equals(lease.WorkspaceRoot, root, StringComparison.OrdinalIgnoreCase)
            || !IsOwnedStagingName(lease.StagingName)
            || !string.Equals(Path.GetFileName(leasePath),
                lease.StagingName + LeaseSuffix, StringComparison.Ordinal)
            || lease.OwnerProcessId <= 0
            || lease.CreatedUtc == default)
            throw new InvalidDataException("Import workspace lease identity failed validation.");
        return lease;
    }

    private static bool IsOwnedStagingName(string stagingName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(stagingName)) return false;
        return Kinds.Any(kind =>
        {
            var prefix = kind + "-";
            return stagingName.StartsWith(prefix, StringComparison.Ordinal)
                   && Guid.TryParseExact(stagingName[prefix.Length..], "N", out _);
        });
    }

    private static (long Bytes, DateTimeOffset NewestUtc) InspectLeaseAndWork(
        string root, string leasePath, string staging,
        ImportWorkspaceLease lease, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ImportPublicationWorkspace.RejectReparseAncestors(root);
        ImportPublicationWorkspace.RejectReparseAncestors(leasePath);
        var leaseInfo = new FileInfo(leasePath);
        var bytes = leaseInfo.Length;
        var newest = MaxUtc(lease.CreatedUtc, leaseInfo.LastWriteTimeUtc);
        if (Directory.Exists(staging))
        {
            ImportPublicationWorkspace.RejectReparseAncestors(staging);
            var snapshot = SafeRecursiveTraversal.Snapshot(staging, ct);
            newest = MaxUtc(newest, Directory.GetLastWriteTimeUtc(staging));
            foreach (var directory in snapshot.Directories)
            {
                ct.ThrowIfCancellationRequested();
                newest = MaxUtc(newest, Directory.GetLastWriteTimeUtc(directory));
            }
            foreach (var path in snapshot.Files)
            {
                ct.ThrowIfCancellationRequested();
                var file = new FileInfo(path);
                bytes = checked(bytes + file.Length);
                newest = MaxUtc(newest, file.LastWriteTimeUtc);
            }
        }
        return (bytes, newest);
    }

    private static DateTimeOffset MaxUtc(DateTimeOffset first, DateTime second)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var secondUtc = new DateTimeOffset(DateTime.SpecifyKind(second, DateTimeKind.Utc));
        return first >= secondUtc ? first : secondUtc;
    }

    private static ImportOwnerState OwnerState(ImportWorkspaceLease lease)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (lease.OwnerProcessStartUtc is null) return ImportOwnerState.Unknown;
        try
        {
            using var process = Process.GetProcessById(lease.OwnerProcessId);
            return new DateTimeOffset(process.StartTime.ToUniversalTime())
                   == lease.OwnerProcessStartUtc.Value
                ? ImportOwnerState.Active
                : ImportOwnerState.Exited;
        }
        catch (ArgumentException) { return ImportOwnerState.Exited; }
        catch (InvalidOperationException) { return ImportOwnerState.Exited; }
        catch (Win32Exception) { return ImportOwnerState.Unknown; }
        catch (NotSupportedException) { return ImportOwnerState.Unknown; }
    }

    private enum ImportOwnerState { Active, Exited, Unknown }

    private sealed record CleanupCandidate(
        string StagingPath, string LeasePath, ImportWorkspaceLease Lease,
        long Bytes, DateTimeOffset NewestUtc);
}
