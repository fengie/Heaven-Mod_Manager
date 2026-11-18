using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class StartupMaintenanceScaleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-maintenance-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public StartupMaintenanceScaleTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task LargeIndexedLibraryPreservesCategoriesAndIdempotentAssignments()
    {
        var db = await CreateDatabaseAsync("many-categories.db");
        const int modCount = 120;
        const int filesPerMod = 40;
        for (var i = 0; i < modCount; i++)
        {
            var id = $"mod-{i:D4}";
            await db.UpsertModAsync(new(id, id, id, Path.Combine(root, id), false, i,
                Category: i == 0 ? "Plugin" : null), Token);
            var files = Enumerable.Range(0, filesPerMod)
                .Select(j => new ModFileDescriptor(id, $"nativePC/pl/f_equip/asset{j:D3}.tex",
                    $"hash-{i}-{j}", null, 1, DateTimeOffset.UtcNow, FileClass.Texture))
                .ToArray();
            await db.ReplaceModFilesAsync(id, files, Token);
        }

        var service = new AutoCategoryService(db);
        Assert.Equal(modCount - 1, await service.AssignMissingAsync(Token));
        var categories = (await db.GetModsAsync(Token)).ToDictionary(m => m.Id, m => m.Category);
        Assert.Equal("Plugin", categories["mod-0000"]);
        Assert.All(categories.Where(p => p.Key != "mod-0000"),
            p => Assert.Equal(AutomationCategory.Armor.ToString(), p.Value));
        Assert.Equal(0, await service.AssignMissingAsync(Token));
    }

    [Fact]
    public async Task DuplicateArchivalReturnsExistingAnalysisWhenNothingCanBeRetired()
    {
        var db = await CreateDatabaseAsync("unchanged-duplicates.db");
        for (var i = 0; i < 2; i++)
        {
            var id = $"same-{i}";
            // No source folder exists: the safety rule must keep both records.
            await db.UpsertModAsync(new(id, id, id, Path.Combine(root, id), false, i), Token);
            await db.ReplaceModFilesAsync(id,
                [new ModFileDescriptor(id, @"nativePC\shared.tex", "same-sha", null,
                    10, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        }

        var service = new DuplicateCleanupService(db, Path.Combine(root, "archive"));
        var outcome = await service.ArchiveSafeWithAnalysisAsync(Token);
        Assert.Equal(0, outcome.Archived);
        Assert.Single(outcome.Analysis.ExactDuplicates);
        Assert.Equal(2, (await db.GetModsAsync(Token)).Count);
    }

    [Fact]
    public async Task DuplicateArchivalReturnsPostRetirementAnalysisAndPreservesKeeper()
    {
        var db = await CreateDatabaseAsync("retired-duplicates.db");
        for (var i = 0; i < 2; i++)
        {
            var id = $"retire-{i}";
            var folder = Path.Combine(root, id);
            Directory.CreateDirectory(folder);
            await db.UpsertModAsync(new(id, id, id, folder, false, i), Token);
            await db.ReplaceModFilesAsync(id,
                [new ModFileDescriptor(id, @"nativePC\shared.tex", "same-sha", null,
                    10, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        }

        var outcome = await new DuplicateCleanupService(db, Path.Combine(root, "archive-retired"))
            .ArchiveSafeWithAnalysisAsync(Token);
        Assert.Equal(1, outcome.Archived);
        Assert.Empty(outcome.Analysis.ExactDuplicates);
        Assert.Single(await db.GetModsAsync(Token));
        Assert.True(Directory.Exists(Path.Combine(root, "archive-retired")));
    }

    [Fact]
    public async Task InboxCanCompleteSafeImportWithoutOptionalNetworkMetadataRefresh()
    {
        var db = await CreateDatabaseAsync("offline-inbox.db");
        var inboxRoot = Path.Combine(root, "offline-inbox");
        var modsRoot = Path.Combine(root, "offline-mods");
        var stateRoot = Path.Combine(root, "offline-state");
        var original = Path.Combine(inboxRoot, "Armor");
        Directory.CreateDirectory(Path.Combine(original, "nativePC", "pl", "f_equip"));
        await File.WriteAllTextAsync(Path.Combine(original, "nativePC", "pl", "f_equip", "body.mod3"),
            "fixture", Token);

        var scanner = new ModScanner(db,
            new BlobStore(Path.Combine(stateRoot, "Blobs"), db), new HashingService());
        var catalog = new CatalogService(db, scanner, modsRoot);
        var nexus = new NexusMetadataService(db, new PlannerSnapshotRepository(db), stateRoot);
        var inbox = new SmartInboxService(db, new ArchiveInspector(), catalog, nexus,
            new AutoCategoryService(db), inboxRoot, modsRoot);

        var result = await inbox.ProcessAsync(progress: null, refreshMetadata: false, ct: Token);
        Assert.Equal(1, result.Imported);
        Assert.True(Directory.Exists(Path.Combine(modsRoot, "Armor")));
        Assert.True(Directory.Exists(Path.Combine(inboxRoot, "Processed")));
    }

    [Fact]
    public async Task DeferringAfterFirstInboxPackageFinishesIndexAndLeavesLaterSourceUntouched()
    {
        var db = await CreateDatabaseAsync("defer-inbox.db");
        var inboxRoot = Path.Combine(root, "defer-inbox");
        var modsRoot = Path.Combine(root, "defer-mods");
        var stateRoot = Path.Combine(root, "defer-state");
        foreach (var name in new[] { "A-Armor", "B-Armor" })
        {
            var folder = Path.Combine(inboxRoot, name, "nativePC", "pl", "f_equip");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "body.mod3"), name, Token);
        }

        var scanner = new ModScanner(db,
            new BlobStore(Path.Combine(stateRoot, "Blobs"), db), new HashingService());
        var catalog = new CatalogService(db, scanner, modsRoot);
        var nexus = new NexusMetadataService(db, new PlannerSnapshotRepository(db), stateRoot);
        var inbox = new SmartInboxService(db, new ArchiveInspector(), catalog, nexus,
            new AutoCategoryService(db), inboxRoot, modsRoot);

        var stop = false;
        var progress = new DirectProgress<InboxRunProgress>(state =>
        {
            if (state.Stage == "Finished item" && state.Completed == 1) stop = true;
        });
        var partial = await inbox.ProcessAsync(progress, refreshMetadata: false,
            shouldStopAfterCurrentItem: () => stop, ct: Token);

        Assert.Equal(2, partial.Found);
        Assert.Equal(1, partial.Imported);
        Assert.True(Directory.Exists(Path.Combine(modsRoot, "A-Armor")));
        Assert.True(Directory.Exists(Path.Combine(inboxRoot, "B-Armor")));
        Assert.False(Directory.Exists(Path.Combine(modsRoot, "B-Armor")));
        Assert.Single(await db.GetModsAsync(Token));

        var resumed = await inbox.ProcessAsync(progress: null, refreshMetadata: false,
            shouldStopAfterCurrentItem: null, ct: Token);
        Assert.Equal(1, resumed.Imported);
        Assert.Equal(2, (await db.GetModsAsync(Token)).Count);
        Assert.False(Directory.Exists(Path.Combine(inboxRoot, "B-Armor")));
    }

    [Fact]
    public async Task DeferringBeforeFirstPackagePreservesAllSourcesAndPublishesNothing()
    {
        var db = await CreateDatabaseAsync("defer-empty.db");
        var inboxRoot = Path.Combine(root, "defer-empty-inbox");
        var modsRoot = Path.Combine(root, "defer-empty-mods");
        var stateRoot = Path.Combine(root, "defer-empty-state");
        var folder = Path.Combine(inboxRoot, "A-Armor", "nativePC", "pl", "f_equip");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "body.mod3"), "content", Token);
        var scanner = new ModScanner(db,
            new BlobStore(Path.Combine(stateRoot, "Blobs"), db), new HashingService());
        var inbox = new SmartInboxService(db, new ArchiveInspector(),
            new CatalogService(db, scanner, modsRoot),
            new NexusMetadataService(db, new PlannerSnapshotRepository(db), stateRoot),
            new AutoCategoryService(db), inboxRoot, modsRoot);

        var partial = await inbox.ProcessAsync(progress: null, refreshMetadata: false,
            shouldStopAfterCurrentItem: () => true, ct: Token);

        Assert.Equal(1, partial.Found);
        Assert.Equal(0, partial.Imported);
        Assert.True(Directory.Exists(Path.Combine(inboxRoot, "A-Armor")));
        Assert.False(Directory.Exists(Path.Combine(modsRoot, "A-Armor")));
        Assert.Empty(await db.GetModsAsync(Token));
    }

    private sealed class DirectProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name));
        await db.InitializeAsync(Token);
        return db;
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
