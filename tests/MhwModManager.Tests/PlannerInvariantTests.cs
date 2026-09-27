using System.Globalization;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class PlannerInvariantTests
{
    [Fact]
    public void Randomized_texture_graphs_are_deterministic_and_have_one_winner_per_path()
    {
        var random = new Random(0x51A7);
        var planner = new DeploymentPlanner(new ConflictEngine());

        for (var iteration = 0; iteration < 75; iteration++)
        {
            var modCount = random.Next(2, 24);
            var pathCount = random.Next(4, 80);
            var mods = Enumerable.Range(0, modCount)
                .Select(i => new ModDescriptor($"m{i}", $"Fixture Texture {i}", $"Fixture Texture {i}", $"M{i}", true, i, FamilyId: "fixture-shared-texture"))
                .ToArray();
            var files = new List<ModFileDescriptor>();
            var now = DateTimeOffset.UnixEpoch;

            for (var p = 0; p < pathCount; p++)
            {
                var providers = random.Next(1, Math.Min(8, modCount) + 1);
                foreach (var i in Enumerable.Range(0, modCount).OrderBy(_ => random.Next()).Take(providers))
                {
                    var path = $@"nativePC\fixture\shared{p}.tex";
                    var hash = $"{i:X8}{p:X8}".PadRight(64, '0');
                    files.Add(new($"m{i}", path, hash, null, 10, now, FileClass.Texture));
                }
            }

            var snapshot = new PlannerSnapshot(mods, files, [],
                new Dictionary<string,string>(), new Dictionary<string,string>(),
                new Dictionary<string,DeploymentManifestEntry>(), new Dictionary<string,string?>());
            var a = planner.Build(snapshot);
            var b = planner.Build(snapshot);

            Assert.False(a.IsBlocked);
            Assert.Equal(
                a.Conflicts.Select(x => (x.Path,x.Kind,x.WinnerModId,x.ReasonCode)),
                b.Conflicts.Select(x => (x.Path,x.Kind,x.WinnerModId,x.ReasonCode)));
            Assert.Equal(
                a.Changes.Select(x => (x.Path,x.AfterBlobSha256,x.ProviderAfter,x.Kind)),
                b.Changes.Select(x => (x.Path,x.AfterBlobSha256,x.ProviderAfter,x.Kind)));

            foreach (var group in files.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                var decision = a.Conflicts.Single(x => PathRules.Comparer.Equals(x.Path, group.Key));
                Assert.NotNull(decision.WinnerModId);
                Assert.Contains(group, x => PathRules.Comparer.Equals(x.ModId, decision.WinnerModId));
            }
        }
    }

    [Fact]
    public void Large_shared_namespace_with_sparse_incompatibility_still_blocks()
    {
        const string path = @"nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex";
        var candidates = Enumerable.Range(0, 160)
            .Select(i => new ProviderCandidate($"m{i}", $"Mod {i}", i,
                new ModFileDescriptor($"m{i}", path, i.ToString("X64", CultureInfo.InvariantCulture), null, 100,
                    DateTimeOffset.UnixEpoch, FileClass.Texture)))
            .ToArray();
        var rule = new ConflictRule("bad", RuleKind.Incompatible, RuleScope.ModPair,
            "m3", "m151", null, null, "fixture", true, DateTimeOffset.UnixEpoch);

        var decision = new ConflictEngine().Decide(path, candidates, [rule],
            new Dictionary<string,string>(), new Dictionary<string,string>(),
            new Dictionary<(string,string),PairStats>());

        Assert.True(decision.Blocking);
        Assert.Equal(ConflictKind.Incompatible, decision.Kind);
    }
}
