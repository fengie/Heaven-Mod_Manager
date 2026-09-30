using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ModIoCatalogProviderTests
{
    private const int ModIoGameId = 123;
    private const string ApiKey = "fixture-api-key";

    [Fact]
    public async Task Browse_uses_official_game_origin_search_filter_and_namespaced_identity()
    {
        var calls = 0;
        using var client = new HttpClient(new RoutingHandler((request, _) =>
        {
            calls++;
            Assert.Equal("g-123.modapi.io", request.RequestUri?.Host);
            Assert.Equal("/v1/games/123/mods", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("_q=dragon", query, StringComparison.Ordinal);
            Assert.Contains("_sort=-date_updated", query, StringComparison.Ordinal);
            Assert.Contains("api_key=fixture-api-key", query, StringComparison.Ordinal);
            Assert.Equal("WINDOWS", Assert.Single(request.Headers.GetValues("X-Modio-Platform")));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ModListJson));
        }));

        var provider = CreateProvider(client);
        var game = CreateGame();

        var result = await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, "dragon", CatalogBrowseMode.RecentlyUpdated, 25),
            TestContext.Current.CancellationToken);

        var mod = Assert.Single(result);
        Assert.Equal("modio:123:456", mod.CanonicalId);
        Assert.Equal("123:456", mod.ProviderModId);
        Assert.Equal("Dragon Fixture", mod.Name);
        Assert.Equal("FixtureAuthor", mod.Author);
        Assert.Equal(321L, mod.Downloads);
        Assert.Empty(mod.Files);
        Assert.Null(mod.ProviderMetadata);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Detail_and_files_never_persist_ephemeral_download_url()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath;
            return path switch
            {
                "/v1/games/123/mods/456/files" =>
                    Task.FromResult(JsonResponse(HttpStatusCode.OK, FileListJson)),
                "/v1/games/123/mods/456" =>
                    Task.FromResult(JsonResponse(HttpStatusCode.OK, ModJson)),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected mod.io request: {path}")
            };
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var mod = await provider.GetModAsync(
            CreateGame(),
            "123:456",
            TestContext.Current.CancellationToken);

        Assert.NotNull(mod);
        var file = Assert.Single(mod!.Files);
        Assert.Equal("123:456", file.ProviderModId);
        Assert.Equal("9001", file.ProviderFileId);
        Assert.Equal("fixture-1.2.3.zip", file.FileName);
        Assert.Null(file.ProviderMetadata);
        Assert.DoesNotContain("download-token", mod.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acquisition_resolves_fresh_download_at_click_time_and_preserves_expiry()
    {
        var calls = 0;
        var handler = new RoutingHandler((request, _) =>
        {
            calls++;
            Assert.Equal("/v1/games/123/mods/456/files/9001", request.RequestUri?.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, FileDetailJson));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = CreateGame();
        var mod = CreateCatalogMod(game);
        var file = CreateCatalogFile();

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Direct, resolution.Kind);
        Assert.Equal(
            "https://g-123.modapi.io/v1/games/123/mods/456/files/9001/download/download-token",
            resolution.DownloadUri?.AbsoluteUri);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800003600), resolution.ExpiresAt);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Acquisition_blocks_file_without_completed_clean_scan()
    {
        var unsafeJson = FileDetailJson
            .Replace("\"virus_status\": 1", "\"virus_status\": 2", StringComparison.Ordinal)
            .Replace("\"virus_positive\": 0", "\"virus_positive\": 2", StringComparison.Ordinal);

        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, unsafeJson))));
        var provider = CreateProvider(client);
        var game = CreateGame();

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, CreateCatalogMod(game), CreateCatalogFile()),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Unavailable, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
    }

    [Fact]
    public async Task Network_failure_does_not_retain_secret_bearing_request_uri()
    {
        using var client = new HttpClient(new RoutingHandler((request, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException($"failed {request.RequestUri}"))));
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(CreateGame()),
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Offline, health.State);
    }

    [Fact]
    public async Task Authentication_failure_does_not_leak_api_key_and_updates_health()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<ModIoTransportException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(CreateGame()),
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.AuthenticationRequired, health.State);
    }

    [Fact]
    public void Compliance_is_official_api_only_and_direct_download_capable()
    {
        var compliance = ModIoCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.True(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
    }

    private static ModIoCatalogProvider CreateProvider(HttpClient client)
    {
        return new ModIoCatalogProvider(
            new ModIoTransport(client, new ModIoCredential(ApiKey)),
            [new ModIoCatalogGameSource("fixture-game", "Fixture Game", ModIoGameId)]);
    }

    private static GameProfile CreateGame()
    {
        return GameProfile.Generic(
            "fixture-game",
            "Fixture Game",
            Path.GetTempPath(),
            "fixture.exe",
            "mods");
    }

    private static CatalogMod CreateCatalogMod(GameProfile game)
    {
        return new CatalogMod(
            "modio:123:456",
            "modio",
            "123:456",
            game.Id,
            "Dragon Fixture",
            "Summary",
            "Description",
            "FixtureAuthor",
            "1.2.3",
            null,
            [],
            null,
            [],
            null,
            null,
            null,
            null,
            null,
            [],
            "https://mod.io/g/fixture-game/m/dragon-fixture",
            [CreateCatalogFile()]);
    }

    private static CatalogModFile CreateCatalogFile()
    {
        return new CatalogModFile(
            "modio",
            "123:456",
            "9001",
            "1.2.3",
            "fixture-1.2.3.zip",
            CatalogFileCategory.Main,
            "1.2.3");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RoutingHandler(
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

    private const string ModListJson = """
    {
      "data": [
        {
          "id": 456,
          "game_id": 123,
          "status": 1,
          "visible": 1,
          "name": "Dragon Fixture",
          "summary": "A test mod",
          "description_plaintext": "A detailed test mod.",
          "profile_url": "https://mod.io/g/fixture-game/m/dragon-fixture",
          "submitted_by": { "username": "FixtureAuthor" },
          "date_added": 1800000000,
          "date_updated": 1800000100,
          "media": { "logo": { "thumb_320x180": "https://assets.modcdn.io/images/fixture.png" } },
          "tags": [ { "name": "Weapons" } ],
          "stats": { "downloads_total": 321 }
        }
      ],
      "result_count": 1,
      "result_limit": 25,
      "result_offset": 0,
      "result_total": 1
    }
    """;

    private const string ModJson = """
    {
      "id": 456,
      "game_id": 123,
      "status": 1,
      "visible": 1,
      "name": "Dragon Fixture",
      "summary": "A test mod",
      "description_plaintext": "A detailed test mod.",
      "profile_url": "https://mod.io/g/fixture-game/m/dragon-fixture",
      "submitted_by": { "username": "FixtureAuthor" },
      "date_added": 1800000000,
      "date_updated": 1800000100,
      "media": { "logo": { "thumb_320x180": "https://assets.modcdn.io/images/fixture.png" } },
      "tags": [ { "name": "Weapons" } ],
      "stats": { "downloads_total": 321 }
    }
    """;

    private const string FileListJson = """
    {
      "data": [
        {
          "id": 9001,
          "mod_id": 456,
          "date_added": 1800000200,
          "virus_status": 1,
          "virus_positive": 0,
          "filesize": 12345,
          "filename": "fixture-1.2.3.zip",
          "version": "1.2.3",
          "changelog": "Fixture changes",
          "download": {
            "binary_url": "https://g-123.modapi.io/v1/games/123/mods/456/files/9001/download/download-token",
            "date_expires": 1800003600
          }
        }
      ],
      "result_count": 1,
      "result_limit": 100,
      "result_offset": 0,
      "result_total": 1
    }
    """;

    private const string FileDetailJson = """
    {
      "id": 9001,
      "mod_id": 456,
      "date_added": 1800000200,
      "virus_status": 1,
      "virus_positive": 0,
      "filesize": 12345,
      "filename": "fixture-1.2.3.zip",
      "version": "1.2.3",
      "changelog": "Fixture changes",
      "download": {
        "binary_url": "https://g-123.modapi.io/v1/games/123/mods/456/files/9001/download/download-token",
        "date_expires": 1800003600
      }
    }
    """;
}
