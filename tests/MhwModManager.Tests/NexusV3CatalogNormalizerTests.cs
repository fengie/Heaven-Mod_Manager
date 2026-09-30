using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3CatalogNormalizerTests
{
    [Fact]
    public void Trending_fixture_normalizes_to_provider_neutral_catalog_mods()
    {
        using var document = ReadFixture("trending.json");

        var mods = NexusV3CatalogNormalizer.NormalizeTrendingMods(
            "monsterhunterworld",
            "monsterhunterworld",
            document);

        Assert.Equal(2, mods.Count);
        Assert.Equal("nexus:101", mods[0].CanonicalId);
        Assert.Equal("nexus", mods[0].ProviderId);
        Assert.Equal("101", mods[0].ProviderModId);
        Assert.Equal("Fixture One", mods[0].Name);
        Assert.Equal("AuthorOne", mods[0].Author);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101",
            mods[0].SourceUrl);
        Assert.Equal("nexus:202", mods[1].CanonicalId);
    }

    [Fact]
    public void Detail_fixture_preserves_global_id_only_as_safe_provider_metadata()
    {
        using var document = ReadFixture("mod.json");

        var mod = NexusV3CatalogNormalizer.NormalizeModDetails(
            "monsterhunterworld",
            "monsterhunterworld",
            document);

        Assert.Equal("nexus:101", mod.CanonicalId);
        Assert.Equal("Fixture One", mod.Name);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101",
            mod.SourceUrl);
        Assert.NotNull(mod.ProviderMetadata);

        using var metadata = JsonDocument.Parse(mod.ProviderMetadata!);
        Assert.Equal(
            "fixture-mod-global-id",
            metadata.RootElement.GetProperty("global_mod_id").GetString());
        Assert.Equal(
            "monsterhunterworld",
            metadata.RootElement.GetProperty("game_domain").GetString());
    }

    [Fact]
    public void File_fixture_normalizes_without_persisting_download_urls()
    {
        using var document = ReadFixture("mod-files.json");

        var files = NexusV3CatalogNormalizer.NormalizeModFiles("101", document);

        var file = Assert.Single(files);
        Assert.Equal("nexus", file.ProviderId);
        Assert.Equal("101", file.ProviderModId);
        Assert.Equal("fixture-file-id", file.ProviderFileId);
        Assert.Equal("Main File", file.Name);
        Assert.Equal(CatalogFileCategory.Unknown, file.Category);
        Assert.NotNull(file.ProviderMetadata);
        Assert.DoesNotContain("http", file.ProviderMetadata!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_category_mapping_covers_user_visible_nexus_groups()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "mod_files": [
                  { "id": "1", "name": "Main", "category_name": "MAIN" },
                  { "id": "2", "name": "Optional", "category_name": "Optional Files" },
                  { "id": "3", "name": "Update", "category_name": "Updates" },
                  { "id": "4", "name": "Misc", "category_name": "Miscellaneous" },
                  { "id": "5", "name": "Old", "category_name": "Old Versions" }
                ]
              }
            }
            """);

        var files = NexusV3CatalogNormalizer.NormalizeModFiles("101", document);

        Assert.Collection(
            files,
            item => Assert.Equal(CatalogFileCategory.Main, item.Category),
            item => Assert.Equal(CatalogFileCategory.Optional, item.Category),
            item => Assert.Equal(CatalogFileCategory.Update, item.Category),
            item => Assert.Equal(CatalogFileCategory.Miscellaneous, item.Category),
            item => Assert.Equal(CatalogFileCategory.OldVersion, item.Category));
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
                    "author": "Unknown",
                    "summary": "No provider identity",
                    "mod_page_url": "https://example.com/mods/101"
                  }
                ]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3CatalogNormalizer.NormalizeTrendingMods(
                "monsterhunterworld",
                "monsterhunterworld",
                document));

        Assert.Contains("trusted Nexus source URL", exception.Message, StringComparison.Ordinal);
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
            () => NexusV3CatalogNormalizer.NormalizeModFiles("101", document));

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
