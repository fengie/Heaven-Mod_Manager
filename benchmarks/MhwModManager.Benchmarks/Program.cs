using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MhwModManager.Core;

namespace MhwModManager.Benchmarks;

public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
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


[MemoryDiagnoser]
[ThreadingDiagnoser]
public class FamilyInferenceBenchmarks
{
    private ModDescriptor[] mods = null!;
    private ModFileDescriptor[] files = null!;

    [Params(100, 250, 500)]
    public int ModCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int filesPerMod = 100;
        const int familySize = 5;
        const int sharedFilesPerFamily = 40;
        var modList = new List<ModDescriptor>(ModCount);
        var fileList = new List<ModFileDescriptor>(ModCount * filesPerMod);
        var now = DateTimeOffset.UnixEpoch;
        var roles = new[] { "Base", "Body", "Waist", "Arms", "Hotfix" };

        for (var i = 0; i < ModCount; i++)
        {
            var family = i / familySize;
            var role = roles[i % familySize];
            var familyName = $"Armor Family {family:D4}";
            var displayName = role == "Base" ? familyName : $"{familyName} - {role}";
            var id = $"family-{family:D4}-part-{i % familySize}";
            modList.Add(new(id, displayName, displayName, id, false, i));

            for (var file = 0; file < filesPerMod; file++)
            {
                var path = file < sharedFilesPerFamily
                    ? $@"nativePC\fixture\family{family:D4}\body\shared{file:D3}.tex"
                    : $@"nativePC\fixture\family{family:D4}\part{i % familySize}\unique{file:D3}.tex";
                fileList.Add(new(id, path, $"{i:X8}{file:X8}".PadRight(64, '0'), null, 1024, now, FileClass.Texture));
            }
        }

        mods = modList.ToArray();
        files = fileList.ToArray();
    }

    [Benchmark]
    public IReadOnlyDictionary<string, string> InferFamilies() =>
        GenericFamilyInference.InferKeys(mods, files, out _);
}
