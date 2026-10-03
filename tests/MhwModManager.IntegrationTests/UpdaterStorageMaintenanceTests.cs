using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

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
