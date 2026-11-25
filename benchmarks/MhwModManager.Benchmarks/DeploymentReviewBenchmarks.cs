using BenchmarkDotNet.Attributes;
using MhwModManager.Core;

namespace MhwModManager.Benchmarks;

[MemoryDiagnoser]
public class DeploymentReviewBenchmarks
{
    [Params(100, 500, 1000)]
    public int ModCount { get; set; }

    private ModDescriptor[] mods = [];
    private DeploymentPlan plan = null!;

    [GlobalSetup]
    public void Setup()
    {
        mods = Enumerable.Range(0, ModCount)
            .Select(i => new ModDescriptor($"mod-{i}", $"Mod {i}", $"Mod {i}", $"mod-{i}", true, i))
            .ToArray();
        var changes = new List<DeploymentChange>();
        var decisions = new List<ConflictDecision>();
        const int filesPerMod = 100;
        for (var i = 0; i < ModCount; i++)
        {
            for (var j = 0; j < filesPerMod; j++)
            {
                var path = $@"nativePC\fixture\{i}\file{j}.tex";
                var before = mods[(i + 1) % ModCount].Id;
                var after = mods[i].Id;
                var oldHash = new string('a', 64);
                var newHash = new string('b', 64);
                decisions.Add(new(path, ConflictKind.UserOverlayRule, false, after,
                    "remembered-overlay", "Explicit overlay", Confidence.Explicit, "rule"));
                changes.Add(new(changes.Count + 1, ChangeKind.Replace, path,
                    oldHash, newHash, before, after, oldHash, "rule"));
            }
        }

        plan = new DeploymentPlan("review-scale", DateTimeOffset.UnixEpoch, changes, decisions, []);
    }

    [Benchmark]
    public int ProjectExactReview()
    {
        var rows = DeploymentReview.Project(plan, mods);
        if (rows.Count != plan.Changes.Count)
            throw new InvalidDataException("Deployment review lost a planned change.");
        return rows.Count;
    }
}
