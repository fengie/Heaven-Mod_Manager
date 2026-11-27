using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Benchmarks;

public enum AutoPopulateTopology { Independent, Chain32, ReplacementPairs, CyclePairs, MissingDependency }

/// <summary>Warm, indexed Auto Populate planning. No installed app, real game, or deployment.</summary>
[MemoryDiagnoser]
public class AutoPopulateTopologyBenchmarks
{
    // Large runs must be selected deliberately, not multiplied into every default benchmark job.
    public static IEnumerable<int> Sizes => Environment.GetEnvironmentVariable("MHW_AUTOPOPULATE_BENCHMARK_SIZE") switch
    {
        null or "" or "100" => [100],
        "1000" => [1000],
        "10000" => [10000],
        _ => throw new InvalidOperationException("Choose Auto Populate benchmark size 100, 1000, or 10000.")
    };

    [ParamsSource(nameof(Sizes))]
    public int ModCount { get; set; }

    [Params(AutoPopulateTopology.Independent, AutoPopulateTopology.Chain32, AutoPopulateTopology.ReplacementPairs, AutoPopulateTopology.CyclePairs, AutoPopulateTopology.MissingDependency)]
    public AutoPopulateTopology Topology { get; set; }

    private string? root;
    private AutoPopulateService service = null!;
    private AutoPopulateResult result = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        root = Directory.CreateTempSubdirectory("umm-auto-populate-topology-").FullName;
        try
        {
            var gameRoot = Directory.CreateDirectory(Path.Combine(root, "disposable-game")).FullName;
            var game = GameProfile.MonsterHunterWorld(gameRoot);
            var db = new ManagerDatabase(Path.Combine(root, "manager.db"));
            await db.InitializeAsync();

            for (var i = 0; i < ModCount; i++)
            {
                var id = Id(i);
                var source = Directory.CreateDirectory(Path.Combine(root, "sources", id)).FullName;
                await db.UpsertModAsync(new ModDescriptor(id, id, id, source, false, ModCount - i));
                var target = Topology == AutoPopulateTopology.ReplacementPairs
                    ? $@"nativePC\pl\f_equip\{Id(i / 2)}\skin.tex" : $@"nativePC\perf\{id}\payload.bin";
                await db.ReplaceModFilesAsync(id, [new ModFileDescriptor(id, target,
                    FormattableString.Invariant($"{i + 1:X64}"), null, 1, DateTimeOffset.UnixEpoch,
                    Topology == AutoPopulateTopology.ReplacementPairs ? FileClass.Texture : FileClass.GameData)]);

                var dependency = Topology switch
                {
                    AutoPopulateTopology.Chain32 when i + 1 < ModCount && (i + 1) % 32 != 0 => Id(i + 1),
                    AutoPopulateTopology.CyclePairs => Id(i ^ 1),
                    AutoPopulateTopology.MissingDependency when i % 8 == 0 => "missing-package",
                    _ => null
                };
                if (dependency is not null)
                    await File.WriteAllTextAsync(Path.Combine(source, "mod-manager.requirements.json"),
                        JsonSerializer.Serialize(new { dependencies = new[] { dependency } }));
            }

            service = new AutoPopulateService(new PlannerSnapshotRepository(db),
                new DeploymentPlanner(new ConflictEngine(game), game), new DependencyDoctorService(db, gameRoot, game),
                gameRoot, game);
            // Warm filesystem/DB caches once and reject a changed selection policy before timing.
            result = await service.BuildAsync();
            VerifyState();
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    [Benchmark]
    public async Task Plan()
    {
        result = await service.BuildAsync();
    }

    // Outside the measured interval. Counting alone would miss a swapped replacement winner.
    [IterationCleanup]
    public void VerifyState()
    {
        if (result.State.Count != ModCount)
            throw new InvalidDataException("Auto Populate lost an indexed synthetic package.");
        var enabled = 0;
        for (var i = 0; i < ModCount; i++)
        {
            var expected = Topology switch
            {
                AutoPopulateTopology.ReplacementPairs => i % 2 == 0,
                AutoPopulateTopology.MissingDependency => i % 8 != 0,
                _ => true
            };
            if (!result.State.TryGetValue(Id(i), out var state) || state.Enabled != expected)
                throw new InvalidDataException($"Incorrect Auto Populate decision for fixture {i} ({Topology}).");
            if (expected) enabled++;
        }
        if (result.EnabledMods != enabled)
            throw new InvalidDataException("Auto Populate enabled count disagrees with the verified identities.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (root is null) return;
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        root = null;
    }

    private static string Id(int i) => FormattableString.Invariant($"fixture-{i:D5}");
}
