using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3NormalizerTests
{
    [Fact]
    public void Trending_fixture_normalizes_provider_identity_and_source_url()
    {
        using var document = ReadFixture("trending.json");

        var mods = NexusV3Normalizer.NormalizeTrending(document, "monsterhunterworld");

        Assert.Equal(2, mods.Count);

        var first = mods[0];
        Assert.Equal("nexus", first.ProviderId);
        Assert.Equal("101", first.ProviderModId);
        Assert.Equal("nexus:101", first.CanonicalId);
        Assert.Equal("monsterhunterworld", first.GameId);
        Assert.Equal("Fixture One", first.Name);
        Assert.Equal("AuthorOne", first.Author);
        Assert.Equal("First deterministic trending fixture.", first.Summary);
        Assert.Equal("https://www.nexusmods.com/monsterhunterworld/mods/101", first.SourceUrl);
        Assert.Equal("https://staticdelivery.nexusmods.com/fixture-one.jpg", first.Thumbnail);

        var second = mods[1];
        Assert.Equal("202", second.ProviderModId);
        Assert.Null(second.Thumbnail);
    }

    [Fact]
    public void Mod_detail_fixture_surfaces_global_id_for_file_hydration()
    {
        using var document = ReadFixture("mod.json");

        var normalized = NexusV3Normalizer.NormalizeMod(document, "monsterhunterworld");

        Assert.Equal("fixture-mod-global-id", normalized.GlobalModId);
        Assert.Equal("101", normalized.Mod.ProviderModId);
        Assert.Equal("nexus:101", normalized.Mod.CanonicalId);
        Assert.Equal("Fixture One", normalized.Mod.Name);
        Assert.Equal("Fixture mod details", normalized.Mod.Summary);
        Assert.Equal("https://www.nexusmods.com/monsterhunterworld/mods/101", normalized.Mod.SourceUrl);
    }

    [Fact]
    public void File_fixture_normalizes_file_identity_and_timestamp()
    {
        using var document = ReadFixture("mod-files.json");

        var files = NexusV3Normalizer.NormalizeFiles(document, "101");

        var file = Assert.Single(files);
        Assert.Equal("nexus", file.ProviderId);
        Assert.Equal("101", file.ProviderModId);
        Assert.Equal("fixture-file-id", file.ProviderFileId);
        Assert.Equal("Main File", file.Name);
        Assert.Equal("Main File", file.FileName);
        Assert.Equal(CatalogFileCategory.Unknown, file.Category);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero),
            file.UploadedAt);
    }

    [Fact]
    public void Trending_entry_without_provider_identity_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "mods": [
                  {
                    "name": "Broken",
                    "author": "Fixture"
                  }
                ]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3Normalizer.NormalizeTrending(document, "monsterhunterworld"));

        Assert.Contains("mod_page_url", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void File_payload_with_wrong_inner_shape_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "data": {
                "mod_files": {}
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3Normalizer.NormalizeFiles(document, "101"));

        Assert.Contains("data.mod_files array", exception.Message, StringComparison.Ordinal);
    }

    private static JsonDocument ReadFixture(string name)
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "NexusV3", name),
            Encoding.UTF8);

        return JsonDocument.Parse(json);
    }
}
