using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GameBananaCatalogNormalizerTests
{
    [Fact]
    public void New_mod_list_normalizes_positive_mod_ids_in_order()
    {
        using var document = ReadFixture("new-mods.json");

        var ids = GameBananaCatalogNormalizer.NormalizeNewModIds(document);

        Assert.Equal(2, ids.Count);
        Assert.Equal("653359", ids[0]);
        Assert.Equal("556536", ids[1]);
    }

    [Fact]
    public void Mod_fixture_normalizes_source_identity_metadata_and_files()
    {
        using var document = ReadFixture("mod.json");
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document);

        Assert.Equal("gamebanana:653359", mod.CanonicalId);
        Assert.Equal("gamebanana", mod.ProviderId);
        Assert.Equal("653359", mod.ProviderModId);
        Assert.Equal(game.Id, mod.GameId);
        Assert.Equal("Effecient Seliana Suite", mod.Name);
        Assert.Equal("Valoe", mod.Author);
        Assert.Equal("Other/Misc", mod.Category);
        Assert.Equal(12, mod.Downloads);
        Assert.Equal("https://gamebanana.com/mods/653359", mod.SourceUrl);
        Assert.Equal(
            "https://gamebanana.com/mods/embeddables/653359?variant=sd_image",
            mod.Thumbnail);
        Assert.Equal(0, mod.Endorsements);
        Assert.Null(mod.ProviderMetadata);

        var file = Assert.Single(mod.Files);
        Assert.Equal("1625805", file.ProviderFileId);
        Assert.Equal(
            "effecient_seliana_20-6020-2-0-1643281679.zip",
            file.FileName);
        Assert.Equal(CatalogFileCategory.Main, file.Category);
        Assert.Equal(11695, file.SizeBytes);
        Assert.Equal("File passed preliminary analysis", file.Description);
        Assert.Null(file.ProviderMetadata);
    }

    [Fact]
    public void Lookalike_source_hostname_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Bad Host",
              "Game().name":"Monster Hunter: World",
              "Url().sProfileUrl()":"https://evilgamebanana.com/mods/653359"
            }
            """);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = Assert.Throws<InvalidDataException>(
            () => GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document));

        Assert.Contains("trusted GameBanana HTTPS URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_mod_source_identity_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Wrong ID",
              "Game().name":"Monster Hunter: World",
              "Url().sProfileUrl()":"https://gamebanana.com/mods/999"
            }
            """);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = Assert.Throws<InvalidDataException>(
            () => GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document));

        Assert.Contains("expected mod identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_game_detail_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Wrong Game",
              "Game().name":"Monster Hunter Rise",
              "Url().sProfileUrl()":"https://gamebanana.com/mods/653359"
            }
            """);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = Assert.Throws<InvalidDataException>(
            () => GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document));

        Assert.Contains("different game", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void New_mod_list_rejects_non_mod_entry_for_mod_only_request()
    {
        using var document = JsonDocument.Parse(
            """
            [
              ["Wip", 123]
            ]
            """);

        var exception = Assert.Throws<InvalidDataException>(
            () => GameBananaCatalogNormalizer.NormalizeNewModIds(document));

        Assert.Contains("non-Mod entry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_file_shape_fails_closed()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "name":"Bad Files",
              "Game().name":"Monster Hunter: World",
              "Url().sProfileUrl()":"https://gamebanana.com/mods/653359",
              "Files().aFiles()": {
                "6020": 42
              }
            }
            """);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = Assert.Throws<InvalidDataException>(
            () => GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document));

        Assert.Contains("must be an object", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_error_object_fails_closed_without_becoming_catalog_data()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "error":"item not found",
              "error_code":"INVALID_PARAMS"
            }
            """);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = Assert.Throws<InvalidDataException>(
            () => GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document));

        Assert.Contains("error object", exception.Message, StringComparison.Ordinal);
    }

    private static JsonDocument ReadFixture(string name)
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "GameBanana", name),
            Encoding.UTF8);
        return JsonDocument.Parse(json);
    }
}
