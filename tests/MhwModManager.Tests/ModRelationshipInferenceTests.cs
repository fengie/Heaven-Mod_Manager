using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ModRelationshipInferenceTests
{
    [Fact]
    public void Disabled_cross_source_texture_overlay_is_reviewable_without_merging_or_enabling()
    {
        var mods = new[]
        {
            Mod("body", "High Poly Body Base", "1965"),
            Mod("tattoo", "tattoo-skin-texture-overlay-8397-1-0-1763815951-500733aabbeeccdd00112233445566778899aabbccddeeff0011223344556677", "8397")
        };
        var files = new[]
        {
            File("body", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3"),
            File("body", @"nativePC\pl\f_equip\pl001_0000\body\body.tex"),
            File("tattoo", @"nativePC\pl\f_equip\pl001_0000\body\body.tex")
        };

        var suggestion = Assert.Single(ModRelationshipInference.Suggest(mods, files));
        Assert.Equal(ModRelationshipKind.PossibleOverride, suggestion.Kind);
        Assert.Equal("body", suggestion.ParentModId);
        Assert.Equal("tattoo", suggestion.ChildModId);
        Assert.Equal(1, suggestion.SharedPaths);
        Assert.Contains("review", suggestion.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, LogicalModFamilies.Build(mods, files).Count);
        Assert.All(mods, mod => Assert.False(mod.Enabled));
    }

    [Fact]
    public void Separate_overlays_do_not_become_each_others_parent_even_when_they_replace_same_texture()
    {
        var mods = new[]
        {
            Mod("body", "High Poly Body Base", "1965"),
            Mod("a", "Tattoo Skin Texture Overlay", "8397"),
            Mod("b", "Tattoo Skin Texture Overlay", "8411")
        };
        var path = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var files = new[]
        {
            File("body", path),
            File("body", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3"),
            File("a", path),
            File("b", path)
        };

        var candidates = ModRelationshipInference.Suggest(mods, files);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal("body", candidate.ParentModId));
        Assert.Contains(candidates, candidate => candidate.ChildModId == "a");
        Assert.Contains(candidates, candidate => candidate.ChildModId == "b");
        Assert.Equal(3, LogicalModFamilies.Build(mods, files).Count);
    }

    [Theory]
    [InlineData("Alt")]
    [InlineData("Alternate")]
    [InlineData("Alternate-Patch")]
    [InlineData("Choose")]
    [InlineData("Choice-Texture")]
    [InlineData("Color")]
    [InlineData("Colour")]
    [InlineData("Style")]
    [InlineData("Version")]
    public void Pick_one_patch_variants_are_not_suggested_as_cross_source_addons(string label)
    {
        var mods = new[]
        {
            Mod("base", "High Poly Body Base", "1965"),
            Mod("variant", $"High Poly Body {label} Patch", "8397")
        };
        const string shared = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var files = new[]
        {
            ContentFile("base", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3", new string('c', 64)),
            ContentFile("base", shared, new string('a', 64)),
            ContentFile("variant", shared, new string('b', 64))
        };

        Assert.Empty(ModRelationshipInference.Suggest(mods, files));
    }

    [Fact]
    public void Shared_loader_roots_or_unrelated_replacements_are_not_enough_to_suggest_a_parent()
    {
        var mods = new[]
        {
            Mod("a", "Summer Dress", "1001"),
            Mod("b", "Dragon Knight", "1002"),
            Mod("c", "Tattoo Overlay", "8397")
        };
        var files = new[]
        {
            File("a", @"nativePC\pl\f_equip\pl001_0000\body\a.tex"),
            File("b", @"nativePC\pl\f_equip\pl001_0000\body\a.tex"),
            File("c", @"nativePC\pl\f_equip\pl099_0000\body\c.tex")
        };
        Assert.Empty(ModRelationshipInference.Suggest(mods, files));
    }

    [Fact]
    public void Existing_manual_family_is_never_reinterpreted_as_automatic_relationship()
    {
        var mods = new[]
        {
            Mod("body", "High Poly Body Base", "1965"),
            Mod("overlay", "Tattoo Skin Texture Overlay", "8397") with { FamilyId = "manual:already-decided" }
        };
        var path = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var files = new[]
        {
            File("body", path),
            File("body", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3"),
            File("overlay", path)
        };
        Assert.Empty(ModRelationshipInference.Suggest(mods, files));
    }

    [Fact]
    public void A_confirmed_manual_main_still_accepts_later_addon_candidates_without_reclassifying_existing_children()
    {
        var mods = new[]
        {
            Mod("body", "High Poly Body Base", "1965") with { FamilyId = "manual:body", FamilyRole = "Main" },
            Mod("first", "Tattoo Skin Texture Overlay A", "8397") with { FamilyId = "manual:body", FamilyRole = "Optional" },
            Mod("second", "Tattoo Skin Texture Overlay B", "8411")
        };
        var path = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var files = new[]
        {
            File("body", path),
            File("body", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3"),
            File("first", path),
            File("second", path)
        };

        var suggestion = Assert.Single(ModRelationshipInference.Suggest(mods, files));
        Assert.Equal("body", suggestion.ParentModId);
        Assert.Equal("second", suggestion.ChildModId);
    }

    [Fact]
    public void Input_order_does_not_change_suggested_edges()
    {
        var mods = new[]
        {
            Mod("base", "High Poly Body Base", "1965"),
            Mod("a", "Tattoo Overlay A", "8397"),
            Mod("b", "Tattoo Overlay B", "8411")
        };
        var shared = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var files = new[]
        {
            File("base", shared), File("base", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3"),
            File("a", shared), File("b", shared)
        };
        var expected = ModRelationshipInference.Suggest(mods, files).Select(s => (s.ParentModId, s.ChildModId)).ToArray();
        for (var seed = 0; seed < 12; seed++)
        {
            var rng = new Random(seed + 12);
            var actual = ModRelationshipInference.Suggest(mods.OrderBy(_ => rng.Next()).ToArray(),
                files.OrderBy(_ => rng.Next()).ToArray()).Select(s => (s.ParentModId, s.ChildModId)).ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Large_unrelated_library_with_common_roots_has_no_suggestions()
    {
        var mods = Enumerable.Range(0, 500).Select(i => Mod($"m{i}", $"Unique Outfit {i}", (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var files = mods.Select((mod, i) => File(mod.Id, $@"nativePC\pl\f_equip\pl001_0000\body\piece{i}.tex")).ToArray();
        Assert.Empty(ModRelationshipInference.Suggest(mods, files));
    }


    [Fact]
    public void Byte_identical_texture_copy_is_not_an_override_suggestion()
    {
        var mods = new[]
        {
            Mod("parent", "High Poly Body Base", "1965"),
            Mod("copy", "Tattoo Skin Texture Overlay", "8397")
        };
        const string shared = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var same = new string('a', 64);
        var files = new[]
        {
            ContentFile("parent", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3", new string('b', 64)),
            ContentFile("parent", shared, same),
            ContentFile("copy", shared, same)
        };

        Assert.Empty(ModRelationshipInference.Suggest(mods, files));
        Assert.Equal(2, LogicalModFamilies.Build(mods, files).Count);
    }

    [Fact]
    public void Byte_different_texture_overlay_remains_reviewable()
    {
        var mods = new[]
        {
            Mod("parent", "High Poly Body Base", "1965"),
            Mod("tattoo", "Tattoo Skin Texture Overlay", "8397")
        };
        const string shared = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        var files = new[]
        {
            ContentFile("parent", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3", new string('d', 64)),
            ContentFile("parent", shared, new string('a', 64)),
            ContentFile("tattoo", shared, new string('b', 64))
        };

        var suggested = Assert.Single(ModRelationshipInference.Suggest(mods, files));
        Assert.Equal(ModRelationshipKind.PossibleOverride, suggested.Kind);
        Assert.Equal("parent", suggested.ParentModId);
        Assert.Equal("tattoo", suggested.ChildModId);
        Assert.Contains("different", suggested.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_identical_and_one_different_shared_file_still_requires_review()
    {
        var mods = new[]
        {
            Mod("parent", "High Poly Body Base", "1965"),
            Mod("patch", "Tattoo Skin Texture Overlay", "8397")
        };
        const string sharedA = @"nativePC\pl\f_equip\pl001_0000\body\body.tex";
        const string sharedB = @"nativePC\pl\f_equip\pl001_0000\body\detail.tex";
        var files = new[]
        {
            ContentFile("parent", @"nativePC\pl\f_equip\pl001_0000\body\body.mod3", new string('c', 64)),
            ContentFile("parent", sharedA, new string('a', 64)),
            ContentFile("parent", sharedB, new string('b', 64)),
            ContentFile("patch", sharedA, new string('a', 64)),
            ContentFile("patch", sharedB, new string('d', 64))
        };

        var result = Assert.Single(ModRelationshipInference.Suggest(mods, files));
        Assert.Equal(2, result.SharedPaths);
        Assert.Contains("1 byte-identical", result.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    private static ModFileDescriptor ContentFile(string modId, string path, string digest) =>
        new(modId, path, digest, null, 4096, DateTimeOffset.UnixEpoch, PathRules.ClassifyFile(path));

    private static ModDescriptor Mod(string id, string name, string nexusModId) =>
        new(id, name, name, id, false, 0, NexusModId: nexusModId);

    private static ModFileDescriptor File(string modId, string path) =>
        new(modId, path, "sha-" + modId, null, 1, DateTimeOffset.UnixEpoch, PathRules.ClassifyFile(path));
}
