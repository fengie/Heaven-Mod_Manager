using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

/// <summary>
/// An exception rollback is not enough when the process terminates between
/// directory publication to Archive and its SQLite retirement transaction.
/// The durable pending intent must make the next startup lossless.
/// </summary>
public sealed class DuplicateArchiveRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-archive-recovery-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public DuplicateArchiveRecoveryTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task CrashAfterDirectoryMoveRestoresSourceBeforeCatalogRefresh()
    {
        var (db, source, keeper, archive) = await SeedDuplicatePairAsync("moved-before-retirement.db");
        var interrupted = new DuplicateCleanupService(db, archive, (point, _) =>
        {
            if (point == DuplicateArchiveFaultPoint.AfterMoveBeforeDatabaseRetirement)
                throw new InvalidOperationException("simulated process termination");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.ArchiveSafeAsync(Token));
        Assert.False(Directory.Exists(source));
        Assert.True(Directory.Exists(keeper));
        Assert.Equal(2, (await db.GetModsAsync(Token)).Count);
        Assert.Single(Directory.GetFiles(archive, ".duplicate-archive-*.pending.json"));

        var recovered = await new DuplicateCleanupService(db, archive).RecoverInterruptedArchivesAsync(Token);
        Assert.Equal(1, recovered);
        Assert.Equal("preserve-source", await File.ReadAllTextAsync(Path.Combine(source, "sentinel.txt"), Token));
        Assert.True(Directory.Exists(keeper));
        Assert.Equal(2, (await db.GetModsAsync(Token)).Count);
        Assert.Empty(Directory.GetFiles(archive, ".duplicate-archive-*.pending.json"));
        Assert.Equal(0, await new DuplicateCleanupService(db, archive).RecoverInterruptedArchivesAsync(Token));
    }

    [Fact]
    public async Task CrashAfterDatabaseRetirementKeepsArchiveAndClearsPendingIntent()
    {
        var (db, source, keeper, archive) = await SeedDuplicatePairAsync("retired-before-cleanup.db");
        var interrupted = new DuplicateCleanupService(db, archive, (point, _) =>
        {
            if (point == DuplicateArchiveFaultPoint.AfterDatabaseRetirementBeforeJournalCleanup)
                throw new InvalidOperationException("simulated process termination");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.ArchiveSafeAsync(Token));
        Assert.False(Directory.Exists(source));
        Assert.Equal("preserve-source", await File.ReadAllTextAsync(Path.Combine(keeper, "sentinel.txt"), Token));
        Assert.Single(await db.GetModsAsync(Token));
        Assert.Single(Directory.GetFiles(archive, ".duplicate-archive-*.pending.json"));

        var recovered = await new DuplicateCleanupService(db, archive).RecoverInterruptedArchivesAsync(Token);
        Assert.Equal(0, recovered);
        Assert.Empty(Directory.GetFiles(archive, ".duplicate-archive-*.pending.json"));
        var moved = Assert.Single(Directory.GetDirectories(archive));
        Assert.Equal("preserve-source", await File.ReadAllTextAsync(Path.Combine(moved, "sentinel.txt"), Token));
    }

    [Fact]
    public async Task RecoveryRefusesToOverwriteSourceCreatedAfterInterruptedMove()
    {
        var (db, source, _, archive) = await SeedDuplicatePairAsync("conflicting-recovery.db");
        var interrupted = new DuplicateCleanupService(db, archive, (point, _) =>
        {
            if (point == DuplicateArchiveFaultPoint.AfterMoveBeforeDatabaseRetirement)
                throw new InvalidOperationException("simulated process termination");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.ArchiveSafeAsync(Token));
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "sentinel.txt"), "new-independent-data", Token);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new DuplicateCleanupService(db, archive).RecoverInterruptedArchivesAsync(Token));

        Assert.Equal("new-independent-data", await File.ReadAllTextAsync(Path.Combine(source, "sentinel.txt"), Token));
        Assert.Equal("preserve-source",
            await File.ReadAllTextAsync(Path.Combine(Assert.Single(Directory.GetDirectories(archive)), "sentinel.txt"), Token));
        Assert.Single(Directory.GetFiles(archive, ".duplicate-archive-*.pending.json"));
        Assert.Equal(2, (await db.GetModsAsync(Token)).Count);
    }

    [Fact]
    public async Task SuccessfulArchiveLeavesNoPendingIntent()
    {
        var (db, source, _, archive) = await SeedDuplicatePairAsync("successful-archive.db");
        Assert.Equal(1, await new DuplicateCleanupService(db, archive).ArchiveSafeAsync(Token));
        Assert.False(Directory.Exists(source));
        Assert.Single(await db.GetModsAsync(Token));
        Assert.Empty(Directory.GetFiles(archive, ".duplicate-archive-*.pending.json"));
    }

    private async Task<(ManagerDatabase Db, string Source, string Keeper, string Archive)> SeedDuplicatePairAsync(string fileName)
    {
        var db = new ManagerDatabase(Path.Combine(root, fileName));
        await db.InitializeAsync(Token);
        var source = Path.Combine(root, Path.GetFileNameWithoutExtension(fileName) + "-old");
        var keeper = Path.Combine(root, Path.GetFileNameWithoutExtension(fileName) + "-new");
        var archive = Path.Combine(root, Path.GetFileNameWithoutExtension(fileName) + "-archive");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(keeper);
        await File.WriteAllTextAsync(Path.Combine(source, "sentinel.txt"), "preserve-source", Token);
        await File.WriteAllTextAsync(Path.Combine(keeper, "sentinel.txt"), "preserve-source", Token);
        await db.UpsertModAsync(new("old", "Older", "Older", source, false, 1), Token);
        await db.UpsertModAsync(new("new", "Newer", "Newer", keeper, false, 2), Token);
        await db.ReplaceModFilesAsync("old",
            [new ModFileDescriptor("old", @"nativePC\shared.tex", "identical", null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        await db.ReplaceModFilesAsync("new",
            [new ModFileDescriptor("new", @"nativePC\shared.tex", "identical", null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        return (db, source, keeper, archive);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
