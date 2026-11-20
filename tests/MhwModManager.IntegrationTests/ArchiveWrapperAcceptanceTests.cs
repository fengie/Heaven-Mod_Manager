using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ArchiveWrapperAcceptanceTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task RegisteredProjectRootSurvivesArchiveImport(bool useInbox, bool transportWrapper, bool mixedRuntime)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory();
        var db = new ManagerDatabase(Path.Combine(root.FullName, "state", "catalog.db"));
        try
        {
            var game = GameProfile.Generic("quartz", "Quartz", Path.Combine(root.FullName, "game"),
                "Quartz.exe", @"QuartzProject\Content\Paks", "unreal-paks");
            Directory.CreateDirectory(game.LiveModRoot);
            var paths = GameModStorage.Resolve(root.FullName, game);

            await db.InitializeAsync(ct);
            var scanner = new ModScanner(db, new BlobStore(Path.Combine(root.FullName, "blobs"), db),
                new HashingService(), game);
            var inspector = new ArchiveInspector();
            var intake = new GameModIntakeService(root.FullName, Path.Combine(root.FullName, "state"), () => [game], inspector);
            var catalog = new CatalogService(db, scanner, paths.InstalledRoot, paths.LegacyInstalledRoot,
                (dir, cancel) => intake.SelectableManagedFolders(dir, game, cancel));
            var zip = Path.Combine(root.FullName, "Quartz garment.zip");
            const string payload = "QuartzProject/Content/Paks/~mods/garment.pak";
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var prefix = transportWrapper ? "download-wrapper/" : "";
                var pathsInArchive = mixedRuntime
                    ? new[] { payload, "QuartzProject/Binaries/Win64/ue4ss/Mods/Test/main.lua",
                        "QuartzProject/Content/Paks/LogicMods/recipe.pak", "QuartzProject/Content/Paks/~mods/garment.json" }
                    : [payload];
                foreach (var path in pathsInArchive)
                {
                    await using var body = archive.CreateEntry(prefix + path).Open();
                    await body.WriteAsync(new byte[] { 1, 2, 3 }, ct);
                }
            }
            if (useInbox)
            {
                var routing = await intake.ImportPathsAsync([zip], ct: ct);
                Assert.Equal(1, routing.Routed);
                var inbox = new SmartInboxService(db, inspector, catalog,
                    new NexusMetadataService(db, new PlannerSnapshotRepository(db), Path.Combine(root.FullName, "state"), game),
                    new AutoCategoryService(db), paths.InboxRoot, paths.InstalledRoot);
                var result = await inbox.ProcessAsync(null, refreshMetadata: false, ct);
                Assert.Equal(1, result.Imported);
            }
            else
            {
                var importer = new ArchiveImportService(inspector, catalog, paths.InstalledRoot);
                await importer.ImportAsync(zip, ct);
            }

            var mod = Assert.Single(await db.GetModsAsync(ct));
            Assert.False(mod.Enabled);
            Assert.True(File.Exists(Path.Combine(mod.SourcePath, payload)));
            if (mixedRuntime)
            {
                Assert.True(File.Exists(Path.Combine(mod.SourcePath, "QuartzProject/Binaries/Win64/ue4ss/Mods/Test/main.lua")));
                Assert.True(File.Exists(Path.Combine(mod.SourcePath, "QuartzProject/Content/Paks/LogicMods/recipe.pak")));
                Assert.True(File.Exists(Path.Combine(mod.SourcePath, "QuartzProject/Content/Paks/~mods/garment.json")));
            }
            else
            {
                await catalog.EnsureCapturedAsync([mod], ct);
                var captured = Assert.Single(await db.GetModFilesAsync(ct));
                Assert.Equal(@"root\QuartzProject\Content\Paks\~mods\garment.pak", captured.Path, ignoreCase: true);
            }
            Assert.Empty(Directory.GetFiles(game.GameRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            await using (var connection = await db.OpenAsync(ct))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            root.Delete(true);
        }
    }
}
