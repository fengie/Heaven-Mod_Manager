using System.IO.Compression;
using System.Security.Cryptography;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// One ordered, real-service acceptance, not a mock of its constituent methods.
/// Every package, catalog, journal, database and game tree is disposable and owned
/// by this test. Source archives represent mod layouts, not commercial game mods.
/// </summary>
public sealed class OrderedProductLearningCycleAcceptanceTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "umm-ordered-cycle-" + Guid.NewGuid().ToString("N"));

    public OrderedProductLearningCycleAcceptanceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task IndependentArchivesRouteByGameRejectUnresolvedChoicesThenApplyAndUndoOnlySelectedFiles()
    {
        var ct = TestToken;
        var managerRoot = Path.Combine(root, "Manager");
        var stateRoot = Path.Combine(managerRoot, "State");
        var mhw = GameProfile.MonsterHunterWorld(Path.Combine(root, "Games", "MHW"));
        var quartz = GameProfile.Generic("quartz", "Quartz", Path.Combine(root, "Games", "Quartz"),
            "Quartz.exe", @"QuartzProject\Content\Paks", "unreal-paks");
        Directory.CreateDirectory(mhw.GameRoot);
        Directory.CreateDirectory(quartz.GameRoot);
        var mhwPaths = GameModStorage.Resolve(managerRoot, mhw);
        var quartzPaths = GameModStorage.Resolve(managerRoot, quartz);
        var foreign = Path.Combine(mhw.GameRoot, "foreign-user-file.bin");
        byte[] sentinel = [0x51, 0xA8, 0x21];
        await File.WriteAllBytesAsync(foreign, sentinel, ct);

        // Independent download roots and titles model arrivals from distinct
        // sources/times. No registration, staging or source pruning is implied.
        var original = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        async Task<string> Archive(string folder, string title, string member, byte[] bytes)
        {
            var source = Path.Combine(root, "Untrusted downloads", folder);
            Directory.CreateDirectory(source);
            var file = Path.Combine(source, title + ".zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                await using var stream = zip.CreateEntry(member, CompressionLevel.NoCompression).Open();
                await stream.WriteAsync(bytes, ct);
            }
            original.Add(file, await File.ReadAllBytesAsync(file, ct));
            return file;
        }

        const string outfit = "nativePC/pl/f_equip/pl032_0010/wst/mod/f_wst032_0010.mod3";
        const string baseTexture = "nativePC/pl/armor/base.tex";
        const string lanternTexture = "nativePC/pl/weapon/lantern.tex";
        const string quartzPak = "QuartzProject/Content/Paks/~mods/quartz-outfit.pak";
        byte[] baseBytes = [0x10, 0x11, 0x12];
        byte[] redBytes = [0x21, 0x22, 0x23];
        byte[] blueBytes = [0x31, 0x32, 0x33];
        byte[] lanternBytes = [0x41, 0x42, 0x43];
        byte[] quartzBytes = [0x51, 0x52, 0x53];
        var mainZip = await Archive("older", "MHW Expedition Main", baseTexture, baseBytes);
        var redZip = await Archive("newer-red", "MHW Expedition Red", outfit, redBytes);
        var blueZip = await Archive("newer-blue", "MHW Expedition Blue", outfit, blueBytes);
        var lanternZip = await Archive("separate-addon", "MHW Lantern", lanternTexture, lanternBytes);
        var quartzZip = await Archive("other-game", "Quartz Outfit", quartzPak, quartzBytes);
        var inputs = new[] { mainZip, redZip, blueZip, lanternZip, quartzZip };

        var inspector = new ArchiveInspector();
        var intake = new GameModIntakeService(managerRoot, stateRoot, () => [mhw, quartz], inspector);
        var routed = await intake.ImportPathsAsync(inputs, ct: ct);
        Assert.Equal(5, routed.Routed);
        Assert.Equal(0, routed.NeedsReview);
        foreach (var file in inputs.Take(4))
            Assert.Contains(routed.Items, item => item.SourcePath == file &&
                item.GameId == mhw.Id && item.Status == GameModIntakeStatus.Routed);
        Assert.Contains(routed.Items, item => item.SourcePath == quartzZip &&
            item.GameId == quartz.Id && item.Status == GameModIntakeStatus.Routed);
        Assert.Equal(4, Directory.GetFiles(mhwPaths.InboxRoot, "*.zip").Length);
        Assert.Single(Directory.GetFiles(quartzPaths.InboxRoot, "*.zip"));

        var mhwDb = new ManagerDatabase(Path.Combine(root, "Databases", "mhw.db"));
        var quartzDb = new ManagerDatabase(Path.Combine(root, "Databases", "quartz.db"));
        await mhwDb.InitializeAsync(ct);
        await quartzDb.InitializeAsync(ct);

        (BlobStore Blobs, HashingService Hashing, CatalogService Catalog, SmartInboxService Inbox) Pipeline(
            ManagerDatabase db, GameProfile game, GameModStoragePaths paths)
        {
            var hashing = new HashingService();
            var blobs = new BlobStore(Path.Combine(root, "Blobs", game.Id), db);
            var scanner = new ModScanner(db, blobs, hashing, game);
            var catalog = new CatalogService(db, scanner, paths.InstalledRoot, paths.LegacyInstalledRoot);
            var metadata = new NexusMetadataService(db, new PlannerSnapshotRepository(db),
                Path.Combine(root, "Metadata", game.Id), game);
            return (blobs, hashing, catalog,
                new SmartInboxService(db, inspector, catalog, metadata, new AutoCategoryService(db),
                    paths.InboxRoot, paths.InstalledRoot, legacyInboxRoot: paths.LegacyInboxRoot));
        }

        var mhwPipeline = Pipeline(mhwDb, mhw, mhwPaths);
        var quartzPipeline = Pipeline(quartzDb, quartz, quartzPaths);
        Assert.Equal(4, (await mhwPipeline.Inbox.ProcessAsync(ct)).Imported);
        Assert.Equal(1, (await quartzPipeline.Inbox.ProcessAsync(ct)).Imported);
        var mhwMods = await mhwDb.GetModsAsync(ct);
        var quartzMod = Assert.Single(await quartzDb.GetModsAsync(ct));
        Assert.Equal(4, mhwMods.Count);
        Assert.All(mhwMods, mod => Assert.False(mod.Enabled));
        Assert.False(quartzMod.Enabled);
        Assert.Equal("Quartz Outfit", quartzMod.DisplayName);
        await mhwPipeline.Catalog.EnsureCapturedAsync(mhwMods, ct);
        await quartzPipeline.Catalog.EnsureCapturedAsync([quartzMod], ct);

        ModDescriptor Named(string name) => Assert.Single(mhwMods, mod => mod.DisplayName == name);
        var main = Named("MHW Expedition Main");
        var red = Named("MHW Expedition Red");
        var blue = Named("MHW Expedition Blue");
        var lantern = Named("MHW Lantern");

        // Unrelated folders must not collide. The two deliberately different-byte
        // outfit providers are explicitly reviewed as one mutually exclusive choice.
        var familyId = await mhwDb.ChainManualFamilyAsync(main.Id, [main.Id],
            [[red.Id, blue.Id]], "Expedition outfit choices", ct);
        var snapshot = await new PlannerSnapshotRepository(mhwDb).LoadAsync(ct);
        var family = Assert.Single(LogicalModFamilies.Build(snapshot.Mods, snapshot.Files, mhw),
            candidate => candidate.Members.Any(member => member.FamilyId == familyId));
        Assert.Equal(3, family.Members.Count);
        Assert.Contains(snapshot.Mods, mod => mod.Id == lantern.Id && mod.FamilyId != familyId);

        var planner = new DeploymentPlanner(new ConflictEngine(mhw), mhw);
        var executor = new DeploymentExecutor(mhwDb, mhwPipeline.Blobs, mhwPipeline.Hashing, mhw.GameRoot);
        Dictionary<string, (bool enabled, int priority)> Stage(params string[] enabled) =>
            snapshot.Mods.ToDictionary(mod => mod.Id,
                mod => (enabled: enabled.Contains(mod.Id), priority: mod.Priority),
                StringComparer.OrdinalIgnoreCase);

        var invalid = Stage(main.Id, red.Id, blue.Id, lantern.Id);
        var blocked = planner.Build(WorkflowAnalysis.WithState(snapshot, invalid));
        Assert.True(blocked.IsBlocked);
        Assert.Contains(blocked.Conflicts, conflict => conflict.Blocking &&
            conflict.ReasonCode == "exclusive-family-choice");
        Assert.False((await executor.ApplyAsync(blocked, "Unresolved outfit alternatives", ct: ct)).Success);

        var selected = Stage(main.Id, red.Id, lantern.Id);
        var preview = planner.Build(WorkflowAnalysis.WithState(snapshot, selected));
        Assert.False(preview.IsBlocked, string.Join(" | ", preview.Conflicts.Select(c => c.ReasonCode)));
        Assert.Equal(3, preview.Changes.Count);
        Assert.All(preview.Changes, change => Assert.Equal(ChangeKind.Add, change.Kind));
        var baseTarget = Path.Combine(mhw.GameRoot, baseTexture.Replace('/', Path.DirectorySeparatorChar));
        var outfitTarget = Path.Combine(mhw.GameRoot, outfit.Replace('/', Path.DirectorySeparatorChar));
        var lanternTarget = Path.Combine(mhw.GameRoot, lanternTexture.Replace('/', Path.DirectorySeparatorChar));
        var quartzTarget = Path.Combine(quartz.GameRoot, quartzPak.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(File.Exists(baseTarget) || File.Exists(outfitTarget) ||
            File.Exists(lanternTarget) || File.Exists(quartzTarget));
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(foreign, ct));

        Assert.True((await executor.ApplyAsync(preview, "Selected outfit and unrelated mod", selected, ct: ct)).Success);
        Assert.Equal(baseBytes, await File.ReadAllBytesAsync(baseTarget, ct));
        Assert.Equal(redBytes, await File.ReadAllBytesAsync(outfitTarget, ct));
        Assert.Equal(lanternBytes, await File.ReadAllBytesAsync(lanternTarget, ct));
        Assert.False(File.Exists(quartzTarget));
        Assert.Equal(3, (await new PlannerSnapshotRepository(mhwDb).LoadAsync(ct)).CurrentManifest.Count);
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(foreign, ct));

        Assert.True((await executor.UndoLastAsync(ct)).Success);
        Assert.False(File.Exists(baseTarget) || File.Exists(outfitTarget) || File.Exists(lanternTarget));
        Assert.Empty((await new PlannerSnapshotRepository(mhwDb).LoadAsync(ct)).CurrentManifest);
        Assert.All(await mhwDb.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
        Assert.False(File.Exists(quartzTarget));
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(foreign, ct));
        foreach (var (source, bytes) in original)
            Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(source, ct)));

        var repeat = await intake.ImportPathsAsync(inputs, ct: ct);
        Assert.Equal(5, repeat.AlreadyPresent);
        Assert.Equal(0, repeat.Routed);
        await using (var mhwConnection = await mhwDb.OpenAsync(ct))
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(mhwConnection);
        await using (var quartzConnection = await quartzDb.OpenAsync(ct))
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(quartzConnection);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
