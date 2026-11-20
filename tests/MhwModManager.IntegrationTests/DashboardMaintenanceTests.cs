using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DashboardMaintenanceTests
{
    public static TheoryData<string> Registered => new(DiscoveredGameProfiles.Load().Select(p => p.Id));
    public static TheoryData<string> ManualMounts => new(DiscoveredGameProfiles.Load()
        .Where(p => !string.IsNullOrWhiteSpace(p.ModRootRelativePath)).Select(p => p.Id));

    [Fact]
    public async Task ADesignatedLibraryAndSingleOrBatchSelectionShareOwnershipAndReceipts()
    {
        var root = Directory.CreateTempSubdirectory("manager-designated-");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var games = DiscoveredGameProfiles.Load().Select(p => DiscoveredGameProfiles.Isolate(p, root.FullName)).ToArray();
            var home = Path.Combine(root.FullName, "Manager");
            var state = Path.Combine(home, "State");
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "Downloads"));
            var paths = games.ToDictionary(p => p.Id, p =>
            {
                var archive = Path.Combine(folder.FullName, p.Id + ".zip");
                using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                using (var metadata = zip.CreateEntry("mod-intake.json").Open())
                    JsonSerializer.Serialize(metadata, new { gameId = p.Id });
                using var payload = new StreamWriter(zip.CreateEntry("payload.bin").Open());
                payload.Write(p.Id);
                return archive;
            });
            var digest = paths.ToDictionary(pair => pair.Key, pair => SHA256.HashData(File.ReadAllBytes(pair.Value)));
            var intake = new GameModIntakeService(home, state, () => games);
            intake.AddSource(folder.FullName);
            var reopened = new GameModIntakeService(home, state, () => games);
            Assert.Equal(folder.FullName, Assert.Single(reopened.LoadSources()));
            var report = await reopened.ScanAsync(ct);
            Assert.Equal(games.Length, report.Routed);
            foreach (var game in games)
            {
                Assert.Single(report.Items, row => row.GameId == game.Id && row.Status == GameModIntakeStatus.Routed);
                var inbox = GameModStorage.Resolve(home, game).InboxRoot;
                var dest = Assert.Single(Directory.GetFiles(inbox, "*.zip"));
                Assert.Equal(digest[game.Id], SHA256.HashData(File.ReadAllBytes(dest)));
                Assert.Equal(digest[game.Id], SHA256.HashData(File.ReadAllBytes(paths[game.Id])));
            }
            Assert.Equal(games.Length, (await reopened.ScanAsync(ct)).AlreadyPresent);
            reopened.RemoveSource(folder.FullName);
            Assert.Empty(intake.LoadSources());
            Assert.Empty((await intake.ScanAsync(ct)).Items);

            // A separate new workspace exercises first publication from both picker entry points.
            var other = Path.Combine(root.FullName, "Second Manager");
            var picker = new GameModIntakeService(other, Path.Combine(other, "State"), () => games);
            Assert.Equal(1, (await picker.ImportPathAsync(paths[games[0].Id], ct)).Routed);
            var batch = await picker.ImportPathsAsync(paths.Values, ct: ct);
            Assert.Equal(games.Length - 1, batch.Routed);
            Assert.Equal(1, batch.AlreadyPresent);
            using var saved = JsonDocument.Parse(File.ReadAllBytes(picker.ReportPath));
            Assert.Equal(games.Length, saved.RootElement.GetProperty("Items").GetArrayLength());
            foreach (var game in games)
            {
                var dest = Assert.Single(Directory.GetFiles(GameModStorage.Resolve(other, game).InboxRoot, "*.zip"));
                Assert.Equal(digest[game.Id], SHA256.HashData(File.ReadAllBytes(dest)));
            }
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void RelinkKeepsOldModsDiscoverableWithoutTreatingThemAsCurrentAfterRestart()
    {
        var root = Directory.CreateTempSubdirectory("manager-history-");
        try
        {
            var state = Path.Combine(root.FullName, "state");
            var registry = new GameProfileRegistry(state);
            var evidence = new Dictionary<string, (string OldPath, string ActivePath)>();
            foreach (var source in DiscoveredGameProfiles.Load())
            {
                var old = DiscoveredGameProfiles.Isolate(source, Path.Combine(root.FullName, "old"));
                var moved = DiscoveredGameProfiles.Isolate(source, Path.Combine(root.FullName, "new"));
                string AddMarker(string gameRoot)
                {
                    var directory = Directory.CreateDirectory(Path.Combine(gameRoot, "Mods", "Example"));
                    var marker = Path.Combine(directory.FullName, "mod.json");
                    File.WriteAllText(marker, "{}");
                    return marker;
                }
                evidence.Add(source.Id, (AddMarker(old.GameRoot), AddMarker(moved.GameRoot)));
                registry.Upsert(old);
                Directory.CreateDirectory(Path.GetDirectoryName(moved.ExecutablePath)!);
                File.WriteAllBytes(moved.ExecutablePath, [8, 42]);
                registry.RepairFromExecutable(old, moved.ExecutablePath, activate: false);
            }
            var reopened = new GameProfileRegistry(state);
            var observations = LocalModInventory.Scan(reopened.Load(), [], [], reopened.ReadInstallHistory(),
                TestContext.Current.CancellationToken).ToLookup(row => row.Location, PathRules.Comparer);
            foreach (var (id, locations) in evidence)
            {
                var active = Assert.Single(observations[locations.ActivePath]);
                var old = Assert.Single(observations[locations.OldPath]);
                Assert.Equal(id, active.RegisteredProfileId);
                Assert.Null(old.RegisteredProfileId);
                Assert.StartsWith("Previous installation", old.Evidence, StringComparison.Ordinal);
                Assert.Equal("{}", File.ReadAllText(locations.OldPath));
            }
        }
        finally { root.Delete(true); }
    }
    [Theory]
    [MemberData(nameof(Registered))]
    public async Task DuplicateCleanupAndPortableExportPreserveTheEnabledPackageAndItsContent(string id)
    {
        var root = Directory.CreateTempSubdirectory("manager-export-cleanup-");
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root.FullName, "state", "manager.db"));
        try
        {
            var profile = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(p => p.Id == id), root.FullName);
            var storage = GameModStorage.Resolve(root.FullName, profile);
            await db.InitializeAsync(ct);
            var hash = new HashingService();
            var blobs = new BlobStore(Path.Combine(root.FullName, "blobs"), db);
            var scanner = new ModScanner(db, blobs, hash, profile);
            byte[] bytes = [12, 54, 109];
            foreach (var key in new[] { "keeper", "duplicate" })
            {
                var dir = Directory.CreateDirectory(Path.Combine(storage.InstalledRoot, key));
                File.WriteAllBytes(Path.Combine(dir.FullName, "manual.pak"), bytes);
                var mod = new ModDescriptor(key, key, key, dir.FullName, key == "keeper", key == "keeper" ? 10 : 20);
                await db.UpsertModAsync(mod, ct);
                await scanner.CaptureAsync(mod, ct);
            }
            var cleanup = new DuplicateCleanupService(db, storage.ArchiveRoot);
            var analysis = await cleanup.AnalyzeAsync(ct);
            Assert.Equal("keeper", Assert.Single(analysis.ExactDuplicates).RecommendedKeeperId);
            Assert.Equal(1, await cleanup.ArchiveSafeAsync(ct));
            Assert.Equal(0, await cleanup.ArchiveSafeAsync(ct));
            Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(storage.ArchiveRoot, "manual.pak", SearchOption.AllDirectories))));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(storage.InstalledRoot, "keeper", "manual.pak")));
            Assert.Equal("keeper", Assert.Single(await db.GetModsAsync(ct)).Id);

            var recipes = new CollectionRecipeService(db, profile);
            var destination = Path.Combine(root.FullName, "Export", "my-setup.json");
            await recipes.ExportAsync(destination, ct);
            var restored = await recipes.PreviewAsync(destination, ct);
            Assert.Equal(profile.Id, restored.Recipe.GameId);
            var entry = Assert.Single(restored.Matches);
            Assert.Equal("keeper", entry.LocalModId);
            Assert.True(entry.CanRestore);
            Assert.True(entry.Entry.Enabled);
            Assert.Equal(10, entry.Entry.Priority);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), Assert.Single(entry.Entry.Files!).Value);
            Assert.False(Directory.Exists(profile.LiveModRoot));
        }
        finally
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            root.Delete(true);
        }
    }

    [Theory]
    [MemberData(nameof(ManualMounts))]
    public async Task ManualAdoptionCopiesOnlyModFilesAndLeavesOriginalsInPlace(string id)
    {
        var root = Directory.CreateTempSubdirectory("manager-adoption-");
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root.FullName, "state", "manager.db"));
        try
        {
            await db.InitializeAsync(ct);
            var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(p => p.Id == id), root.FullName);
            var storage = GameModStorage.Resolve(root.FullName, game);
            var unreal = game.AdapterId == "unreal-paks" && Path.GetFileName(game.LiveModRoot).Equals("Paks", StringComparison.OrdinalIgnoreCase);
            var dir = Directory.CreateDirectory(unreal ? Path.Combine(game.LiveModRoot, "~mods") : game.LiveModRoot);
            var mod = Path.Combine(dir.FullName, unreal ? "Costume_P.pak" : "ManualMod.bin");
            File.WriteAllText(mod, "manual mod");
            var stock = Path.Combine(game.LiveModRoot, "pakchunk0-Windows.pak");
            if (unreal) File.WriteAllText(stock, "retail game package");
            var hash = new HashingService();
            var service = new UnmanagedAdoptionService(db, new PlannerSnapshotRepository(db), hash, storage.InstalledRoot, game);
            var result = await service.AdoptAsync(ct);
            Assert.True(result.Created);
            Assert.Equal(1, result.FileCount);
            var copied = Assert.Single(Directory.GetFiles(storage.InstalledRoot, "*", SearchOption.AllDirectories));
            Assert.Equal("manual mod", File.ReadAllText(copied));
            Assert.Equal("manual mod", File.ReadAllText(mod));
            if (unreal) Assert.Equal("retail game package", File.ReadAllText(stock));
            Assert.False((await service.AdoptAsync(ct)).Created);
            var scanner = new ModScanner(db, new BlobStore(Path.Combine(root.FullName, "blobs"), db), hash, game);
            await new CatalogService(db, scanner, storage.InstalledRoot).RefreshFoldersAsync(ct);
            Assert.False(Assert.Single(await db.GetModsAsync(ct)).Enabled);
        }
        finally
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            root.Delete(true);
        }
    }
}
