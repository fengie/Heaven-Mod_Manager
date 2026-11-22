using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class SmartInboxProgressTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-inbox-progress-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InboxReportsEachPackageAndPostImportIndexingInOrder()
    {
        var inbox = Path.Combine(root, "Inbox");
        var mods = Path.Combine(root, "Mods");
        Directory.CreateDirectory(inbox);
        foreach (var name in new[] { "A", "B" })
        {
            var payload = Path.Combine(inbox, name, "nativePC");
            Directory.CreateDirectory(payload);
            await File.WriteAllTextAsync(Path.Combine(payload, name + ".tex"), name, TestToken);
        }

        var db = await CreateDatabaseAsync();
        var service = CreateService(db, inbox, mods);
        var events = new List<InboxRunProgress>();
        var result = await service.ProcessAsync(new ImmediateProgress<InboxRunProgress>(events.Add), TestToken);

        Assert.Equal(2, result.Imported);
        Assert.Contains(events, p => p.Stage == "Copying folder" && p.Completed == 0 && p.Total == 2);
        Assert.Contains(events, p => p.Stage == "Copying folder" && p.Completed == 1 && p.Total == 2);
        Assert.Contains(events, p => p.Stage == "Indexing catalog" && p.Completed == 2 && p.Total == 2);
        Assert.Contains(events, p => p.Stage == "Capturing imported mods");
        Assert.Contains(events, p => p.Stage == "Refreshing metadata");
        Assert.Contains(events, p => p.Stage == "Assigning categories");
        var final = events[^1];
        Assert.Equal("Finished", final.Stage);
        Assert.Equal(2, final.Completed);
        Assert.Equal(2, final.Total);
        Assert.True(Directory.Exists(Path.Combine(mods, "A")));
        Assert.True(Directory.Exists(Path.Combine(mods, "B")));
    }

    [Fact]
    public async Task CancellationFromProgressEventPreservesLaterInboxPackage()
    {
        var inbox = Path.Combine(root, "Inbox");
        var mods = Path.Combine(root, "Mods");
        Directory.CreateDirectory(inbox);
        foreach (var name in new[] { "A", "B" })
        {
            var payload = Path.Combine(inbox, name, "nativePC");
            Directory.CreateDirectory(payload);
            await File.WriteAllTextAsync(Path.Combine(payload, name + ".tex"), name, TestToken);
        }

        var db = await CreateDatabaseAsync();
        var service = CreateService(db, inbox, mods);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var events = new List<InboxRunProgress>();
        var progress = new ImmediateProgress<InboxRunProgress>(p =>
        {
            events.Add(p);
            if (p.Stage == "Finished item" && p.Completed == 1) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessAsync(progress, cts.Token));
        Assert.True(Directory.Exists(Path.Combine(mods, "A")));
        Assert.True(Directory.Exists(Path.Combine(inbox, "B")));
        Assert.False(Directory.Exists(Path.Combine(mods, "B")));
        Assert.DoesNotContain(events, p => p.Stage == "Finished");
    }

    [Fact]
    public async Task ArchiveExtractionReportsActualBytesAndFileCounts()
    {
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "two.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "a", "b" })
            {
                await using var output = zip.CreateEntry(name + ".txt", CompressionLevel.NoCompression).Open();
                await output.WriteAsync(new byte[1024], TestToken);
            }
        }
        var events = new List<ArchiveExtractionProgress>();
        await new ArchiveInspector().ExtractSafelyAsync(archive, Path.Combine(root, "output"), root,
            new ArchiveExtractionLimits(4096, 4096), new ImmediateProgress<ArchiveExtractionProgress>(events.Add), TestToken);

        Assert.NotEmpty(events);
        var final = events[^1];
        Assert.Equal(2, final.CompletedFiles);
        Assert.Equal(2, final.TotalFiles);
        Assert.Equal(2048, final.CompletedBytes);
        Assert.Equal(2048, final.TotalBytes);
    }

    [Fact]
    public async Task ActiveInboxReadinessIgnoresProcessedAndEmptyFoldersAndIndexesAnOrdinaryDropOnce()
    {
        var inbox = Path.Combine(root, "Inbox");
        var mods = Path.Combine(root, "Installed");
        Directory.CreateDirectory(mods);
        var processed = Directory.CreateDirectory(Path.Combine(inbox, "Processed")).FullName;
        await File.WriteAllTextAsync(Path.Combine(processed, "previous.zip"), "previous", TestToken);
        var empty = Directory.CreateDirectory(Path.Combine(inbox, "empty")).FullName;
        await File.WriteAllTextAsync(Path.Combine(inbox, "downloading.tmp"), "partial", TestToken);
        var db = await CreateDatabaseAsync();
        var service = CreateService(db, inbox, mods);
        Assert.False(service.HasPendingPackages());

        var submitted = Path.Combine(inbox, "Example.zip");
        using (var zip = ZipFile.Open(submitted, ZipArchiveMode.Create))
        using (var output = zip.CreateEntry("nativePC/example.bin").Open())
            output.WriteByte(42);
        var original = await File.ReadAllBytesAsync(submitted, TestToken);
        Assert.True(service.HasPendingPackages());
        var result = await service.ProcessAsync(null, refreshMetadata: false, TestToken);
        Assert.Equal(1, result.Imported);
        var mod = Assert.Single(await db.GetModsAsync(TestToken));
        Assert.False(mod.Enabled);
        Assert.True(Directory.Exists(empty));
        var retained = Assert.Single(Directory.GetFiles(processed, "*Example.zip", SearchOption.AllDirectories));
        Assert.Equal(original, await File.ReadAllBytesAsync(retained, TestToken));
        Assert.False(service.HasPendingPackages());
        Assert.Equal(0, (await service.ProcessAsync(null, refreshMetadata: false, TestToken)).Imported);
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync()
    {
        var db = new ManagerDatabase(Path.Combine(root, "state", "manager.db"));
        await db.InitializeAsync(TestToken);
        return db;
    }

    private SmartInboxService CreateService(ManagerDatabase db, string inbox, string mods)
    {
        var hash = new HashingService();
        var blobs = new BlobStore(Path.Combine(root, "blobs"), db);
        var scanner = new ModScanner(db, blobs, hash);
        var catalog = new CatalogService(db, scanner, mods);
        var nexus = new NexusMetadataService(db, new PlannerSnapshotRepository(db), Path.Combine(root, "state"));
        return new SmartInboxService(db, new ArchiveInspector(), catalog, nexus, new AutoCategoryService(db), inbox, mods);
    }

    private sealed class ImmediateProgress<T>(Action<T> notify) : IProgress<T>
    {
        public void Report(T value) => notify(value);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
