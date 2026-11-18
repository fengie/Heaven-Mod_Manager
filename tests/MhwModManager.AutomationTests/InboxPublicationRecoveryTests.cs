using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

/// <summary>
/// A process can terminate after directory publication but before the Inbox
/// original is archived. Its next startup must not create another package or
/// delete either copy on the basis of an unverified assumption.
/// </summary>
public sealed class InboxPublicationRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "umm-inbox-recovery-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public InboxPublicationRecoveryTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task CrashAfterPublishPreservesBothCopiesAndDoesNotImportAgain()
    {
        var (inbox, mods, source, destination, processed) = await CreateFixtureAsync("pending");
        var journal = new InboxPublicationJournalService(inbox, mods);
        var intent = await journal.BeginAsync(source, destination, processed, Token);

        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "published.txt"), "published", Token);

        var blocked = await journal.RecoverAsync(Token);
        Assert.Contains(source, blocked);
        Assert.True(File.Exists(intent));

        var db = await CreateDatabaseAsync("pending.db");
        var result = await CreateService(db, inbox, mods)
            .ProcessAsync(progress: null, refreshMetadata: false, ct: Token);

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Contains("review", Assert.Single(result.Items).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.EnumerateDirectories(mods));
        Assert.Equal("source", await File.ReadAllTextAsync(
            Path.Combine(source, "nativePC", "source.tex"), Token));
        Assert.Equal("published", await File.ReadAllTextAsync(
            Path.Combine(destination, "published.txt"), Token));
        Assert.True(File.Exists(intent));
    }

    [Fact]
    public async Task CrashBeforePublishCanReimportOnceAndClearIntent()
    {
        var (inbox, mods, source, destination, processed) = await CreateFixtureAsync("before");
        var journal = new InboxPublicationJournalService(inbox, mods);
        var intent = await journal.BeginAsync(source, destination, processed, Token);

        Assert.Empty(await journal.RecoverAsync(Token));
        Assert.False(File.Exists(intent));

        var db = await CreateDatabaseAsync("before.db");
        var first = await CreateService(db, inbox, mods)
            .ProcessAsync(progress: null, refreshMetadata: false, ct: Token);
        Assert.Equal(1, first.Imported);
        Assert.True(Directory.Exists(destination));
        Assert.False(Directory.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(
            ImportPublicationWorkspace.RootFor(mods), ".inbox-publish-*.pending.json"));

        var second = await CreateService(db, inbox, mods)
            .ProcessAsync(progress: null, refreshMetadata: false, ct: Token);
        Assert.Equal(0, second.Imported);
    }

    [Fact]
    public async Task CrashAfterSourceArchivePreservesPublishedModAndClearsIntent()
    {
        var (inbox, mods, source, destination, processed) = await CreateFixtureAsync("completed");
        var journal = new InboxPublicationJournalService(inbox, mods);
        var intent = await journal.BeginAsync(source, destination, processed, Token);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "published.txt"), "published", Token);
        Directory.CreateDirectory(Path.GetDirectoryName(processed)!);
        Directory.Move(source, processed);

        Assert.Empty(await journal.RecoverAsync(Token));
        Assert.False(File.Exists(intent));
        Assert.False(Directory.Exists(source));
        Assert.True(Directory.Exists(processed));
        Assert.Equal("published", await File.ReadAllTextAsync(
            Path.Combine(destination, "published.txt"), Token));
    }

    [Fact]
    public async Task IntentRejectsSourceOrDestinationOutsideOwnedRoots()
    {
        var (inbox, mods, source, destination, processed) = await CreateFixtureAsync("outside");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "do not touch", Token);
        var journal = new InboxPublicationJournalService(inbox, mods);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            journal.BeginAsync(source, outside, processed, Token));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            journal.BeginAsync(outside, destination, processed, Token));

        Assert.Equal("do not touch", await File.ReadAllTextAsync(sentinel, Token));
        Assert.True(Directory.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(
            ImportPublicationWorkspace.RootFor(mods), ".inbox-publish-*.pending.json"));
    }

    private async Task<(string Inbox, string Mods, string Source, string Destination, string Processed)>
        CreateFixtureAsync(string suffix)
    {
        var inbox = Path.Combine(root, suffix, "Inbox");
        var mods = Path.Combine(root, suffix, "Installed");
        var source = Path.Combine(inbox, "Armor");
        var destination = Path.Combine(mods, "Armor");
        var processed = Path.Combine(inbox, "Processed", "original-Armor");
        Directory.CreateDirectory(Path.Combine(source, "nativePC"));
        Directory.CreateDirectory(mods);
        await File.WriteAllTextAsync(Path.Combine(source, "nativePC", "source.tex"), "source", Token);
        return (inbox, mods, source, destination, processed);
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync(string file)
    {
        var db = new ManagerDatabase(Path.Combine(root, file));
        await db.InitializeAsync(Token);
        return db;
    }

    private SmartInboxService CreateService(ManagerDatabase db, string inbox, string mods)
    {
        var scanner = new ModScanner(db,
            new BlobStore(Path.Combine(root, "blobs"), db), new HashingService());
        var catalog = new CatalogService(db, scanner, mods);
        var nexus = new NexusMetadataService(db, new PlannerSnapshotRepository(db),
            Path.Combine(root, "state"));
        return new SmartInboxService(db, new ArchiveInspector(), catalog, nexus,
            new AutoCategoryService(db), inbox, mods);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
