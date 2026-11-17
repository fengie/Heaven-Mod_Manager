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
    public async Task Inbox_reports_each_package_and_post_import_indexing_in_order()
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
        var result = await service.ProcessAsync(TestToken, new ImmediateProgress<InboxRunProgress>(events.Add));

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
    public async Task Cancellation_from_progress_event_preserves_later_inbox_package()
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessAsync(cts.Token, progress));
        Assert.True(Directory.Exists(Path.Combine(mods, "A")));
        Assert.True(Directory.Exists(Path.Combine(inbox, "B")));
        Assert.False(Directory.Exists(Path.Combine(mods, "B")));
        Assert.DoesNotContain(events, p => p.Stage == "Finished");
    }

    [Fact]
    public async Task Archive_extraction_reports_actual_bytes_and_file_counts()
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
            new ArchiveExtractionLimits(4096, 4096), TestToken,
            new ImmediateProgress<ArchiveExtractionProgress>(events.Add));

        Assert.NotEmpty(events);
        var final = events[^1];
        Assert.Equal(2, final.CompletedFiles);
        Assert.Equal(2, final.TotalFiles);
        Assert.Equal(2048, final.CompletedBytes);
        Assert.Equal(2048, final.TotalBytes);
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
