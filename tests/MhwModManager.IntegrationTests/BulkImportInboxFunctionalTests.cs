using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>Real service-chain acceptance against uniquely owned temporary game library roots.</summary>
public sealed class BulkImportInboxFunctionalTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-e2e-intake-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoutedArchiveBecomesDisabledIndexedModAfterExplicitInboxProcessing()
    {
        var sourceRoot = Path.Combine(root, "Downloads");
        var managerRoot = Path.Combine(root, "Manager");
        Directory.CreateDirectory(sourceRoot);
        var original = Path.Combine(sourceRoot, "MHW Armor.zip");
        using (var zip = ZipFile.Open(original, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("nativePC/pl/armor/actual.tex", CompressionLevel.NoCompression);
            await using var data = entry.Open();
            await data.WriteAsync(new byte[4096], TestToken);
        }
        var originalBytes = await File.ReadAllBytesAsync(original, TestToken);
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "MHW"));
        var paths = GameModStorage.Resolve(managerRoot, game);
        var db = await CreateDatabaseAsync();
        var inspector = new ArchiveInspector();
        var routing = new GameModIntakeService(managerRoot, Path.Combine(managerRoot, "State"),
            () => new[] { game }, inspector);
        var intake = await routing.ImportPathsAsync([original], ct: TestToken);
        Assert.Equal(1, intake.Routed);
        Assert.Equal(0, intake.NeedsReview);
        Assert.Single(Directory.GetFiles(paths.InboxRoot, "*.zip"));
        Assert.Empty(await db.GetModsAsync(TestToken));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original, TestToken));

        var blobs = new BlobStore(Path.Combine(root, "Blobs"), db);
        var scanner = new ModScanner(db, blobs, new HashingService(), game);
        var catalog = new CatalogService(db, scanner, paths.InstalledRoot, paths.LegacyInstalledRoot);
        var metadata = new NexusMetadataService(db, new PlannerSnapshotRepository(db),
            Path.Combine(root, "State"), game);
        var service = new SmartInboxService(db, inspector, catalog, metadata,
            new AutoCategoryService(db), paths.InboxRoot, paths.InstalledRoot,
            legacyInboxRoot: paths.LegacyInboxRoot);

        var result = await service.ProcessAsync(TestToken);
        Assert.Equal(1, result.Imported);
        var mod = Assert.Single(await db.GetModsAsync(TestToken));
        Assert.False(mod.Enabled);
        Assert.Equal("MHW Armor", mod.DisplayName);
        Assert.Equal("MHW Armor", Path.GetFileName(mod.SourcePath));
        Assert.True(Directory.Exists(mod.SourcePath));
        Assert.Contains(await db.GetModFilesAsync(TestToken),
            file => file.ModId == mod.Id && file.Path.Contains("nativePC", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.GetFiles(paths.InboxRoot, "*.zip"));
        Assert.Single(Directory.GetFiles(Path.Combine(paths.InboxRoot, "Processed"), "*.zip"));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original, TestToken));

        var repeated = await routing.ImportPathsAsync([original], ct: TestToken);
        Assert.Equal(0, repeated.Routed);
        Assert.Equal(1, repeated.AlreadyPresent);
        Assert.Empty(Directory.GetFiles(paths.InboxRoot, "*.zip"));
    }

    [Fact]
    public async Task UnidentifiedSourceRemainsUnchangedAndNeverAppearsAsInstalledMod()
    {
        var sourceRoot = Path.Combine(root, "Downloads-unknown");
        Directory.CreateDirectory(sourceRoot);
        var original = Path.Combine(sourceRoot, "unattributed.zip");
        using (var zip = ZipFile.Open(original, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("generic/no-game-evidence.bin", CompressionLevel.NoCompression);
            await using var output = entry.Open();
            await output.WriteAsync(new byte[1024], TestToken);
        }
        var snapshot = await File.ReadAllBytesAsync(original, TestToken);
        var managerRoot = Path.Combine(root, "Manager");
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "MHW"));
        var routing = new GameModIntakeService(managerRoot, Path.Combine(managerRoot, "State"),
            () => new[] { game });
        var report = await routing.ImportPathsAsync([original], ct: TestToken);
        Assert.Equal(0, report.Routed);
        Assert.True(report.NeedsReview > 0);
        Assert.Equal(snapshot, await File.ReadAllBytesAsync(original, TestToken));
        var paths = GameModStorage.Resolve(managerRoot, game);
        Assert.False(Directory.Exists(paths.InboxRoot) &&
            Directory.EnumerateFileSystemEntries(paths.InboxRoot).Any());
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync()
    {
        var database = new ManagerDatabase(Path.Combine(root, "Db", "manager.db"));
        await database.InitializeAsync(TestToken);
        return database;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
