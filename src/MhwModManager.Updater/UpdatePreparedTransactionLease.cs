using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public enum UpdatePreparedTransactionOwnerState
{
    Active,
    Exited,
    Unknown
}

public sealed record UpdatePreparedTransactionLease(
    int SchemaVersion,
    long BuildNumber,
    string TransactionId,
    int OwnerProcessId,
    DateTimeOffset? OwnerProcessStartUtc,
    DateTimeOffset UpdatedUtc)
{
    public const int CurrentSchemaVersion = 1;

    public void ValidateFor(
        string transactionRoot,
        long expectedBuildNumber)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"transaction={transactionRoot}; build={expectedBuildNumber}");
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported updater prepared-transaction lease schema {SchemaVersion}.");
        if (BuildNumber != expectedBuildNumber)
            throw new InvalidDataException(
                "Updater prepared-transaction lease build does not match its transaction directory.");
        if (OwnerProcessId <= 0)
            throw new InvalidDataException(
                "Updater prepared-transaction lease process id must be positive.");
        if (UpdatedUtc == default)
            throw new InvalidDataException(
                "Updater prepared-transaction lease timestamp is missing.");

        var expectedTransactionId = Path.GetFileName(
            Path.GetFullPath(transactionRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
        if (!string.Equals(
                TransactionId,
                expectedTransactionId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Updater prepared-transaction lease identity does not match its transaction directory.");
    }
}

public static class UpdatePreparedTransactionLeaseStore
{
    public const string FileName = "prepared-lease.json";

    public static async Task WriteAsync(
        string transactionRoot,
        long buildNumber,
        int ownerProcessId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"transaction={transactionRoot}; build={buildNumber}; pid={ownerProcessId}");
        if (ownerProcessId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(ownerProcessId),
                "Updater prepared-transaction lease process id must be positive.");

        var fullTransactionRoot = Path.GetFullPath(transactionRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(fullTransactionRoot))
            throw new DirectoryNotFoundException(
                $"Updater transaction root is missing: {fullTransactionRoot}");

        var lease = new UpdatePreparedTransactionLease(
            UpdatePreparedTransactionLease.CurrentSchemaVersion,
            buildNumber,
            Path.GetFileName(fullTransactionRoot),
            ownerProcessId,
            TryGetProcessStartUtc(ownerProcessId),
            DateTimeOffset.UtcNow);
        lease.ValidateFor(
            fullTransactionRoot,
            buildNumber);

        await UpdatePackageStager.WriteJsonAtomicallyAsync(
            Path.Combine(fullTransactionRoot, FileName),
            lease,
            ct);
    }

    public static async Task<UpdatePreparedTransactionLease> ReadAsync(
        string path,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        return JsonSerializer.Deserialize<UpdatePreparedTransactionLease>(
                   await File.ReadAllTextAsync(path, ct),
                   UpdateProtocol.Json)
               ?? throw new InvalidDataException(
                   "Updater prepared-transaction lease is empty.");
    }

    public static UpdatePreparedTransactionOwnerState GetOwnerState(
        UpdatePreparedTransactionLease lease)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"pid={lease.OwnerProcessId}");
        if (lease.OwnerProcessStartUtc is null)
            return UpdatePreparedTransactionOwnerState.Unknown;

        try
        {
            using var process = Process.GetProcessById(
                lease.OwnerProcessId);
            var actualStartUtc = new DateTimeOffset(
                process.StartTime.ToUniversalTime());
            return actualStartUtc == lease.OwnerProcessStartUtc.Value
                ? UpdatePreparedTransactionOwnerState.Active
                : UpdatePreparedTransactionOwnerState.Exited;
        }
        catch (ArgumentException)
        {
            return UpdatePreparedTransactionOwnerState.Exited;
        }
        catch (InvalidOperationException)
        {
            return UpdatePreparedTransactionOwnerState.Exited;
        }
        catch (Win32Exception)
        {
            return UpdatePreparedTransactionOwnerState.Unknown;
        }
        catch (NotSupportedException)
        {
            return UpdatePreparedTransactionOwnerState.Unknown;
        }
    }

    private static DateTimeOffset? TryGetProcessStartUtc(
        int processId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"pid={processId}");
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
}
