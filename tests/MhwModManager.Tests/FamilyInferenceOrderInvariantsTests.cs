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
            File(mod.Id, $@"nativePC\common\shared\assets\object{i:D2}.tex")).ToArray();

        var inferred = GenericFamilyInference.InferKeys(mods, files, out var evidence);

        Assert.Empty(inferred);
        Assert.Empty(evidence);
    }

    [Fact]
    public void Dissimilar_names_with_identical_file_paths_do_not_create_family_evidence()
    {
        var mods = new[]
        {
            Mod("dragon", "Dragon Armor"),
            Mod("ocean", "Ocean Cloak")
        };
        var files = new[]
        {
            File("dragon", @"nativePC\shared\collection\common\same.tex"),
            File("ocean", @"nativePC\shared\collection\common\same.tex")
        };

        var inferred = GenericFamilyInference.InferKeys(mods, files, out var evidence);

        Assert.Empty(inferred);
        Assert.Empty(evidence);
    }

    [Fact]
    public void Version_like_numeric_suffixes_with_one_shared_root_do_not_group_unrelated_files()
    {
        var mods = Enumerable.Range(0, 32)
            .Select(index => Mod($"item-{index:D5}", $"Synthetic {index:D5}"))
            .ToArray();
        var files = mods.Select((mod, index) =>
            File(mod.Id, $@"nativePC\shared\collection\common\mod{index:D5}\file.tex"))
            .ToArray();

        var inferred = GenericFamilyInference.InferKeys(mods, files, out var evidence);

        Assert.Empty(inferred);
        Assert.Empty(evidence);
    }


    [Fact]
    public void Different_sources_and_names_can_form_one_family_from_rare_matching_payload_anchors()
    {
        var mods = new[]
        {
            Mod("part-a", "Solstice Mantle"),
            Mod("part-b", "Veiled Counterweight"),
            Mod("other", "Ranger Cloak")
        };
        const string sharedMesh = @"nativePC\pl\f_equip\pl001_0000\body\shared.mod3";
        const string sharedMaterial = @"nativePC\pl\f_equip\pl001_0000\body\shared.mrl3";
        var files = new[]
        {
            ContentFile("part-a", sharedMesh, "private-mesh"),
            ContentFile("part-a", sharedMaterial, "private-material"),
            ContentFile("part-a", @"nativePC\pl\f_equip\pl001_0000\body\a.ctc", "unique-part-a"),
            ContentFile("part-b", sharedMesh, "private-mesh"),
            ContentFile("part-b", sharedMaterial, "private-material"),
            ContentFile("part-b", @"nativePC\pl\f_equip\pl001_0000\wst\b.ctc", "unique-part-b"),
            ContentFile("other", @"nativePC\pl\f_equip\pl001_0000\body\other.mod3", "unrelated")
        };

        var expectedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "part-a", "part-b" };
        for (var seed = 0; seed < 8; seed++)
        {
            var random = new Random(seed + 31);
            var orderedMods = mods.OrderBy(_ => random.Next()).ToArray();
            var orderedFiles = files.OrderBy(_ => random.Next()).ToArray();
            var families = LogicalModFamilies.Build(orderedMods, orderedFiles);
            Assert.Equal(2, families.Count);
            Assert.Contains(families, f => f.Members.Select(m => m.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedParts));
            Assert.Contains(families, f => f.Members.Count == 1 && f.Members[0].Id == "other");
        }
    }

    [Fact]
    public void Third_party_copy_of_two_assets_preserves_distinctive_three_asset_family()
    {
        // Two independently downloaded parts share three nontrivial, identical
        // deployable assets, while a later unrelated mod copies only two.
        // The third package must not retroactively split the original family.
        var a = Mod("part-a", "Solstice Mantle");
        var b = Mod("part-b", "Veiled Counterweight");
        var unrelated = Mod("unrelated", "Ranger Cloak");
        const string mesh = @"nativePC\pl\f_equip\pl001_0000\body\shared.mod3";
        const string material = @"nativePC\pl\f_equip\pl001_0000\body\shared.mrl3";
        const string detail = @"nativePC\pl\f_equip\pl001_0000\body\detail.tex";
        var files = new[]
        {
            ContentFile(a.Id, mesh, "same-mesh"),
            ContentFile(a.Id, material, "same-material"),
            ContentFile(a.Id, detail, "same-detail"),
            ContentFile(a.Id, @"nativePC\pl\f_equip\pl001_0000\body\a.ctc", "a-unique"),
            ContentFile(b.Id, mesh, "same-mesh"),
            ContentFile(b.Id, material, "same-material"),
            ContentFile(b.Id, detail, "same-detail"),
            ContentFile(b.Id, @"nativePC\pl\f_equip\pl001_0000\body\b.ctc", "b-unique"),
            ContentFile(unrelated.Id, mesh, "same-mesh"),
            ContentFile(unrelated.Id, material, "same-material"),
            ContentFile(unrelated.Id, @"nativePC\pl\f_equip\pl001_0000\body\unrelated.ctc", "unrelated-unique")
        };

        var before = GenericFamilyInference.InferKeys([a, b], files, out _);
        Assert.Equal(before[a.Id], before[b.Id]);

        for (var seed = 0; seed < 8; seed++)
        {
            var random = new Random(seed + 371);
            var mods = new[] { a, b, unrelated }.OrderBy(_ => random.Next()).ToArray();
            var shuffledFiles = files.OrderBy(_ => random.Next()).ToArray();
            var after = GenericFamilyInference.InferKeys(mods, shuffledFiles, out var evidence);
            Assert.Equal(2, after.Count);
            Assert.Equal(before[a.Id], after[a.Id]);
            Assert.Equal(after[a.Id], after[b.Id]);
            Assert.False(after.ContainsKey(unrelated.Id));
            Assert.Single(evidence);
        }
    }

    [Fact]
    public void Common_payload_copied_by_three_unrelated_packages_is_not_family_identity()
    {
        var mods = new[] { Mod("a", "Atlas"), Mod("b", "Nebula"), Mod("c", "Tundra") };
        const string anchorA = @"nativePC\shared\common\one.tex";
        const string anchorB = @"nativePC\shared\common\two.tex";
        var files = mods.SelectMany(mod => new[]
        {
            ContentFile(mod.Id, anchorA, "shared-a"),
            ContentFile(mod.Id, anchorB, "shared-b"),
            ContentFile(mod.Id, @"nativePC\shared\common\" + mod.Id + ".tex", mod.Id)
        }).ToArray();

        Assert.Empty(GenericFamilyInference.InferKeys(mods, files, out var evidence));
        Assert.Empty(evidence);
    }

    [Fact]
    public void One_identical_shared_payload_does_not_establish_an_unrelated_family()
    {
        var mods = new[] { Mod("a", "Atlas"), Mod("b", "Nebula") };
        const string anchor = @"nativePC\shared\common\one.tex";
        var files = new[]
        {
            ContentFile("a", anchor, "shared-a"),
            ContentFile("a", @"nativePC\shared\common\a.tex", "only-a"),
            ContentFile("b", anchor, "shared-a"),
            ContentFile("b", @"nativePC\shared\common\b.tex", "only-b")
        };
        Assert.Empty(GenericFamilyInference.InferKeys(mods, files, out var evidence));
        Assert.Empty(evidence);
    }


    [Fact]
    public void Shared_asset_namespace_cannot_bridge_two_distinct_content_backed_families()
    {
        var mods = new[]
        {
            Mod("a", "Solstice"),
            Mod("b", "Nebula"),
            Mod("c", "Cinder")
        };
        const string x = @"nativePC\pl\f_equip\pl001_0000\body\x.mod3";
        const string y = @"nativePC\pl\f_equip\pl001_0000\body\y.mrl3";
        const string z = @"nativePC\pl\f_equip\pl001_0000\body\z.mod3";
        const string w = @"nativePC\pl\f_equip\pl001_0000\body\w.mrl3";
        var files = new[]
        {
            ContentFile("a", x, "private-x"),
            ContentFile("a", y, "private-y"),
            ContentFile("a", @"nativePC\pl\f_equip\pl001_0000\body\a.ctc", "a"),
            ContentFile("b", x, "private-x"),
            ContentFile("b", y, "private-y"),
            ContentFile("b", z, "private-z"),
            ContentFile("b", w, "private-w"),
            ContentFile("b", @"nativePC\pl\f_equip\pl001_0000\body\b.ctc", "b"),
            ContentFile("c", z, "private-z"),
            ContentFile("c", w, "private-w"),
            ContentFile("c", @"nativePC\pl\f_equip\pl001_0000\body\c.ctc", "c")
        };
        var families = LogicalModFamilies.Build(mods,files);
        Assert.Equal(2, families.Count);
        Assert.Contains(families,f => f.Members.Count==2 &&
            f.Members.Any(m => m.Id=="a") && f.Members.Any(m => m.Id=="b"));
        Assert.Contains(families,f => f.Members.Count==1 && f.Members[0].Id=="c");
    }

    private static ModFileDescriptor ContentFile(string modId, string path, string payload) =>
        new(modId, path,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            null, 4096, DateTimeOffset.UnixEpoch, PathRules.ClassifyFile(path));

    private static ModDescriptor Mod(string id, string name) =>
        new(id, name, name, id, false, 0);

    private static ModFileDescriptor File(string modId, string path) =>
        new(modId, path, "sha-" + modId, null, 1, DateTimeOffset.UnixEpoch,
            PathRules.ClassifyFile(path));
}
