using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

[Collection(UpdaterGlobalStateFixture.Name)]
public sealed class UpdaterStorageMaintenanceTests : IDisposable
{
    private readonly string root = Path.Combine(
        UpdatePackageStager.GetUpdaterRoot(),
        "tests",
        "storage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Cleanup_preserves_pending_and_recovery_state_but_removes_old_orphans_and_terminal_transactions()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var protectedPayload = CreateStagingAttempt(102, old, "protected");
        var orphanPayload = CreateStagingAttempt(101, old, "orphan");

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, UpdateProtocol.PendingFileName),
            JsonSerializer.Serialize(
                new StagedUpdate(
                    Manifest(102),
                    protectedPayload,
                    Path.Combine(
                        protectedPayload,
                        UpdateProtocol.ProductManifestFileName)),
                UpdateProtocol.Json),
            TestContext.Current.CancellationToken);

        var terminal = CreateTransaction(
            100,
            UpdateJournalPhase.Confirmed,
            old,
            legacyName: true);
        var recovery = CreateTransaction(
            99,
            UpdateJournalPhase.RollbackRequired,
            old,
            legacyName: false);

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(Path.GetDirectoryName(protectedPayload)!));
        Assert.False(Directory.Exists(Path.GetDirectoryName(orphanPayload)!));
        Assert.False(Directory.Exists(terminal));
        Assert.True(Directory.Exists(recovery));
        Assert.Equal(1, result.DeletedStagingAttempts);
        Assert.Equal(1, result.DeletedTransactions);
    }

    [Fact]
    public async Task Malformed_pending_state_fails_closed_for_staging_cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var orphanPayload = CreateStagingAttempt(103, old, "orphan");

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, UpdateProtocol.PendingFileName),
            "{ definitely-not-json",
            TestContext.Current.CancellationToken);

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(Path.GetDirectoryName(orphanPayload)!));
        Assert.Equal(0, result.DeletedStagingAttempts);
    }

    [Fact]
    public async Task Recent_orphan_is_preserved_by_cleanup_grace_period()
    {
        var now = DateTimeOffset.UtcNow;
        var recentPayload = CreateStagingAttempt(
            104,
            now - TimeSpan.FromMinutes(5),
            "recent");

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(Path.GetDirectoryName(recentPayload)!));
        Assert.Equal(0, result.DeletedStagingAttempts);
    }

    [Fact]
    public async Task Cleanup_never_deletes_reparse_target()
    {
        var now = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.Combine(root, "staging"));
        var external = Path.Combine(
            Path.GetTempPath(),
            "mhwmm-updater-storage-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(external);
        await File.WriteAllTextAsync(
            Path.Combine(external, "keep.txt"),
            "KEEP",
            TestContext.Current.CancellationToken);

        var link = Path.Combine(
            root,
            "staging",
            "105-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateSymbolicLink(link, external);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException
                or IOException
                or PlatformNotSupportedException)
        {
            try { Directory.Delete(external, true); } catch { }
            return;
        }

        try
        {
            var result = await UpdateStorageMaintenance.CleanupRootAsync(
                root,
                now,
                TimeSpan.FromHours(1),
                log: null,
                CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(external, "keep.txt")));
            Assert.True(Directory.Exists(link));
            Assert.Equal(0, result.DeletedStagingAttempts);
        }
        finally
        {
            try { Directory.Delete(link); } catch { }
            try { Directory.Delete(external, true); } catch { }
        }
    }

    [Fact]
    public async Task Cleanup_reclaims_repeated_old_prepared_transactions_when_lease_owner_is_gone()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var transactions = Enumerable.Range(0, 3)
            .Select(index => CreatePreparedTransaction(
                106 + index,
                old,
                int.MaxValue - index,
                old))
            .ToArray();

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.All(transactions, transaction => Assert.False(Directory.Exists(transaction)));
        Assert.Equal(3, result.DeletedTransactions);
    }

    [Fact]
    public async Task Cleanup_preserves_old_prepared_transaction_while_recorded_owner_is_alive()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var ownerStartUtc = new DateTimeOffset(
            current.StartTime.ToUniversalTime());
        var transaction = CreatePreparedTransaction(
            109,
            old,
            Environment.ProcessId,
            ownerStartUtc);

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(transaction));
        Assert.Equal(0, result.DeletedTransactions);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Cleanup_reclaims_legacy_prepared_transaction_with_missing_start_after_owner_exits()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var transaction = CreatePreparedTransaction(
            110,
            old,
            int.MaxValue,
            ownerProcessStartUtc: null);

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.False(Directory.Exists(transaction));
        Assert.Equal(1, result.DeletedTransactions);
    }

    [Fact]
    public async Task Cleanup_preserves_legacy_prepared_transaction_with_missing_start_while_pid_exists()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var transaction = CreatePreparedTransaction(
            111,
            old,
            Environment.ProcessId,
            ownerProcessStartUtc: null);

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(transaction));
        Assert.Equal(0, result.DeletedTransactions);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Prepared_lease_write_requires_and_records_process_start_identity()
    {
        var build = 112L;
        var transaction = Path.Combine(
            root,
            "transactions",
            $"{build}-{new string('D', 24)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transaction);

        await UpdatePreparedTransactionLeaseStore.WriteAsync(
            transaction,
            build,
            Environment.ProcessId,
            TestContext.Current.CancellationToken);

        var lease = await UpdatePreparedTransactionLeaseStore.ReadAsync(
            Path.Combine(
                transaction,
                UpdatePreparedTransactionLeaseStore.FileName),
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease.OwnerProcessStartUtc);
        Assert.Equal(
            UpdatePreparedTransactionOwnerState.Active,
            UpdatePreparedTransactionLeaseStore.GetOwnerState(lease));
    }

    [Fact]
    public async Task Cleanup_preserves_legacy_journal_less_transaction_without_explicit_lease()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var transaction = Path.Combine(
            root,
            "transactions",
            $"113-{new string('C', 24)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(transaction, "helper"));
        File.WriteAllText(
            Path.Combine(transaction, "apply-request.json"),
            "{}");
        SetTreeLastWriteUtc(transaction, old);

        var result = await UpdateStorageMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            log: null,
            CancellationToken.None);

        Assert.True(Directory.Exists(transaction));
        Assert.Equal(0, result.DeletedTransactions);
    }

    [Fact]
    public void Confirmed_staging_cleanup_removes_entire_attempt()
    {
        var updaterRoot = UpdatePackageStager.GetUpdaterRoot();
        UpdatePackageStager.EnsureUpdaterRoot(updaterRoot);
        var build = 987654321L;
        var attempt = Path.Combine(
            updaterRoot,
            "staging",
            $"{build}-{Guid.NewGuid():N}");
        var payload = Path.Combine(attempt, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(
            Path.Combine(attempt, "release.zip"),
            "ARCHIVE");
        File.WriteAllText(
            Path.Combine(payload, "app.exe"),
            "PAYLOAD");

        try
        {
            Assert.True(
                UpdateStorageMaintenance.DeleteConfirmedStagingBestEffort(
                    payload,
                    build));
            Assert.False(Directory.Exists(attempt));
        }
        finally
        {
            try { Directory.Delete(attempt, true); } catch { }
        }
    }

    private string CreatePreparedTransaction(
        long build,
        DateTimeOffset writeUtc,
        int ownerProcessId,
        DateTimeOffset? ownerProcessStartUtc)
    {
        var transactionId =
            $"{build}-{new string('B', 24)}-{Guid.NewGuid():N}";
        var transaction = Path.Combine(
            root,
            "transactions",
            transactionId);
        Directory.CreateDirectory(Path.Combine(transaction, "helper"));
        File.WriteAllText(
            Path.Combine(transaction, "apply-request.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(
                transaction,
                UpdatePreparedTransactionLeaseStore.FileName),
            JsonSerializer.Serialize(
                new UpdatePreparedTransactionLease(
                    UpdatePreparedTransactionLease.CurrentSchemaVersion,
                    build,
                    transactionId,
                    ownerProcessId,
                    ownerProcessStartUtc,
                    writeUtc),
                UpdateProtocol.Json));
        SetTreeLastWriteUtc(transaction, writeUtc);
        return transaction;
    }

    private string CreateStagingAttempt(
        long build,
        DateTimeOffset writeUtc,
        string contents)
    {
        var attempt = Path.Combine(
            root,
            "staging",
            $"{build}-{Guid.NewGuid():N}");
        var payload = Path.Combine(attempt, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(
            Path.Combine(payload, "app.exe"),
            contents);
        SetTreeLastWriteUtc(attempt, writeUtc);
        return Path.GetFullPath(payload);
    }

    private string CreateTransaction(
        long build,
        UpdateJournalPhase phase,
        DateTimeOffset writeUtc,
        bool legacyName)
    {
        var name = legacyName
            ? $"{build}-{Guid.NewGuid():N}"
            : $"{build}-{new string('A', 24)}-{Guid.NewGuid():N}";
        var transaction = Path.Combine(
            root,
            "transactions",
            name);
        var backup = Path.Combine(transaction, "backup");
        Directory.CreateDirectory(backup);
        File.WriteAllText(
            Path.Combine(backup, "large.bin"),
            "OLD-BACKUP");
        File.WriteAllText(
            Path.Combine(transaction, "journal.json"),
            JsonSerializer.Serialize(
                new UpdateJournal(
                    phase,
                    build,
                    "source-sha",
                    writeUtc),
                UpdateProtocol.Json));
        SetTreeLastWriteUtc(transaction, writeUtc);
        return transaction;
    }

    private static UpdateManifest Manifest(long build) =>
        new(
            UpdateProtocol.ManifestSchemaVersion,
            UpdateProtocol.Channel,
            "8.8.82",
            "abcdef0123456789abcdef0123456789abcdef01",
            build,
            "release.zip",
            1,
            new string('A', 64),
            new string('B', 64),
            "app.exe",
            UpdateProtocol.UpdaterProtocolVersion,
            DateTimeOffset.UtcNow);

    private static void SetTreeLastWriteUtc(
        string path,
        DateTimeOffset value)
    {
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, value.UtcDateTime);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(
                file,
                value.UtcDateTime);
        foreach (var directory in Directory.EnumerateDirectories(
                     path,
                     "*",
                     SearchOption.AllDirectories)
                     .OrderByDescending(x => x.Length))
            Directory.SetLastWriteTimeUtc(
                directory,
                value.UtcDateTime);
        Directory.SetLastWriteTimeUtc(
            path,
            value.UtcDateTime);
    }
}
