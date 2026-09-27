using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MhwModManager.Core;

namespace MhwModManager.Benchmarks;

public static class Program
{
    public static void Main() => BenchmarkRunner.Run<PlannerBenchmarks>();
}

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class PlannerBenchmarks
{
    private PlannerSnapshot snapshot = null!;
    private DeploymentPlanner planner = null!;

    [Params("200mods-50kfiles", "500mods-150kfiles")]
    public string Scenario { get; set; } = "200mods-50kfiles";

    [GlobalSetup]
    public void Setup()
    {
        planner = new(new ConflictEngine());
        var (modCount, filesPerMod) = Scenario.StartsWith("200", StringComparison.Ordinal) ? (200, 250) : (500, 300);
        var mods = new List<ModDescriptor>(modCount);
        var files = new List<ModFileDescriptor>(modCount * filesPerMod);
        var now = DateTimeOffset.UnixEpoch;

        for (var i = 0; i < modCount; i++)
        {
            var id = $"m{i}";
            mods.Add(new(id, $"Mod {i}", $"Mod {i}", $"M{i}", true, i));
            for (var f = 0; f < filesPerMod; f++)
            {
                // 100 paths intentionally have every mod as a provider. This stresses the
                // shared-resource case without unrealistic duplicate paths inside one mod.
                var path = f < 100
                    ? $@"nativePC\pl\f_equip\mod_hepsy\shared{f}.tex"
                    : $@"nativePC\fixture\{i}\file{f}.tex";
                var hash = $"{i:X8}{f:X8}".PadRight(64, '0');
                files.Add(new(id, path, hash, null, 1024, now, FileClass.Texture));
            }
        }

        snapshot = new(mods, files, [], new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, DeploymentManifestEntry>(), new Dictionary<string, string?>());
    }

    [Benchmark]
    public DeploymentPlan BuildPlan() => planner.Build(snapshot);
}
