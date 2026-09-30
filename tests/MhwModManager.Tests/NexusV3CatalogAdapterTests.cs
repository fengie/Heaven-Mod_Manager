using System.Net;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3CatalogAdapterTests
{
    [Fact]
    public void Trending_fixture_normalizes_provider_identity_and_urls()
    {
        using var document = JsonDocument.Parse(ReadFixture("trending.json"));
        var game = GameProfile.MonsterHunterWorld(@"C:\Games\MHW");

        var mods = NexusV3CatalogNormalizer.NormalizeTrending(document, game);

        Assert.Equal(2, mods.Count);
        Assert.Equal("nexus:101", mods[0].CanonicalId);
        Assert.Equal("101", mods[0].ProviderModId);
        Assert.Equal("Fixture One", mods[0].Name);
        Assert.Equal("AuthorOne", mods[0].Author);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101",
            mods[0].SourceUrl);
        Assert.Equal(
            "https://staticdelivery.nexusmods.com/fixture-one.jpg",
            mods[0].Thumbnail);
        Assert.Equal("nexus:202", mods[1].CanonicalId);
        Assert.Null(mods[1].Thumbnail);
    }

    [Fact]
    public void Mod_details_hydrate_global_identity_and_preserve_seed_metadata()
    {
        var game = GameProfile.MonsterHunterWorld(@"C:\Games\MHW");
        using var trendingDocument = JsonDocument.Parse(ReadFixture("trending.json"));
        var seed = NexusV3CatalogNormalizer.NormalizeTrending(trendingDocument, game)[0];

        using var modDocument = JsonDocument.Parse(ReadFixture("mod.json"));
        var mod = NexusV3CatalogNormalizer.NormalizeMod(modDocument, game, seed);

        Assert.Equal("101", mod.ProviderModId);
        Assert.Equal("Fixture One", mod.Name);
        Assert.Equal("AuthorOne", mod.Author);
        Assert.Equal("Fixture mod details", mod.Summary);
        Assert.Equal("fixture-mod-global-id", NexusV3CatalogNormalizer.GetGlobalModId(mod));
        Assert.Contains("monsterhunterworld-game-id", mod.ProviderMetadata!, StringComparison.Ordinal);
    }

    [Fact]
    public void File_versions_normalize_exact_version_identity_and_category()
    {
        using var document = JsonDocument.Parse(ReadFixture("mod-file-versions.json"));

        var files = NexusV3CatalogNormalizer.NormalizeModFileVersions(document, "101");

        var file = Assert.Single(files);
        Assert.Equal("nexus", file.ProviderId);
        Assert.Equal("101", file.ProviderModId);
        Assert.Equal("fixture-version-id", file.ProviderFileId);
        Assert.Equal("1.2.3", file.Version);
        Assert.Equal(CatalogFileCategory.Main, file.Category);
        Assert.True(file.Recommended);
        Assert.Contains("fixture-file-id", file.ProviderMetadata!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adapter_hydrates_global_mod_id_before_file_endpoints()
    {
        var requests = new List<string>();
        var handler = new RecordingHandler((request, _) =>
        {
            var uri = request.RequestUri?.AbsoluteUri
                ?? throw new InvalidOperationException("Request URI was missing.");
            requests.Add(uri);

            if (uri.EndsWith(
                "/games/monsterhunterworld/mods/101",
                StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(ReadFixture("mod.json")));
            }

            if (uri.EndsWith(
                "/mods/fixture-mod-global-id/files",
                StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(ReadFixture("mod-files.json")));
            }

            if (uri.EndsWith(
                "/mod-files/fixture-file-id/versions",
                StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(ReadFixture("mod-file-versions.json")));
            }

            throw new InvalidOperationException("Unexpected Nexus fixture request: " + uri);
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        var adapter = new NexusV3CatalogAdapter(
            transport,
            NexusV3Credential.ApiKey("fixture-key"));
        var game = GameProfile.MonsterHunterWorld(@"C:\Games\MHW");

        var files = await adapter.GetModFilesAsync(game, "101");

        var file = Assert.Single(files);
        Assert.Equal("fixture-version-id", file.ProviderFileId);
        Assert.Equal(
            new[]
            {
                "https://api.nexusmods.com/v3/games/monsterhunterworld/mods/101",
                "https://api.nexusmods.com/v3/mods/fixture-mod-global-id/files",
                "https://api.nexusmods.com/v3/mod-files/fixture-file-id/versions"
            },
            requests);
    }

    [Fact]
    public void Missing_required_inner_field_fails_closed_as_schema_drift()
    {
        const string malformed =
            "{\"data\":{\"id\":\"global\",\"game_scoped_id\":\"101\",\"name\":\"Broken\"}}";
        using var document = JsonDocument.Parse(malformed);
        var game = GameProfile.MonsterHunterWorld(@"C:\Games\MHW");

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3CatalogNormalizer.NormalizeMod(document, game));

        Assert.Contains("game_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trending_url_for_wrong_game_fails_closed()
    {
        const string malformed =
            "{\"data\":{\"mods\":[{\"name\":\"Wrong\",\"mod_page_url\":\"https://www.nexusmods.com/skyrim/mods/1\"}]}}";
        using var document = JsonDocument.Parse(malformed);
        var game = GameProfile.MonsterHunterWorld(@"C:\Games\MHW");

        var exception = Assert.Throws<InvalidDataException>(
            () => NexusV3CatalogNormalizer.NormalizeTrending(document, game));

        Assert.Contains("expected game/mod path", exception.Message, StringComparison.Ordinal);
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "NexusV3", name),
            Encoding.UTF8);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return callback(request, cancellationToken);
        }
    }
}
