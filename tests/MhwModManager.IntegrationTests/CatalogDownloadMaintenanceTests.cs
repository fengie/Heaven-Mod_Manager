using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Automation;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogDownloadMaintenanceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhwmm-catalog-download-retention-" + Guid.NewGuid().ToString("N"));

    public CatalogDownloadMaintenanceTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Cleanup_reclaims_old_download_when_recorded_owner_is_gone()
    {
        var now = DateTimeOffset.UtcNow;
        var old = now - TimeSpan.FromHours(3);
        var owned = await CreateOwnedDownloadAsync(
            old,
            ownerProcessId: int.MaxValue,
            ownerProcessStartUtc: old,
            bytes: 4096);

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            CatalogDownloadMaintenance.DefaultMaxOwnedBytes,
            log: null,
            CancellationToken.None);

        Assert.False(File.Exists(owned.Archive));
        Assert.False(File.Exists(owned.Lease));
        Assert.Equal(1, result.DeletedDownloads);
        Assert.Equal(1, result.DeletedLeases);
        Assert.True(result.ReclaimedBytes >= 4096);
    }

    [Fact]
    public async Task Cleanup_preserves_download_while_recorded_owner_is_alive()
    {
        var now = DateTimeOffset.UtcNow;
        using var current = Process.GetCurrentProcess();
        var startUtc = new DateTimeOffset(
            current.StartTime.ToUniversalTime());
        var owned = await CreateOwnedDownloadAsync(
            now - TimeSpan.FromHours(3),
            Environment.ProcessId,
            startUtc,
            bytes: 4096);

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.Zero,
            maxOwnedBytes: 0,
            log: null,
            CancellationToken.None);

        Assert.True(File.Exists(owned.Archive));
        Assert.True(File.Exists(owned.Lease));
        Assert.Equal(0, result.DeletedDownloads);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Cleanup_preserves_download_when_owner_identity_cannot_be_proven()
    {
        var now = DateTimeOffset.UtcNow;
        var archive = Path.Combine(
            root,
            $"catalog-{Guid.NewGuid():N}.zip");
        var lease = archive + ".lease.json";
        await File.WriteAllBytesAsync(
            archive,
            new byte[4096],
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            lease,
            JsonSerializer.Serialize(
                new CatalogDownloadLease(
                    CatalogDownloadLease.CurrentSchemaVersion,
                    Path.GetFileName(archive),
                    int.MaxValue,
                    OwnerProcessStartUtc: null,
                    now - TimeSpan.FromHours(3))),
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(
            archive,
            (now - TimeSpan.FromHours(3)).UtcDateTime);
        File.SetLastWriteTimeUtc(
            lease,
            (now - TimeSpan.FromHours(3)).UtcDateTime);

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.Zero,
            maxOwnedBytes: 0,
            log: null,
            CancellationToken.None);

        Assert.True(File.Exists(archive));
        Assert.True(File.Exists(lease));
        Assert.Equal(0, result.DeletedDownloads);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Cleanup_reclaims_abandoned_temporary_lease_when_recorded_owner_is_gone()
    {
        var now = DateTimeOffset.UtcNow;
        var temporaryLease = await CreateTemporaryLeaseAsync(
            now - TimeSpan.FromHours(3),
            ownerProcessId: int.MaxValue,
            ownerProcessStartUtc: now - TimeSpan.FromHours(3));

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            CatalogDownloadMaintenance.DefaultMaxOwnedBytes,
            log: null,
            CancellationToken.None);

        Assert.False(File.Exists(temporaryLease));
        Assert.Equal(0, result.DeletedDownloads);
        Assert.Equal(1, result.DeletedLeases);
        Assert.True(result.ReclaimedBytes > 0);
    }

    [Fact]
    public async Task Cleanup_preserves_temporary_lease_while_recorded_owner_is_alive()
    {
        var now = DateTimeOffset.UtcNow;
        using var current = Process.GetCurrentProcess();
        var startUtc = new DateTimeOffset(
            current.StartTime.ToUniversalTime());
        var temporaryLease = await CreateTemporaryLeaseAsync(
            now - TimeSpan.FromHours(3),
            Environment.ProcessId,
            startUtc);

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.Zero,
            maxOwnedBytes: 0,
            log: null,
            CancellationToken.None);

        Assert.True(File.Exists(temporaryLease));
        Assert.Equal(0, result.DeletedLeases);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Cleanup_enforces_byte_quota_for_recent_exited_downloads()
    {
        var now = DateTimeOffset.UtcNow;
        var first = await CreateOwnedDownloadAsync(
            now - TimeSpan.FromMinutes(20),
            int.MaxValue,
            now - TimeSpan.FromHours(1),
            bytes: 8192);
        var second = await CreateOwnedDownloadAsync(
            now - TimeSpan.FromMinutes(10),
            int.MaxValue - 1,
            now - TimeSpan.FromHours(1),
            bytes: 8192);

        var secondBytes =
            new FileInfo(second.Archive).Length
            + new FileInfo(second.Lease).Length;

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            now,
            TimeSpan.FromHours(1),
            maxOwnedBytes: secondBytes,
            log: null,
            CancellationToken.None);

        Assert.False(File.Exists(first.Archive));
        Assert.False(File.Exists(first.Lease));
        Assert.True(File.Exists(second.Archive));
        Assert.True(File.Exists(second.Lease));
        Assert.Equal(1, result.DeletedDownloads);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Cleanup_preserves_unleased_unknown_catalog_file()
    {
        var unknown = Path.Combine(
            root,
            $"catalog-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(
            unknown,
            "unknown ownership",
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(
            unknown,
            DateTime.UtcNow - TimeSpan.FromDays(7));

        var result = await CatalogDownloadMaintenance.CleanupRootAsync(
            root,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            maxOwnedBytes: 0,
            log: null,
            CancellationToken.None);

        Assert.True(File.Exists(unknown));
        Assert.Equal(0, result.DeletedDownloads);
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public async Task Cleanup_refuses_reparse_root()
    {
        var target = Path.Combine(
            Path.GetTempPath(),
            "mhwmm-catalog-download-target-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(
            Path.GetTempPath(),
            "mhwmm-catalog-download-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                    or IOException
                    or PlatformNotSupportedException)
            {
                return;
            }

            await Assert.ThrowsAsync<IOException>(() =>
                CatalogDownloadMaintenance.CleanupRootAsync(
                    link,
                    DateTimeOffset.UtcNow,
                    TimeSpan.Zero,
                    maxOwnedBytes: 0,
                    log: null,
                    CancellationToken.None));
        }
        finally
        {
            try { Directory.Delete(link); } catch { }
            try { Directory.Delete(target, true); } catch { }
        }
    }

    private async Task<string> CreateTemporaryLeaseAsync(
        DateTimeOffset writeUtc,
        int ownerProcessId,
        DateTimeOffset ownerProcessStartUtc)
    {
        var archiveName = $"catalog-{Guid.NewGuid():N}.zip";
        var temporaryLease = Path.Combine(
            root,
            archiveName + ".lease.json.tmp-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(
            temporaryLease,
            JsonSerializer.Serialize(
                new CatalogDownloadLease(
                    CatalogDownloadLease.CurrentSchemaVersion,
                    archiveName,
                    ownerProcessId,
                    ownerProcessStartUtc,
                    writeUtc)),
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(
            temporaryLease,
            writeUtc.UtcDateTime);
        return temporaryLease;
    }

    private async Task<(string Archive, string Lease)> CreateOwnedDownloadAsync(
        DateTimeOffset writeUtc,
        int ownerProcessId,
        DateTimeOffset ownerProcessStartUtc,
        int bytes)
    {
        var archive = Path.Combine(
            root,
            $"catalog-{Guid.NewGuid():N}.zip");
        var lease = archive + ".lease.json";

        await File.WriteAllBytesAsync(
            archive,
            new byte[bytes],
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            lease,
            JsonSerializer.Serialize(
                new CatalogDownloadLease(
                    CatalogDownloadLease.CurrentSchemaVersion,
                    Path.GetFileName(archive),
                    ownerProcessId,
                    ownerProcessStartUtc,
                    writeUtc)),
            TestContext.Current.CancellationToken);

        File.SetLastWriteTimeUtc(archive, writeUtc.UtcDateTime);
        File.SetLastWriteTimeUtc(lease, writeUtc.UtcDateTime);
        return (archive, lease);
    }
}
