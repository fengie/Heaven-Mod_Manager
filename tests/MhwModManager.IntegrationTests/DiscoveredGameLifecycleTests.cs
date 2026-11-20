using System.IO.Compression;
using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DiscoveredGameLifecycleTests(ITestOutputHelper output)
{
    public static TheoryData<string> ProfileIds => new(DiscoveredGameProfiles.Load().Select(game => game.Id));

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public async Task CatalogDestinationsAndSaveSnapshotFollowThisGame(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = Directory.CreateTempSubdirectory("heaven-capabilities-");
        var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(profile => profile.Id == id),workspace.FullName);
        var state = Path.Combine(workspace.FullName,"State");
        var db = new ManagerDatabase(Path.Combine(state,"manager.db"));
        try
        {
            Assert.Equal(game.HasNexusIntegration,game.NexusBrowseUri is not null);
            Assert.Equal(game.HasGameBananaIntegration,game.GameBananaBrowseUri is not null);
            if (game.NexusBrowseUri is { } nexus)
            {
                Assert.Equal("www.nexusmods.com",nexus.Host);
                Assert.Equal("/"+game.NexusGameDomain+"/mods",nexus.AbsolutePath);
            }
            if (game.GameBananaBrowseUri is { } banana)
            {
                Assert.Equal("gamebanana.com",banana.Host);
                Assert.Equal(FormattableString.Invariant($"/games/{game.GameBananaGameId}"),banana.AbsolutePath);
            }
            Assert.Null((game with { NexusGameDomain = "../other/game" }).NexusBrowseUri);
            Assert.Null((game with { GameBananaGameId = -1 }).GameBananaBrowseUri);
            await db.InitializeAsync(ct);
            var knownSave = game.SavePath ?? Path.Combine(workspace.FullName, "configured-save.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(knownSave)!);
            await File.WriteAllTextAsync(knownSave,id,ct);
            var archive = await new SaveBackupService(db,state,game with { SavePath=knownSave }).CreateAsync("profile coverage",ct);
            Assert.True(archive.Success);
            Assert.Equal(knownSave,archive.SaveSource);
            Assert.Equal(id,await File.ReadAllTextAsync(Path.Combine(archive.SnapshotRoot,"save",Path.GetFileName(knownSave)),ct));
            output.WriteLine($"{id}: Nexus {(game.NexusBrowseUri is null ? "unavailable" : game.NexusBrowseUri.AbsoluteUri)}; GameBanana {(game.GameBananaBrowseUri is null ? "unavailable" : game.GameBananaBrowseUri.AbsoluteUri)}. Local save backup read back from isolated configured save; real provider network and real save bytes untested.");
        }
        finally
        {
            if (File.Exists(db.DatabasePath))
            {
                await using var connection = await db.OpenAsync(CancellationToken.None);
                SqliteConnection.ClearPool(connection);
            }
            workspace.Delete(true);
        }
    }

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public async Task ImportPopulatePreviewApplyVerifyDisableAndUndo(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = Directory.CreateTempSubdirectory("heaven-profile-");
        var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(record => record.Id == id), workspace.FullName);
        var state = Path.Combine(workspace.FullName,"state");
        var db = new ManagerDatabase(Path.Combine(state,"manager.db"));
        try
        {
            var adapter = GameAdapters.Resolve(game);
            Assert.Contains(adapter.Validate(game), item => item.Blocking && item.Code == "missing-executable");
            Directory.CreateDirectory(Path.GetDirectoryName(game.ExecutablePath)!);
            await File.WriteAllTextAsync(game.ExecutablePath,"inert fixture",ct);
            Assert.DoesNotContain(adapter.Validate(game), item => item.Blocking);
            var registry = new GameProfileRegistry(state);
            registry.Upsert(game);
            registry.SetActive(id);
            Assert.Equal(id,registry.GetActive()!.Id);
            await db.InitializeAsync(ct);
            var manager = Path.Combine(workspace.FullName,"Manager");
            var storage = GameModStorage.Resolve(manager,game);
            var blobs = new BlobStore(Path.Combine(state,"blobs"),db);
            var hashing = new HashingService();
            var scanner = new ModScanner(db,blobs,hashing,game);
            var archives = new ArchiveInspector();
            var intake = new GameModIntakeService(manager,state,() => [game],archives);
            var catalog = new CatalogService(db,scanner,storage.InstalledRoot,storage.LegacyInstalledRoot,
                game.AdapterId == "unreal-paks" ? (dir, token) => intake.SelectableManagedFolders(dir,game,token) : null,
                game.AdapterId == "unreal-paks" ? (dir, token) => intake.InspectManagedShelf(dir,game,token) : null);
            var snapshots = new PlannerSnapshotRepository(db);
            var planning = new DeploymentPlanner(new ConflictEngine(game),game);
            var dependencies = new DependencyDoctorService(db,game.GameRoot,game);
            var executor = new DeploymentExecutor(db,blobs,hashing,game.GameRoot);
            var suffix = game.AdapterId == "unreal-paks" ? ".pak" : ".bin";
            var inboxSource = Directory.CreateDirectory(Path.Combine(storage.InboxRoot,"Inbox package")).FullName;
            await File.WriteAllTextAsync(Path.Combine(inboxSource,"first"+suffix),"inbox",ct);
            var inbox = new SmartInboxService(db,archives,catalog,new NexusMetadataService(db,snapshots,state,game),
                new AutoCategoryService(db),storage.InboxRoot,storage.InstalledRoot);
            Assert.Equal(1,(await inbox.ProcessAsync(null,false,ct)).Imported);
            Assert.Equal(0,(await inbox.ProcessAsync(null,false,ct)).Imported);
            var zip = Path.Combine(workspace.FullName,"Second package.zip");
            using (var archive = ZipFile.Open(zip,ZipArchiveMode.Create))
            {
                await using var stream = archive.CreateEntry("second"+suffix).Open();
                await stream.WriteAsync("archive"u8.ToArray(),ct);
            }
            var originalArchive = await File.ReadAllBytesAsync(zip,ct);
            var importer = new ArchiveImportService(archives,catalog,storage.InstalledRoot);
            await importer.ImportAsync(zip,ct);
            await catalog.RefreshFoldersAsync(ct);
            var selected = await new AutoPopulateService(snapshots,planning,dependencies,game.GameRoot,game,blobs,scanner).BuildAsync(ct);
            Assert.Equal(2,selected.EnabledMods);
            var stage = selected.State.ToDictionary(row => row.Key,row => (row.Value.Enabled,row.Value.Priority),PathRules.Comparer);
            var snapshot = await snapshots.LoadAsync(ct);
            Assert.All(snapshot.Mods, mod => Assert.False(mod.Enabled));
            var plan = planning.Build(snapshot with { Mods = snapshot.Mods.Select(mod => mod with { Enabled = stage[mod.Id].Enabled }).ToArray() });
            Assert.False(plan.IsBlocked);
            Assert.Equal(2,plan.Changes.Count);
            Assert.All(plan.Changes, operation => Assert.False(File.Exists(executor.Destination(operation.Path))));
            var saved = new ProfileRepository(db);
            var profileId = await saved.SaveAsync("Coverage",stage,ct:ct);
            var restored = await saved.LoadAsync(profileId,ct);
            Assert.Equal(stage.Count,restored.Count);
            foreach (var entry in stage) Assert.Equal(entry.Value,restored[entry.Key]);
            Assert.True((await executor.ApplyAsync(plan,"profile coverage",stage,ct:ct)).Success);
            var manifest = (await snapshots.LoadAsync(ct)).CurrentManifest;
            Assert.Equal(2,manifest.Count);
            var mount = game.AdapterId == "unreal-paks" && game.ModRootRelativePath.EndsWith(@"Content\Paks",StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(game.LiveModRoot,"~mods") : game.LiveModRoot;
            Assert.Equal("inbox",await File.ReadAllTextAsync(Path.Combine(mount,"first"+suffix),ct));
            Assert.Equal("archive",await File.ReadAllTextAsync(Path.Combine(mount,"second"+suffix),ct));
            var health = new HealthService(db,snapshots,hashing,blobs,executor.Destination,game);
            Assert.DoesNotContain(await health.ScanAsync(ct), issue => issue.Code is "DEPLOYED_MISSING" or "DEPLOYED_CHANGED");
            snapshot = await snapshots.LoadAsync(ct);
            var disabled = snapshot.Mods.Select(mod => mod with { Enabled=false }).ToArray();
            var remove = planning.Build(snapshot with { Mods=disabled });
            Assert.False(remove.IsBlocked);
            Assert.True((await executor.ApplyAsync(remove,"disable",disabled.ToDictionary(mod => mod.Id,mod => (false,mod.Priority)),ct:ct)).Success);
            Assert.All(manifest.Keys,path => Assert.False(File.Exists(executor.Destination(path))));
            Assert.True((await executor.UndoLastAsync(ct)).Success);
            Assert.Equal("archive",await File.ReadAllTextAsync(Path.Combine(mount,"second"+suffix),ct));
            Assert.Equal(originalArchive,await File.ReadAllBytesAsync(zip,ct));
            output.WriteLine($"{id} ({game.AdapterId}, {game.Store ?? "manual"}): register, select, missing executable, Inbox/replay, ZIP import, catalog, Auto Populate, preview, save/load selections, apply, manifest, health, disable, undo, unchanged source verified in isolated directories.");
        }
        finally
        {
            if (File.Exists(db.DatabasePath))
            {
                await using var connection = await db.OpenAsync(CancellationToken.None);
                SqliteConnection.ClearPool(connection);
            }
            workspace.Delete(true);
        }
    }
}
