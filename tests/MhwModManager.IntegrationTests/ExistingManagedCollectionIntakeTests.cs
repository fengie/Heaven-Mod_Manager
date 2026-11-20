using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Reanalysis of already indexed single-package folders must discover independent nested
/// mods without deleting, moving or applying the original tracked source package.
/// </summary>
public sealed class ExistingManagedCollectionIntakeTests : IAsyncDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-managed-collection-" + Guid.NewGuid().ToString("N"));
    private ManagerDatabase? catalogDatabase;
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private string Manager => Path.Combine(home, "Manager");

    private GameProfile Game => GameProfile.Generic("stellar-blade", "Stellar Blade",
        Path.Combine(home, "Game"), "StellarBlade.exe", "SB/Content/Paks/~mods",
        nexusGameDomain: "stellarblade");

    [Fact]
    public async Task Existing_shelf_reuses_ids_captures_distinct_payloads_and_preserves_reviews_and_active_mod()
    {
        var game = GameProfile.Generic("fixture", "Fixture", Path.Combine(home, "game"), "game.exe", "Project/Content/Paks", "unreal-paks");
        var installed = GameModStorage.Resolve(Manager, game).InstalledRoot;
        var shelf = Path.Combine(installed, "Unnamed shelf");
        async Task Write(string root, string name, string value)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, value, TestToken);
        }
        var active = Path.Combine(installed, "Original");
        var copy = Path.Combine(shelf, "Package copy");
        var unique = Path.Combine(shelf, "Package variant");
        foreach (var folder in new[] { active, copy, unique })
        {
            await Write(folder, "outfit.pak", folder == unique ? "different" : "same");
            await Write(folder, "outfit.sig", "same-signature");
        }
        var authored = Path.Combine(shelf, "Authored");
        await Write(authored, "final.pak", "finished");
        await Write(authored, "sources/Project/Game.uproject", "{}");
        await Write(authored, "sources/Model.blend", "source");
        await Write(authored, "Installation Instructions.txt", "documentation");
        var uncertain = Path.Combine(shelf, "Choose options");
        await Write(uncertain, "Low/option.pak", "low");
        await Write(uncertain, "High/option.pak", "high");
        await Write(shelf, "desktop.ini", "metadata");
        var db = catalogDatabase = new ManagerDatabase(Path.Combine(home, "state", "manager.db"));
        await db.InitializeAsync(TestToken);
        var scanner = new ModScanner(db, new BlobStore(Path.Combine(home, "blobs"), db), new HashingService(), game);
        var original = new ModDescriptor("active", "active", "Original", active, true, 12);
        await db.UpsertModAsync(original, TestToken);
        await scanner.CaptureAsync(original, TestToken);
        await db.UpsertModAsync(new("wrapper","wrapper","Wrapper",shelf,false,51,Category: "Texture"), TestToken);
        await db.UpsertModAsync(new("saved","saved","Saved name",unique,false,57), TestToken);
        var intake = new GameModIntakeService(Manager, Path.Combine(Manager, "State"), () => [game]);
        var catalog = new CatalogService(db, scanner, installed, inspectShelf: (folder, ct) => intake.InspectManagedShelf(folder, game, ct));
        for (var pass = 0; pass < 2; pass++)
        {
            await catalog.RefreshFoldersAsync(TestToken);
            var rows = await db.GetModsAsync(TestToken);
            Assert.Equal(4, rows.Count);
            Assert.Equal("active", Assert.Single(rows, row => row.Enabled).Id);
            Assert.Equal(57, Assert.Single(rows, row => row.Id == "saved").Priority);
            var review = Assert.Single(rows, row => row.SourcePath == uncertain);
            Assert.True(review.NeedsRevalidation);
            await Assert.ThrowsAsync<InvalidDataException>(() => catalog.EnsureCapturedAsync([review], TestToken));
            var expected = Assert.Single(rows, row => row.SourcePath == authored);
            var indexed = (await db.GetModFilesAsync(TestToken)).Where(file => file.ModId == expected.Id).ToArray();
            Assert.EndsWith(@"~mods\final.pak", Assert.Single(indexed).Path, StringComparison.OrdinalIgnoreCase);
            var sources = await db.GetModsAsync(true, TestToken);
            Assert.Equal(6, sources.Count);
            Assert.Equal("Texture", Assert.Single(sources, row => row.Id == "wrapper").Category);
        }
        var unavailable = new CatalogService(db, scanner, installed, inspectShelf: (_, _) => throw new IOException("Transient USB read failure"));
        await unavailable.RefreshFoldersAsync(TestToken);
        Assert.Equal(4, (await db.GetModsAsync(TestToken)).Count);
        Assert.Equal("review", await db.ShelfRoleAsync((await db.GetModsAsync(TestToken)).Single(row => row.SourcePath == uncertain).Id, TestToken));
        await Write(copy, "outfit.pak", "new independent revision");
        await catalog.RefreshFoldersAsync(TestToken);
        Assert.Contains(await db.GetModsAsync(TestToken), row => row.SourcePath == copy && !row.Enabled);
        Assert.True(File.Exists(Path.Combine(authored, "sources/Model.blend")));
        Assert.False(Directory.Exists(game.GameRoot));
    }

    [Fact]
    public async Task Fresh_folder_inbox_publication_indexes_independent_nested_children_before_returning()
    {
        var game = GameProfile.Generic("quartz", "Quartz", Path.Combine(home, "game"), "game.exe", "Quartz/Content/Paks", "unreal-paks");
        var storage = GameModStorage.Resolve(Manager, game);
        string[] sources = ["Shelf/Outer/Favorites/Blue/blue.pak", "Shelf/Outer/Favorites/Gold/gold.pak", "Shelf/Outer/Other/Green/green.pak"];
        foreach (var name in sources)
        {
            var path = Path.Combine(storage.InboxRoot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, name, TestToken);
        }
        var db = catalogDatabase = new ManagerDatabase(Path.Combine(home, "state", "manager.db"));
        await db.InitializeAsync(TestToken);
        var scanner = new ModScanner(db, new BlobStore(Path.Combine(home, "blobs"), db), new HashingService(), game);
        var inspector = new ArchiveInspector();
        var intake = new GameModIntakeService(Manager, Path.Combine(Manager, "State"), () => [game], inspector);
        var catalog = new CatalogService(db, scanner, storage.InstalledRoot, inspectShelf: (folder, ct) => intake.InspectManagedShelf(folder, game, ct));
        var inbox = new SmartInboxService(db, inspector, catalog,
            new NexusMetadataService(db, new PlannerSnapshotRepository(db), Path.Combine(Manager, "State"), game),
            new AutoCategoryService(db), storage.InboxRoot, storage.InstalledRoot);
        Assert.Equal(1, (await inbox.ProcessAsync(null, refreshMetadata: false, TestToken)).Imported);
        var rows = await db.GetModsAsync(TestToken);
        Assert.Equal("Blue,Gold,Green", string.Join(",", rows.Select(row => row.Name).Order()));
        Assert.All(rows, row => Assert.False(row.Enabled));
        Assert.Equal(3, (await db.GetModFilesAsync(TestToken)).Count);
        Assert.False(Directory.Exists(game.GameRoot));
    }

    [Theory]
    [InlineData("sifu", "Sifu/Content/Paks", "fav", "mai favorite", "pakchunk99-MAI-By-serikase", false)]
    [InlineData("stellar-blade", "SB/Content/Paks/~mods", "old apply", "Scanning Invisible Drone-167-1-0-1749821637", "InvisibleDrone_P", true)]
    [InlineData("third-game", "NovelProject/Content/Paks", "custom shelf", "Another package", "sample", true)]
    public async Task Normal_catalog_refresh_discovers_disabled_named_packages_without_copying_sources(
        string id, string mount, string wrapper, string name, string stem, bool loaderDirectory)
    {
        var game = GameProfile.Generic(id, id, Path.Combine(home, "game"), "game.exe", mount, "unreal-paks");
        var installed = GameModStorage.Resolve(Manager, game).InstalledRoot;
        var collection = Directory.CreateDirectory(Path.Combine(installed, wrapper)).FullName;
        var source = Path.Combine(collection, name);
        var payload = Directory.CreateDirectory(loaderDirectory ? Path.Combine(source, "~mods") : source).FullName;
        string[] extensions = loaderDirectory ? [".pak", ".utoc", ".ucas"] : [".pak", ".sig"];
        foreach (var extension in extensions)
            await File.WriteAllTextAsync(Path.Combine(payload, stem + extension), extension, TestToken);
        var orphan = Directory.CreateDirectory(Path.Combine(collection, "incomplete")).FullName;
        await File.WriteAllTextAsync(Path.Combine(orphan, "missing.ucas"), "orphan", TestToken);
        var mixed = Directory.CreateDirectory(Path.Combine(collection, "mixed", "~mods")).FullName;
        await File.WriteAllTextAsync(Path.Combine(mixed, "mixed.pak"), "pak", TestToken);
        await File.WriteAllTextAsync(Path.Combine(mixed, "unknown.json"), "{}", TestToken);
        if (id == "third-game")
            await File.WriteAllTextAsync(Path.Combine(collection, "unattributed.pak"), "review", TestToken);
        var db = catalogDatabase = new ManagerDatabase(Path.Combine(home, "state", "manager.db"));
        await db.InitializeAsync(TestToken);
        var blobs = Path.Combine(home, "blobs");
        var scanner = new ModScanner(db, new BlobStore(blobs, db), new HashingService(), game);
        var intake = new GameModIntakeService(Manager, Path.Combine(Manager, "State"), () => [game]);
        var catalog = new CatalogService(db, scanner, installed, discoverChildren:
            (folder, ct) => intake.SelectableManagedFolders(folder, game, ct));
        await db.UpsertModAsync(new("existing", wrapper, "Saved wrapper", collection, false, 53), TestToken);

        await catalog.RefreshFoldersAsync(TestToken);
        var rows = await db.GetModsAsync(TestToken);
        Assert.Equal(2, rows.Count);
        var child = Assert.Single(rows, row => row.SourcePath == source);
        Assert.False(child.Enabled);
        Assert.Contains(name.Split('-')[0], child.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(53, Assert.Single(rows, row => row.Id == "existing").Priority);
        Assert.Empty(await db.GetModFilesAsync(TestToken));
        Assert.False(Directory.Exists(blobs) && Directory.EnumerateFiles(blobs, "*", SearchOption.AllDirectories).Any());

        await db.SetEnabledAndPriorityAsync(new Dictionary<string,(bool,int)> { [child.Id] = (true, 9) }, TestToken);
        await catalog.RefreshFoldersAsync(TestToken);
        var saved = Assert.Single(await db.GetModsAsync(TestToken), row => row.Id == child.Id);
        Assert.True(saved.Enabled);
        Assert.Equal(9, saved.Priority);
        await catalog.EnsureCapturedAsync([saved], TestToken);
        var indexed = await db.GetModFilesAsync(TestToken);
        Assert.Equal(extensions.Length, indexed.Count);
        foreach (var extension in extensions)
        {
            var target = "root/" + mount.TrimEnd('/') + (mount.EndsWith("~mods", StringComparison.OrdinalIgnoreCase) ? "/" : "/~mods/") + stem + extension;
            Assert.Contains(indexed, row => row.Path.Replace('\\', '/').Equals(target, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(extension, await File.ReadAllTextAsync(Path.Combine(payload, stem + extension), TestToken));
        }
        // An older catalog may already have captured the wrapper. Matching its child's
        // fingerprint must never archive the selected child's physical source as a side effect.
        await db.ReplaceModFilesAsync("existing", indexed.Select(file => file with { ModId = "existing" }).ToArray(), TestToken);
        var cleanup = new DuplicateCleanupService(db, Path.Combine(home, "archive"));
        Assert.Single((await cleanup.AnalyzeAsync(TestToken)).ExactDuplicates);
        Assert.Equal(0, await cleanup.ArchiveSafeAsync(TestToken));
        Assert.True(Directory.Exists(payload));
    }

    private GameModIntakeService Service() =>
        new(Manager, Path.Combine(Manager, "State"), () => [Game]);

    private string Managed(string name) => Path.Combine(Manager, "Mods by Game",
        "stellar-blade", "Installed", name);

    [Fact]
    public async Task Existing_deep_collection_is_split_into_independent_inbox_mods_and_replayed_without_touching_source()
    {
        var source = Managed("fav");
        var originals = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var name = $"Outfit-{i:D3}";
            var folder = Path.Combine(source, "Downloads", "Archive", "Outfits", name,
                "SB", "Content", "Paks", "~mods");
            Directory.CreateDirectory(folder);
            foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
            {
                var file = Path.Combine(folder, name + "_P" + extension);
                await File.WriteAllTextAsync(file, name + extension, TestToken);
                originals.Add(file);
            }
        }

        var service = Service();
        var preview = service.PreviewManagedCollection(source, Game, TestToken);
        Assert.Equal(12, preview.Candidates.Count(candidate => candidate.Kind == ModBoundaryKind.Folder));

        var first = await service.ImportManagedCollectionAsync(source, Game, TestToken);
        Assert.Equal(12, first.Routed);
        var inbox = Path.Combine(Manager, "Mods by Game", "stellar-blade", "Inbox");
        var packages = Directory.GetDirectories(inbox);
        Assert.Equal(12, packages.Length);
        Assert.All(packages, package =>
        {
            Assert.Equal(3, Directory.GetFiles(package, "*", SearchOption.AllDirectories).Length);
            Assert.True(Directory.Exists(Path.Combine(package, "SB", "Content", "Paks", "~mods")));
            Assert.False(Directory.Exists(Path.Combine(package, "Downloads")));
        });
        Assert.All(originals, original => Assert.True(File.Exists(original)));
        Assert.True(Directory.Exists(source));

        var replay = await service.ImportManagedCollectionAsync(source, Game, TestToken);
        Assert.Equal(0, replay.Routed);
        Assert.Equal(12, replay.AlreadyPresent);
        Assert.Equal(12, Directory.GetDirectories(inbox).Length);
    }

    [Fact]
    public async Task One_existing_mod_is_not_duplicated_or_published_as_a_collection()
    {
        var source = Managed("One mod");
        var payload = Path.Combine(source, "SB", "Content", "Paks", "~mods", "One_P.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        await File.WriteAllTextAsync(payload, "one", TestToken);
        var result = await Service().ImportManagedCollectionAsync(source, Game, TestToken);
        Assert.Equal(0, result.Routed);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(payload));
        Assert.False(Directory.Exists(Path.Combine(Manager, "Mods by Game", "stellar-blade", "Inbox")));
    }

    [Fact]
    public async Task Foreign_or_live_game_sources_cannot_use_the_managed_collection_exception()
    {
        var service = Service();
        var foreign = Path.Combine(home, "Not Managed", "Mods");
        Directory.CreateDirectory(foreign);
        var live = Path.Combine(Game.GameRoot, "SB", "Content", "Paks", "~mods");
        Directory.CreateDirectory(live);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportManagedCollectionAsync(foreign, Game, TestToken));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportManagedCollectionAsync(live, Game, TestToken));
        Assert.False(Directory.Exists(Path.Combine(Manager, "Mods by Game", "stellar-blade", "Inbox")));
    }

    [Fact]
    public void Mods_screen_exposes_an_opt_in_existing_collection_rescan()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "src",
            "MhwModManager.App", "MainWindow.xaml"));
        var vm = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.ManagedCollections.cs"));
        Assert.Contains("ReanalyzeManagedCollectionsCommand", xaml);
        Assert.Contains("PreviewManagedCollection", vm);
        Assert.Contains("ImportManagedCollectionAsync", vm);
        Assert.Contains("MessageBoxResult.Yes", vm);
    }

    public async ValueTask DisposeAsync()
    {
        if (catalogDatabase is not null)
        {
            await using var connection = await catalogDatabase.OpenAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }
}
