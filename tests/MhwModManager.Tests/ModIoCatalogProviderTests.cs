using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ModIoCatalogProviderTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private static readonly Uri ApiBaseUri = new("https://u-999.modapi.io/v1/");

    [Fact]
    public void Compliance_record_is_current_api_only_and_allows_provider_authorized_downloads()
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
        Assert.Equal(new Uri("https://mod.io/apiterms"), compliance.TermsUri);
    }

    [Fact]
    public void Transport_rejects_non_modio_credential_hosts()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"))));

        Assert.Throws<ArgumentException>(
            () => new ModIoTransport(
                client,
                ApiKey,
                new Uri("https://evil.example/v1/")));
    }

    [Fact]
    public async Task Transport_sends_scoped_read_query_and_never_echoes_api_key_in_errors()
    {
        var observed = false;
        using var client = new HttpClient(new RoutingHandler((request, _) =>
        {
            observed = true;
            Assert.Equal("u-999.modapi.io", request.RequestUri?.Host);
            Assert.Equal("/v1/games/123/mods", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("api_key=" + ApiKey, query, StringComparison.Ordinal);
            Assert.Contains("_q=weapon", query, StringComparison.Ordinal);
            Assert.Contains("_sort=-date_updated", query, StringComparison.Ordinal);
            Assert.True(request.Headers.TryGetValues("X-Modio-Platform", out var platforms));
            Assert.Equal("windows", Assert.Single(platforms));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));

        var transport = new ModIoTransport(client, ApiKey, ApiBaseUri);
        var exception = await Assert.ThrowsAsync<ModIoTransportException>(
            () => transport.GetModsAsync(
                123,
                "weapon",
                "-date_updated",
                0,
                25,
                TestContext.Current.CancellationToken));

        Assert.True(observed);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Transport_sanitizes_network_failures_that_echo_the_request_uri()
    {
        using var client = new HttpClient(new RoutingHandler((request, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException($"fixture failure for {request.RequestUri}"))));

        var transport = new ModIoTransport(client, ApiKey, ApiBaseUri);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetModsAsync(
                123,
                null,
                "-date_live",
                0,
                10,
                TestContext.Current.CancellationToken));

        Assert.Equal("mod.io API request failed before receiving a response.", exception.Message);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transport_preserves_retry_after_without_reading_error_body()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    "{\"error\":{\"message\":\"do not echo this body\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(57));
            return Task.FromResult(response);
        }));

        var transport = new ModIoTransport(client, ApiKey, ApiBaseUri);
        var exception = await Assert.ThrowsAsync<ModIoTransportException>(
            () => transport.GetModsAsync(
                123,
                null,
                "-date_live",
                0,
                10,
                TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(57), exception.RetryAfter);
        Assert.DoesNotContain("do not echo", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalizer_preserves_identity_but_not_dynamic_download_urls()
    {
        var game = CreateGame();

        using var modsDocument = JsonDocument.Parse(ReadFixture("mods.json"));
        var mod = Assert.Single(ModIoCatalogNormalizer.NormalizeMods(game, 123, modsDocument));
        var embeddedFile = Assert.Single(mod.Files);

        Assert.Equal("modio:123:42", mod.CanonicalId);
        Assert.Equal("420", embeddedFile.ProviderFileId);
        Assert.Contains("Weapons", mod.Tags);
        Assert.DoesNotContain("binary_url", embeddedFile.ProviderMetadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expiring-secret-token", embeddedFile.ProviderMetadata ?? string.Empty, StringComparison.Ordinal);

        using var filesDocument = JsonDocument.Parse(ReadFixture("files.json"));
        var file = Assert.Single(ModIoCatalogNormalizer.NormalizeModFiles("123:42", filesDocument));

        Assert.Equal("2.0", file.Version);
        Assert.DoesNotContain("binary_url", file.ProviderMetadata ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expiring-secret-token", file.ProviderMetadata ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizer_rejects_cross_game_and_external_download_identity()
    {
        var game = CreateGame();
        using var modDocument = JsonDocument.Parse(ReadFixture("mod.json"));

        Assert.Throws<InvalidDataException>(
            () => ModIoCatalogNormalizer.NormalizeMod(game, 999, "999:42", modDocument));

        using var maliciousDownload = JsonDocument.Parse(
            """
            {
              "id": 420,
              "mod_id": 42,
              "virus_status": 1,
              "virus_positive": 0,
              "download": {
                "binary_url": "https://evil.example/mod.zip",
                "date_expires": 4102444800
              }
            }
            """);

        Assert.Throws<InvalidDataException>(
            () => ModIoCatalogNormalizer.NormalizeAcquisitionFile(
                "123:42",
                "420",
                maliciousDownload));
    }

    [Fact]
    public async Task Provider_supports_full_text_query_and_browse_sorting()
    {
        using var client = new HttpClient(new RoutingHandler((request, _) =>
        {
            Assert.Equal("/v1/games/123/mods", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("_q=weapon", query, StringComparison.Ordinal);
            Assert.Contains("_sort=-downloads_today", query, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mods.json")));
        }));

        var provider = CreateProvider(client);
        var result = await provider.SearchModsAsync(
            new CatalogBrowseRequest(
                CreateGame(),
                Query: "weapon",
                Mode: CatalogBrowseMode.Trending,
                Limit: 25),
            TestContext.Current.CancellationToken);

        var mod = Assert.Single(result);
        Assert.Equal("123:42", mod.ProviderModId);
        Assert.True(provider.Capabilities.HasFlag(CatalogProviderCapabilities.Search));
        Assert.True(provider.Capabilities.HasFlag(CatalogProviderCapabilities.DirectDownload));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Connected, health.State);
    }

    [Fact]
    public async Task Direct_acquisition_refetches_fresh_file_url_and_returns_expiry()
    {
        var game = CreateGame();
        using var modDocument = JsonDocument.Parse(ReadFixture("mod.json"));
        using var filesDocument = JsonDocument.Parse(ReadFixture("files.json"));
        var mod = ModIoCatalogNormalizer.NormalizeMod(game, 123, "123:42", modDocument);
        var file = Assert.Single(ModIoCatalogNormalizer.NormalizeModFiles("123:42", filesDocument));

        var fileFetches = 0;
        using var client = new HttpClient(new RoutingHandler((request, _) =>
        {
            Assert.Equal("/v1/games/123/mods/42/files/420", request.RequestUri?.AbsolutePath);
            fileFetches++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("file.json")));
        }));

        var provider = CreateProvider(client);
        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, fileFetches);
        Assert.Equal(CatalogAcquisitionKind.Direct, resolution.Kind);
        Assert.Contains("fresh-expiring-token", resolution.DownloadUri?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(resolution.ExpiresAt);
    }

    [Fact]
    public async Task Potentially_harmful_modio_scan_result_blocks_automatic_acquisition()
    {
        var game = CreateGame();
        using var modDocument = JsonDocument.Parse(ReadFixture("mod.json"));
        using var filesDocument = JsonDocument.Parse(ReadFixture("files.json"));
        var mod = ModIoCatalogNormalizer.NormalizeMod(game, 123, "123:42", modDocument);
        var file = Assert.Single(ModIoCatalogNormalizer.NormalizeModFiles("123:42", filesDocument));

        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("file-threat.json")))));

        var provider = CreateProvider(client);
        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Unavailable, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Contains("unsafe", resolution.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rate_limit_and_auth_failures_are_visible_in_provider_health()
    {
        using (var rateClient = new HttpClient(new RoutingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(19));
            return Task.FromResult(response);
        })))
        {
            var provider = CreateProvider(rateClient);
            await Assert.ThrowsAsync<ModIoTransportException>(
                () => provider.SearchModsAsync(
                    new CatalogBrowseRequest(CreateGame()),
                    TestContext.Current.CancellationToken));
            var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CatalogProviderState.RateLimited, health.State);
            Assert.NotNull(health.RateLimit?.RetryAfter);
        }

        using (var authClient = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))))
        {
            var provider = CreateProvider(authClient);
            await Assert.ThrowsAsync<ModIoTransportException>(
                () => provider.SearchModsAsync(
                    new CatalogBrowseRequest(CreateGame()),
                    TestContext.Current.CancellationToken));
            var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CatalogProviderState.AuthenticationRequired, health.State);
        }
    }

    [Fact]
    public async Task Provider_rejects_cross_game_provider_identity_before_transport()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Cross-game identity must not reach mod.io transport.")));
        var provider = CreateProvider(client);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetModAsync(
                CreateGame(),
                "999:42",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unmapped_game_fails_before_transport()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Unmapped game must not reach mod.io transport.")));
        var provider = CreateProvider(client);
        var game = GameProfile.Generic(
            "other-game",
            "Other Game",
            Path.GetTempPath(),
            "other.exe",
            "mods");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game),
                TestContext.Current.CancellationToken));
    }

    private static ModIoCatalogProvider CreateProvider(HttpClient client)
    {
        return new ModIoCatalogProvider(
            new ModIoTransport(client, ApiKey, ApiBaseUri),
            [new ModIoCatalogGameSource("fixture-game", "Fixture Game", 123)]);
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

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "ModIo", name),
            Encoding.UTF8);
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
}
