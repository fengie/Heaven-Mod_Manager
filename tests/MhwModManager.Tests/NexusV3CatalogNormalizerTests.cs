using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3CatalogNormalizerTests
{
    [Fact]
    public void Trending_fixture_normalizes_provider_identity_and_source_url()
    {
        using var document = ReadFixture("trending.json");

        var mods = NexusV3CatalogNormalizer.NormalizeTrendingMods(
            "monster-hunter-world",
            "monsterhunterworld",
            document);

        Assert.Equal(2, mods.Count);
        Assert.Equal("nexus:101", mods[0].CanonicalId);
        Assert.Equal("101", mods[0].ProviderModId);
        Assert.Equal("Fixture One", mods[0].Name);
        Assert.Equal("AuthorOne", mods[0].Author);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101",
            mods[0].SourceUrl);
        Assert.Equal("nexus:202", mods[1].CanonicalId);
    }

    [Fact]
    public void Detail_fixture_surfaces_global_id_without_secret_or_signed_url_state()
    {
        using var document = ReadFixture("mod.json");

        var normalized = NexusV3CatalogNormalizer.NormalizeModDetails(
            "monster-hunter-world",
            "monsterhunterworld",
            document);

        Assert.Equal("fixture-mod-global-id", normalized.GlobalModId);
        Assert.Equal("101", normalized.ProviderModId);
        Assert.Equal("fixture-game-global-id", normalized.GlobalGameId);
        Assert.NotNull(normalized.Mod);
        var mod = normalized.Mod!;
        Assert.Equal("nexus:101", mod.CanonicalId);
        Assert.NotNull(mod.ProviderMetadata);
        Assert.Contains("fixture-mod-global-id", mod.ProviderMetadata!, StringComparison.Ordinal);
        Assert.DoesNotContain("http", mod.ProviderMetadata!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_unavailable_mod_with_null_name_normalizes_as_missing_catalog_item()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "id": "fixture-hidden-global-id",
                "game_scoped_id": "303",
                "game_id": "fixture-game-global-id",
                "name": null
              }
            }
            """);

        var normalized = NexusV3CatalogNormalizer.NormalizeModDetails(
            "monster-hunter-world",
            "monsterhunterworld",
            document);

        Assert.Equal("fixture-hidden-global-id", normalized.GlobalModId);
        Assert.Equal("303", normalized.ProviderModId);
        Assert.Null(normalized.Mod);
    }

    [Fact]
    public void File_group_fixture_normalizes_stable_group_identity()
    {
        using var document = ReadFixture("mod-files.json");

        var groups = NexusV3CatalogNormalizer.NormalizeModFileGroups(document);

        var group = Assert.Single(groups);
        Assert.Equal("fixture-file-id", group.Id);
        Assert.Equal("Main File", group.Name);
    }

    [Fact]
    public void File_version_fixture_normalizes_user_visible_categories()
    {
        using var document = ReadFixture("mod-file-versions.json");

        var files = NexusV3CatalogNormalizer.NormalizeModFileVersions(
            "101",
            "fixture-file-id",
            "Main File",
            document);

        Assert.Equal(2, files.Count);
        Assert.Equal(CatalogFileCategory.Main, files[0].Category);
        Assert.Equal("1.2.0", files[0].Version);
        Assert.True(files[0].Recommended);
        Assert.Equal(CatalogFileCategory.Optional, files[1].Category);
        Assert.False(files[1].Recommended);
    }

    [Fact]
    public void File_category_mapping_covers_catalog_groups()
    {
        Assert.Equal(CatalogFileCategory.Main, NexusV3CatalogNormalizer.ParseFileCategory("MAIN"));
        Assert.Equal(CatalogFileCategory.Optional, NexusV3CatalogNormalizer.ParseFileCategory("Optional Files"));
        Assert.Equal(CatalogFileCategory.Update, NexusV3CatalogNormalizer.ParseFileCategory("Updates"));
        Assert.Equal(CatalogFileCategory.Miscellaneous, NexusV3CatalogNormalizer.ParseFileCategory("Miscellaneous"));
        Assert.Equal(CatalogFileCategory.OldVersion, NexusV3CatalogNormalizer.ParseFileCategory("Old Versions"));
        Assert.Equal(CatalogFileCategory.Archived, NexusV3CatalogNormalizer.ParseFileCategory("Archived"));
        Assert.Equal(CatalogFileCategory.Removed, NexusV3CatalogNormalizer.ParseFileCategory("Removed"));
    }

    [Fact]
    public void Trending_entry_without_strong_provider_identity_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "mods": [
                  {
                    "name": "Ambiguous",
                    "mod_page_url": "https://example.com/mods/101"
                  }
                ]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3CatalogNormalizer.NormalizeTrendingMods(
                "monster-hunter-world",
                "monsterhunterworld",
                document));

        Assert.Contains("trusted Nexus HTTPS URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_game_trending_identity_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "mods": [
                  {
                    "name": "Wrong Game",
                    "mod_page_url": "https://www.nexusmods.com/skyrim/mods/101"
                  }
                ]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3CatalogNormalizer.NormalizeTrendingMods(
                "monster-hunter-world",
                "monsterhunterworld",
                document));

        Assert.Contains("expected game-scoped mod identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_inner_schema_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "mod_files": [
                  { "id": "1", "name": 42 }
                ]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3CatalogNormalizer.NormalizeModFileGroups(document));

        Assert.Contains("'name' must be a string", exception.Message, StringComparison.Ordinal);
    }

    private static JsonDocument ReadFixture(string name)
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "NexusV3", name),
            Encoding.UTF8);
        return JsonDocument.Parse(json);
    }
}
