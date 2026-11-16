using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

/// <summary>
/// Metamorphic tests: changing input enumeration order must not change inferred
/// family membership, even when names and shared resource roots are ambiguous.
/// </summary>
public sealed class FamilyInferenceOrderInvariantsTests
{
    [Fact]
    public void Independent_nested_components_keep_the_same_family_keys_after_seeded_permutations()
    {
        var mods = new[]
        {
            Mod("a-base", "Commission Armor Rework"),
            Mod("a-waist", "Commission Armor Rework - Waist"),
            Mod("b-base", "Commission Armor Rework"),
            Mod("b-waist", "Commission Armor Rework - Waist")
        };
        var files = new[]
        {
            File("a-base", @"nativePC\pl\f_equip\pl001_0000\wst\a.mod3"),
            File("a-waist", @"nativePC\pl\f_equip\pl001_0000\wst\a.mod3"),
            File("b-base", @"nativePC\pl\f_equip\pl099_0000\wst\b.mod3"),
            File("b-waist", @"nativePC\pl\f_equip\pl099_0000\wst\b.mod3")
        };
        var baseline = GenericFamilyInference.InferKeys(mods, files, out var baselineEvidence);
        Assert.Equal(4, baseline.Count);
        Assert.Equal(baseline["a-base"], baseline["a-waist"]);
        Assert.Equal(baseline["b-base"], baseline["b-waist"]);
        Assert.NotEqual(baseline["a-base"], baseline["b-base"]);
        Assert.NotEmpty(baselineEvidence);

        for (var seed = 0; seed < 16; seed++)
        {
            var random = new Random(seed * 31 + 7);
            var shuffledMods = mods.OrderBy(_ => random.Next()).ToArray();
            var shuffledFiles = files.OrderBy(_ => random.Next()).ToArray();
            var observed = GenericFamilyInference.InferKeys(shuffledMods, shuffledFiles, out var evidence);
            foreach (var mod in mods)
                Assert.Equal(baseline[mod.Id], observed[mod.Id]);
            Assert.Equal(baselineEvidence.Count, evidence.Count);
        }
    }

    [Fact]
    public void Indirect_bridge_cannot_merge_incompatible_families_in_any_input_order()
    {
        var mods = new[]
        {
            Mod("alpha", "Alpha Armor"),
            Mod("bridge", "Alpha Armor - Waist"),
            Mod("beta", "Beta Dress")
        };
        var files = new[]
        {
            File("alpha", @"nativePC\pl\f_equip\pl001_0000\wst\shared-a.mod3"),
            File("bridge", @"nativePC\pl\f_equip\pl001_0000\wst\shared-a.mod3"),
            File("bridge", @"nativePC\pl\f_equip\pl099_0000\body\shared-b.mod3"),
            File("beta", @"nativePC\pl\f_equip\pl099_0000\body\shared-b.mod3")
        };

        for (var seed = 0; seed < 12; seed++)
        {
            var random = new Random(100 + seed);
            var observed = GenericFamilyInference.InferKeys(
                mods.OrderBy(_ => random.Next()).ToArray(),
                files.OrderBy(_ => random.Next()).ToArray(), out _);

            Assert.Equal(observed["alpha"], observed["bridge"]);
            Assert.False(observed.ContainsKey("beta"));
        }
    }

    [Fact]
    public void Shared_directory_prefix_without_identity_evidence_does_not_collapse_distinct_mods()
    {
        var names = new[]
        {
            "Dragon", "Summer", "Fatalis", "Neon", "Crystal", "Azure",
            "Forest", "Winter", "Royal", "Volcanic", "Steam", "Golden"
        };
        var mods = names.Select((name, i) => Mod($"different-{i:D2}", name)).ToArray();
        var files = mods.Select((mod, i) =>
            File(mod.Id, $@"nativePC\common\shared\object{i:D2}.tex")).ToArray();

        var inferred = GenericFamilyInference.InferKeys(mods, files, out var evidence);

        Assert.Empty(inferred);
        Assert.Empty(evidence);
    }

    private static ModDescriptor Mod(string id, string name) =>
        new(id, name, name, id, false, 0);

    private static ModFileDescriptor File(string modId, string path) =>
        new(modId, path, "sha-" + modId, null, 1, DateTimeOffset.UnixEpoch,
            PathRules.ClassifyFile(path));
}
