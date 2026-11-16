using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.Benchmarks;

/// <summary>
/// A warm import is not free: duplicate receipts must be loaded and each selected
/// archive is inspected and verified again. Measure replay separately from first
/// import so the very different code paths cannot hide regressions in one mean.
/// All files and databases are synthetic and scoped to a temporary directory.
/// </summary>
[MemoryDiagnoser]
public class BulkArchiveReplayBenchmarks
{
    [Params(8, 64, 256)]
    public int ArchiveCount { get; set; }

    private string root = "";
    private string source = "";
    private GameModIntakeService intake = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        root = Path.Combine(Path.GetTempPath(),
            "umm-replay-benchmark-" + Guid.NewGuid().ToString("N"));
        source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);

        var games = new[]
        {
            GameProfile.MonsterHunterWorld(Path.Combine(root, "Games", "MHW")),
            GameProfile.Generic("nier-automata", "NieR Automata",
                Path.Combine(root, "Games", "NieR"), "NieRAutomata.exe", "mods")
        };
        var payload = new byte[4 * 1024];
        var random = new Random(944);
        for (var index = 0; index < ArchiveCount; index++)
        {
            random.NextBytes(payload);
            var mhw = index % 2 == 0;
            var name = mhw
                ? $"MHW Armor {index:D4}.zip"
                : $"NieR Automata Outfit {index:D4}.zip";
            var entry = mhw
                ? $"nativePC/pl/armor/a{index:D4}.tex"
                : $"mods/outfit{index:D4}.dds";
            using var zip = ZipFile.Open(Path.Combine(source, name), ZipArchiveMode.Create);
            using var stream = zip.CreateEntry(entry).Open();
            stream.Write(payload);
        }

        var manager = Path.Combine(root, "Manager");
        intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => games);
        var first = await intake.ImportPathsAsync([source]);
        if (first.Routed != ArchiveCount || first.NeedsReview != 0)
            throw new InvalidDataException(
                $"Setup imported {first.Routed}/{ArchiveCount} packages; needs review {first.NeedsReview}.");
    }

    [Benchmark]
    public async Task<int> ReimportPreviouslyStagedArchiveFolder()
    {
        var result = await intake.ImportPathsAsync([source]);
        if (result.AlreadyPresent != ArchiveCount || result.Routed != 0)
            throw new InvalidDataException(
                $"Replay changed routing: {result.AlreadyPresent} deduplicated, {result.Routed} new.");
        return result.AlreadyPresent;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
