using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

/// <summary>
/// Content equivalence is a safe physical co-installation fact, not evidence
/// that unrelated packages have a shared family or a particular update order.
/// </summary>
public sealed class EquivalentStructuralBundleTests
{
    private const string Mesh = @"nativePC\pl\f_equip\pl018_0000\body\armor.mod3";
    private const string Material = @"nativePC\pl\f_equip\pl018_0000\body\armor.mrl3";
    private const string Extra = @"nativePC\pl\f_equip\pl018_0000\body\armor.ctc";
    private static readonly string MeshHash = new('a', 64);
    private static readonly string MaterialHash = new('b', 64);
    private static readonly string DifferentHash = new('c', 64);

    [Fact]
    public void Unrelated_mods_with_identical_complete_structural_bundle_coexist()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Material, MaterialHash), File("beta", Mesh, MeshHash)]);

        Assert.False(plan.IsBlocked);
        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Conflicts, decision =>
        {
            Assert.False(decision.Blocking);
            Assert.Equal(ConflictKind.Identical, decision.Kind);
            Assert.Equal("identical-bytes", decision.ReasonCode);
        });
    }

    [Fact]
    public void Changed_structural_sibling_still_requires_a_proven_order()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, MeshHash), File("beta", Material, DifferentHash)]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts, c => c.Blocking && c.ReasonCode == "bundle-mixed-providers");
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Incomplete_bundle_repack_does_not_authorize_mixing_unique_siblings()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, MeshHash), File("beta", Extra, DifferentHash)]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts, c => c.Blocking && c.ReasonCode == "bundle-mixed-providers");
    }

    [Fact]
    public void Missing_structural_sibling_does_not_count_as_a_complete_duplicate()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, MeshHash)]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts, c => c.Blocking && c.ReasonCode == "bundle-mixed-providers");
    }

    [Fact]
    public void Explicit_incompatibility_overrides_even_byte_identical_packages()
    {
        var rule = new ConflictRule("incompatible", RuleKind.Incompatible, RuleScope.ModPair,
            "alpha", "beta", null, null, "Explicitly incompatible", true, DateTimeOffset.UnixEpoch);
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, MeshHash), File("beta", Material, MaterialHash)], [rule]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts, c => c.Blocking && c.Kind == ConflictKind.Incompatible);
    }

    private static DeploymentPlan Build(IReadOnlyList<ModFileDescriptor> files,
        IReadOnlyList<ConflictRule>? rules = null)
    {
        var game = GameProfile.MonsterHunterWorld(Path.Combine(Path.GetTempPath(), "mhw-bundle-fixture"));
        var mods = new[]
        {
            new ModDescriptor("alpha", "Dawn Armor", "Dawn Armor", "source-a", true, 10),
            new ModDescriptor("beta", "Twilight Armor", "Twilight Armor", "source-b", true, 20)
        };
        var snapshot = new PlannerSnapshot(mods, files, rules ?? [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, DeploymentManifestEntry>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        return new DeploymentPlanner(new ConflictEngine(game), game).Build(snapshot);
    }

    private static ModFileDescriptor File(string mod, string path, string sha) =>
        new(mod, path, sha, null, 4096, DateTimeOffset.UnixEpoch, FileClass.Structural);
}
