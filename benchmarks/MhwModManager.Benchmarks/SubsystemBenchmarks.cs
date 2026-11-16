using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Benchmarks;

/// <summary>Pure mod-boundary detection at realistic package counts, without disk noise.</summary>
[MemoryDiagnoser]
public class ModBoundaryDiscoveryBenchmarks
{
    [Params(100, 1000, 10000)]
    public int PackageCount { get; set; }

    private string[] inventory = [];

    [GlobalSetup]
    public void Setup() =>
        inventory = Enumerable.Range(0, PackageCount)
            .Select(index => $"Downloads/Collection/Mod{index:D5}.zip").ToArray();

    [Benchmark]
    public ModBoundaryResult Discover() => ModBoundaryDiscovery.Discover(inventory);
}

/// <summary>Game attribution cost with a late positive signature in a large manifest.</summary>
[MemoryDiagnoser]
public class GameClassificationBenchmarks
{
    [Params(100, 1000, 10000)]
    public int FileCount { get; set; }

    private string[] inventory = [];
    private GameProfile[] games = [];

    [GlobalSetup]
    public void Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "umm-classifier-synthetic");
        games =
        [
            GameProfile.MonsterHunterWorld(Path.Combine(root, "mhw")),
            GameProfile.Generic("nier-automata", "NieR:Automata",
                Path.Combine(root, "nier"), "NieRAutomata.exe", "mods")
        ];
        inventory = Enumerable.Range(0, FileCount - 1)
            .Select(index => $"assets/textures/{index:D6}.dds")
            .Append("nativePC/pl/armor/identified.tex").ToArray();
    }

    [Benchmark]
    public GameModIntakeDetection Classify() =>
        GameModIntakeClassifier.Classify(games, "MHW Armor.zip", inventory);
}

/// <summary>Real ZIP header inspection and fully bounded extraction (including cleanup).</summary>
[MemoryDiagnoser]
public class ArchivePipelineBenchmarks
{
    [Params(32, 512)]
    public int EntryCount { get; set; }

    private readonly ArchiveInspector inspector = new();
    private string root = "";
    private string archivePath = "";

    [GlobalSetup]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "umm-archive-benchmark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        archivePath = Path.Combine(root, "synthetic.zip");
        var content = new byte[4096];
        new Random(4207).NextBytes(content);
        using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        for (var i = 0; i < EntryCount; i++)
        {
            using var entry = zip.CreateEntry($"nativePC/pl/armor/part{i:D6}.tex").Open();
            entry.Write(content);
        }
    }

    [Benchmark]
    public ArchiveInspection InspectArchive() => inspector.Inspect(archivePath);

    [Benchmark]
    public async Task<int> ExtractArchiveAndCleanup()
    {
        var destination = Path.Combine(root, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            await inspector.ExtractSafelyAsync(archivePath, destination, root);
            return Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length;
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

/// <summary>Compare the fast change detector with the authoritative two-pass SHA-256 path.</summary>
[MemoryDiagnoser]
public class FileHashingBenchmarks
{
    [Params(1, 8)]
    public int InputMiB { get; set; }

    private readonly HashingService hashing = new();
    private string root = "";
    private string source = "";

    [GlobalSetup]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "umm-hash-benchmark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        source = Path.Combine(root, "synthetic-mod.bin");
        var bytes = new byte[InputMiB * 1024 * 1024];
        new Random(731).NextBytes(bytes);
        File.WriteAllBytes(source, bytes);
    }

    [Benchmark(Baseline = true)]
    public Task<HashResult> FastHash() => hashing.HashFileAsync(source, authoritative: false);

    [Benchmark]
    public Task<HashResult> AuthoritativeHash() => hashing.HashFileAsync(source, authoritative: true);

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

/// <summary>Warm SQLite library read cost after seeding isolated synthetic mod records.</summary>
[MemoryDiagnoser]
public class ModDatabaseBenchmarks
{
    [Params(100, 1000)]
    public int ModCount { get; set; }

    private string root = "";
    private ManagerDatabase database = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "umm-db-benchmark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        database = new ManagerDatabase(Path.Combine(root, "manager.db"));
        await database.InitializeAsync();
        for (var i = 0; i < ModCount; i++)
        {
            var id = $"bench-{i:D5}";
            await database.UpsertModAsync(new ModDescriptor(id, id, id,
                Path.Combine(root, "mods", id), false, i));
        }
    }

    [Benchmark]
    public Task<IReadOnlyList<ModDescriptor>> LoadModRows() => database.GetModsAsync();

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

/// <summary>
/// End-to-end bulk intake into isolated game-scoped Inboxes. Each invocation uses
/// fresh manager state, so repeat deduplication cannot silently replace the workload.
/// Includes safe publication and owned scratch cleanup in the measured time.
/// </summary>
[MemoryDiagnoser]
public class BulkArchiveIntakeBenchmarks
{
    [Params(8, 64)]
    public int ArchiveCount { get; set; }

    private string root = "";
    private string downloads = "";
    private GameProfile[] games = [];

    [GlobalSetup]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "umm-bulk-benchmark-" + Guid.NewGuid().ToString("N"));
        downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        games =
        [
            GameProfile.MonsterHunterWorld(Path.Combine(root, "games", "MHW")),
            GameProfile.Generic("nier-automata", "NieR:Automata",
                Path.Combine(root, "games", "NieR"), "NieRAutomata.exe", "mods")
        ];
        for (var i = 0; i < ArchiveCount; i++)
        {
            var mhw = i % 2 == 0;
            var file = Path.Combine(downloads,
                mhw ? $"MHW Gear {i:D4}.zip" : $"NieR Automata Outfit {i:D4}.zip");
            var entryName = mhw
                ? $"nativePC/pl/armor/armor{i:D4}.tex"
                : $"mods/outfit{i:D4}.dds";
            using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
            using var entry = zip.CreateEntry(entryName).Open();
            entry.WriteByte((byte)i);
        }
    }

    [Benchmark]
    public async Task<int> ImportArchiveFolder()
    {
        var manager = Path.Combine(root, "manager-" + Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new GameModIntakeService(
                manager, Path.Combine(manager, "State"), () => games);
            var report = await intake.ImportPathsAsync([downloads]);
            if (report.Routed != ArchiveCount)
                throw new InvalidDataException(
                    $"Synthetic batch imported {report.Routed}/{ArchiveCount}; review={report.NeedsReview}.");
            return report.Routed;
        }
        finally
        {
            if (Directory.Exists(manager)) Directory.Delete(manager, recursive: true);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
