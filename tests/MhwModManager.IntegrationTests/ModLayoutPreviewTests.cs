using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Structural groups are inspection evidence, never automatic installation authority.
/// All fixture names are synthetic; no user mod files are needed or published.
/// </summary>
public sealed class ModLayoutPreviewTests
{
    private static GameProfile Game(string id) => GameProfile.Generic(
        id, id, Path.Combine(Path.GetTempPath(), "layout-preview-game"),
        "Game.exe", "mods", nexusGameDomain: id == "stellar-blade" ? "stellarblade" : id);

    private static ModLayoutInspection Inspect(string game, params string[] paths) =>
        ModLayoutPreview.Inspect(ModBoundaryDiscovery.Discover(paths), Game(game), TestContext.Current.CancellationToken);

    private static void AssertConserved(string[] paths, ModLayoutInspection inspection)
    {
        var expected = paths.Select(path => path.Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var actual = inspection.Groups.SelectMany(group => group.Files)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, inspection.FileCount);
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Boundary_discovery_retains_even_files_omitted_from_automatic_candidates()
    {
        string[] paths = ["readme.md", "screenshots/preview.png", "unknown.bin"];
        var discovery = ModBoundaryDiscovery.Discover(paths);
        Assert.NotNull(discovery.FileInventory);
        Assert.Equal(paths.Order(StringComparer.OrdinalIgnoreCase), discovery.FileInventory);
        AssertConserved(paths, ModLayoutPreview.Inspect(discovery, Game("sifu"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Bare_sifu_companions_are_visible_without_game_named_wrapper_folders()
    {
        string[] paths = ["OutfitA.pak", "OutfitA.sig", "OutfitB.pak", "OutfitB.sig"];
        var result = Inspect("sifu", paths);
        Assert.Equal("unreal-pak-signature/v1", result.ProfileId);
        Assert.Equal(2, result.Groups.Count);
        Assert.All(result.Groups, group =>
        {
            Assert.Equal(ModLayoutGroupKind.CompanionPattern, group.Kind);
            Assert.Equal(2, group.Files.Count);
            Assert.Contains("not proof", group.Reason, StringComparison.OrdinalIgnoreCase);
        });
        AssertConserved(paths, result);
    }

    [Fact]
    public void Nested_stellar_companions_do_not_require_a_repeated_SB_installation_tree()
    {
        string[] paths = [
            "Collection/A/One_P.pak", "Collection/A/One_P.ucas", "Collection/A/One_P.utoc",
            "Collection/B/Two_P.pak", "Collection/B/Two_P.ucas", "Collection/B/Two_P.utoc"];
        var result = Inspect("stellar-blade", paths);
        Assert.Equal("unreal-iostore-companions/v1", result.ProfileId);
        Assert.Equal(2, result.Groups.Count);
        Assert.All(result.Groups, group => Assert.Equal(ModLayoutGroupKind.CompanionPattern, group.Kind));
        AssertConserved(paths, result);
    }

    [Fact]
    public void Same_stem_in_different_folders_is_never_joined_across_sources()
    {
        string[] paths = ["A/Skin.pak", "A/Skin.sig", "B/Skin.pak", "B/Skin.sig"];
        var result = Inspect("sifu", paths);
        Assert.Equal(2, result.Groups.Count);
        Assert.All(result.Groups, group => Assert.Single(group.Files.Select(Path.GetDirectoryName).Distinct()));
        AssertConserved(paths, result);
    }

    [Fact]
    public void Companion_matching_is_case_insensitive_but_preserves_original_spelling()
    {
        string[] paths = ["Set/Outfit.PAK", "set/outfit.SIG"];
        var result = Inspect("sifu", paths);
        Assert.Equal(ModLayoutGroupKind.CompanionPattern, Assert.Single(result.Groups).Kind);
        AssertConserved(paths, result);
    }

    [Fact]
    public void Partial_patterns_and_unclassified_files_are_not_discarded_or_declared_invalid()
    {
        string[] paths = ["Complete.pak", "Complete.sig", "Partial.pak", "readme.md", "extra.bin"];
        var result = Inspect("sifu", paths);
        Assert.Contains(result.Groups, group => group.Kind == ModLayoutGroupKind.PartialCompanionPattern);
        Assert.Contains(result.Groups, group => group.Kind == ModLayoutGroupKind.Unclassified);
        AssertConserved(paths, result);
    }

    [Fact]
    public void Fomod_choices_remain_one_review_scope_including_all_options()
    {
        string[] paths = [
            "Package/fomod/ModuleConfig.xml", "Package/Options/A.pak", "Package/Options/A.sig",
            "Package/Options/B.pak", "Package/Options/B.sig", "Package/readme.md"];
        var result = Inspect("sifu", paths);
        Assert.Equal(ModLayoutGroupKind.PackageReview, Assert.Single(result.Groups).Kind);
        AssertConserved(paths, result);
    }

    [Fact]
    public void Metadata_is_a_review_boundary_not_unvalidated_permission_to_split_a_package()
    {
        string[] paths = ["Package/manifest.json", "Package/A.pak", "Package/A.sig", "Package/B.pak", "Package/B.sig"];
        var result = Inspect("sifu", paths);
        Assert.Equal(ModLayoutGroupKind.PackageReview, Assert.Single(result.Groups).Kind);
        AssertConserved(paths, result);
    }

    [Fact]
    public void Shared_loader_contents_stay_together_and_do_not_hide_unrelated_siblings()
    {
        string[] paths = [
            "UE4SS/Mods/A/Scripts/main.lua", "UE4SS/Mods/B/Scripts/main.lua", "UE4SS/UE4SS.dll",
            "Outfit.pak", "Outfit.sig"];
        var result = Inspect("sifu", paths);
        Assert.Equal(2, result.Groups.Count);
        Assert.Contains(result.Groups, group => group.Kind == ModLayoutGroupKind.PackageReview && group.Files.Count == 3);
        Assert.Contains(result.Groups, group => group.Kind == ModLayoutGroupKind.CompanionPattern);
        AssertConserved(paths, result);
    }

    [Fact]
    public void Unknown_games_do_not_inherit_Sifu_semantics_from_extensions()
    {
        string[] paths = ["A.pak", "A.sig", "archive.zip"];
        var result = Inspect("unknown-game", paths);
        Assert.Equal("generic/v1", result.ProfileId);
        Assert.DoesNotContain(result.Groups, group => group.Kind == ModLayoutGroupKind.CompanionPattern);
        Assert.Contains(result.Groups, group => group.Kind == ModLayoutGroupKind.ArchiveReview);
        AssertConserved(paths, result);
    }

    [Fact]
    public void Two_thousand_nested_sifu_patterns_are_all_accounted_for_deterministically()
    {
        var paths = Enumerable.Range(0, 2000).SelectMany(index => new[]
        {
            $"Collection/Downloads/Archive/Outfits/{index:D4}/Outfit.pak",
            $"Collection/Downloads/Archive/Outfits/{index:D4}/Outfit.sig"
        }).ToArray();
        var first = Inspect("sifu", paths);
        var reversed = Inspect("sifu", paths.Reverse().ToArray());
        Assert.Equal(2000, first.Groups.Count);
        Assert.Equal(first.Groups.Select(group => group.RelativePath), reversed.Groups.Select(group => group.RelativePath));
        AssertConserved(paths, first);
        AssertConserved(paths, reversed);
    }

    [Theory]
    [InlineData("../outside.pak")]
    [InlineData("C:/outside.pak")]
    [InlineData("/outside.pak")]
    public void Existing_unsafe_path_rejection_is_preserved(string path)
    {
        Assert.Throws<InvalidDataException>(() => Inspect("sifu", path));
    }

    [Fact]
    public void Missing_inventory_is_not_reported_as_a_successful_empty_inspection()
    {
        var legacy = new ModBoundaryResult([]);
        Assert.Throws<InvalidOperationException>(() => ModLayoutPreview.Inspect(legacy, Game("sifu"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_observed_empty_inventory_is_distinct_from_missing_evidence()
    {
        var result = Inspect("sifu");
        Assert.Equal(0, result.FileCount);
        Assert.Empty(result.Groups);
    }
}
