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
    public void Independent_mod_namespaces_in_one_armor_slot_can_coexist()
    {
        // Custom mod_* resource namespaces are physically isolated even though both
        // paths sit under the same armor body folder.
        var plan = Build([
            File("alpha", @"nativePC\pl\f_equip\pl018_0000\body\mod_cheeky\mesh.mod3", MeshHash),
            File("alpha", @"nativePC\pl\f_equip\pl018_0000\body\mod_cheeky\material.mrl3", MaterialHash),
            File("beta", @"nativePC\pl\f_equip\pl018_0000\body\mod_clockwork\mesh.mod3", DifferentHash),
            File("beta", @"nativePC\pl\f_equip\pl018_0000\body\mod_clockwork\material.mrl3", MaterialHash)]);

        Assert.False(plan.IsBlocked);
        Assert.Equal(4, plan.Changes.Count);
        Assert.DoesNotContain(plan.Conflicts, decision => decision.ReasonCode == "bundle-mixed-providers");
    }

    [Fact]
    public void One_shared_mod_namespace_does_not_justify_mixing_structural_assets()
    {
        var plan = Build([
            File("alpha", @"nativePC\pl\f_equip\pl018_0000\body\mod_shared\a.mod3", MeshHash),
            File("beta", @"nativePC\pl\f_equip\pl018_0000\body\mod_shared\b.mrl3", MaterialHash)]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts, c => c.Blocking && c.ReasonCode == "bundle-mixed-providers");
    }

    [Fact]
    public void Different_files_in_one_armor_slot_report_no_exact_file_collision()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Extra, DifferentHash),
            File("beta", @"nativePC\pl\f_equip\pl018_0000\body\armor.ccl", DifferentHash)]);

        Assert.True(plan.IsBlocked); // Mixing model/material/physics still requires evidence.
        var blocker = Assert.Single(plan.Conflicts, c => c.ReasonCode == "bundle-mixed-providers");
        Assert.Contains("0 exact-path overlaps", blocker.Evidence ?? string.Empty);
        Assert.Contains(Mesh, blocker.Evidence ?? string.Empty);
        Assert.Contains(Extra, blocker.Evidence ?? string.Empty);
    }

    [Fact]
    public void Same_target_with_different_bytes_reports_real_exact_file_collision()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, DifferentHash), File("beta", Material, MaterialHash)]);

        Assert.True(plan.IsBlocked);
        var blocker = Assert.Single(plan.Conflicts, c => c.ReasonCode == "bundle-mixed-providers");
        Assert.Contains("2 exact-path overlaps", blocker.Evidence ?? string.Empty);
        Assert.Contains("1 different-byte exact-path overlap", blocker.Evidence ?? string.Empty);
        Assert.Contains(Mesh, blocker.Evidence ?? string.Empty);
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
    public void Identical_subset_of_one_complete_bundle_does_not_create_a_false_conflict()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, MeshHash)]);

        Assert.False(plan.IsBlocked);
        Assert.Equal(2, plan.Changes.Count);
        Assert.Contains(plan.Conflicts, c => !c.Blocking && c.ReasonCode == "identical-bytes");
    }

    [Fact]
    public void Two_complementary_incomplete_structural_packages_still_need_review()
    {
        var plan = Build([File("alpha", Mesh, MeshHash),
            File("beta", Material, MaterialHash)]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Conflicts, c => c.Blocking && c.ReasonCode == "bundle-mixed-providers");
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Complete_provider_must_not_mask_a_different_byte_subset()
    {
        var plan = Build([File("alpha", Mesh, MeshHash), File("alpha", Material, MaterialHash),
            File("beta", Mesh, DifferentHash)]);

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
