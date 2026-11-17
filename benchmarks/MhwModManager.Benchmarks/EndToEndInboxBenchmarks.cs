using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Benchmarks;

/// <summary>
/// Full synthetic ZIP processing through SmartInbox, indexing and capture.
/// Fresh test-owned state for every iteration prevents replay from hiding work.
/// </summary>
[MemoryDiagnoser]
public class EndToEndInboxBenchmarks
{
    [Params(8, 64)]
    public int ArchiveCount { get; set; }

    [Params(4, 1024)]
    public int PayloadKiB { get; set; }

    private string root = "";
    private string source = "";
    private string iterationRoot = "";
    private ManagerDatabase database = null!;
    private SmartInboxService inbox = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        root = Path.Combine(Path.GetTempPath(), "umm-inbox-perf-" + Guid.NewGuid().ToString("N"));
        source = Path.Combine(root, "templates");
        Directory.CreateDirectory(source);
        var bytes = new byte[PayloadKiB * 1024];
        new Random(735).NextBytes(bytes);
        for (var i = 0; i < ArchiveCount; i++)
        {
            using var zip = ZipFile.Open(Path.Combine(source, $"Mod-{i:D4}.zip"), ZipArchiveMode.Create);
            using var entry = zip.CreateEntry($"nativePC/pl/armor/file-{i:D4}.tex", CompressionLevel.NoCompression).Open();
            entry.Write(bytes);
        }
    }

    [IterationSetup]
    public void PrepareFreshInbox()
    {
        iterationRoot = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N"));
        var game = GameProfile.MonsterHunterWorld(Path.Combine(iterationRoot, "Game"));
        var paths = GameModStorage.Resolve(iterationRoot, game);
        Directory.CreateDirectory(paths.InboxRoot);
        foreach (var original in Directory.EnumerateFiles(source, "*.zip"))
            File.Copy(original, Path.Combine(paths.InboxRoot, Path.GetFileName(original)));
        database = new ManagerDatabase(Path.Combine(iterationRoot, "Db", "manager.db"));
        database.InitializeAsync().GetAwaiter().GetResult();
        var archives = new ArchiveInspector();
        var scanner = new ModScanner(database,
            new BlobStore(Path.Combine(iterationRoot, "Blobs"), database), new HashingService(), game);
        var catalog = new CatalogService(database, scanner, paths.InstalledRoot);
        var nexus = new NexusMetadataService(database, new PlannerSnapshotRepository(database),
            Path.Combine(iterationRoot, "State"), game);
        inbox = new SmartInboxService(database, archives, catalog, nexus,
            new AutoCategoryService(database), paths.InboxRoot, paths.InstalledRoot);
    }

    [Benchmark]
    public async Task<int> InstallIndexAndCaptureFreshInboxAsync()
    {
        var imported = await inbox.ProcessAsync();
        var indexed = await database.GetModsAsync();
        if (imported.Imported != ArchiveCount || indexed.Count != ArchiveCount)
            throw new InvalidOperationException(
                $"Inbox accepted {imported.Imported}/{ArchiveCount}; indexed {indexed.Count}. A partial run is not a benchmark.");
        return indexed.Count;
    }

    [IterationCleanup]
    public void CleanupFreshInbox()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(iterationRoot)) Directory.Delete(iterationRoot, true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
