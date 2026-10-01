using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class CurseForgeCatalogProviderTests
{
    [Fact]
    public void Compliance_is_official_api_only_and_never_html()
    {
        var compliance = CurseForgeCatalogPolicy.Compliance;

        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.True(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
        Assert.Empty(CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public async Task Search_scopes_game_and_keeps_api_key_in_header_only()
    {
        const string apiKey = "fixture-super-secret-key";
        var handler = new RoutingHandler((request, _) =>
        {
            Assert.Equal("/v1/mods/search", request.RequestUri?.AbsolutePath);
            Assert.Contains("gameId=777", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, request.RequestUri?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
            Assert.True(request.Headers.TryGetValues("x-api-key", out var values));
            Assert.Equal(apiKey, Assert.Single(values));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("search.json")));
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, apiKey);

        var mods = await provider.SearchModsAsync(
            new CatalogBrowseRequest(CreateGame(), Query: "hunter", Mode: CatalogBrowseMode.RecentlyUpdated, Limit: 1),
            TestContext.Current.CancellationToken);

        var mod = Assert.Single(mods);
        Assert.Equal("curseforge:123", mod.CanonicalId);
        Assert.Equal("Fixture Hunter Tools", mod.Name);
        Assert.Equal("FixtureAuthor", mod.Author);
        Assert.Equal("https://www.curseforge.com/example/fixture-hunter-tools", mod.SourceUrl);
        var file = Assert.Single(mod.Files);
        Assert.Equal("456", file.ProviderFileId);
        Assert.Equal("fixture-hunter-tools.zip", file.FileName);
        Assert.DoesNotContain(apiKey, mod.SourceUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", mod.SourceUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acquisition_returns_ephemeral_download_url_without_persisting_it_in_mod_metadata()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/mods/search")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("search.json")));
            Assert.Equal("/v1/mods/123/files/456/download-url", request.RequestUri?.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("download-url.json")));
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "fixture-key");
        var game = CreateGame();
        var mod = Assert.Single(await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest, Limit: 1),
            TestContext.Current.CancellationToken));
        var file = Assert.Single(mod.Files);

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.AuthenticatedDirect, resolution.Kind);
        Assert.Equal("download.example", resolution.DownloadUri?.Host);
        Assert.Contains("token=short-lived", resolution.DownloadUri?.Query ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", mod.SourceUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Null(mod.ProviderMetadata);
        Assert.Null(file.ProviderMetadata);
    }

    [Fact]
    public async Task Authentication_failure_is_fail_closed_and_health_is_explicit()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var provider = CreateProvider(client, "fixture-key");

        await Assert.ThrowsAsync<CurseForgeTransportException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(CreateGame(), Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.AuthenticationRequired, health.State);
    }

    [Fact]
    public async Task Rate_limit_preserves_retry_after()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return Task.FromResult(response);
        }));
        var provider = CreateProvider(client, "fixture-key");

        await Assert.ThrowsAsync<CurseForgeTransportException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(CreateGame(), Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.RateLimited, health.State);
        Assert.NotNull(health.RateLimit?.RetryAfter);
    }

    [Fact]
    public async Task Schema_drift_marks_provider_limited()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"data\":{\"unexpected\":true}}"))));
        var provider = CreateProvider(client, "fixture-key");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(CreateGame(), Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    [Fact]
    public async Task Unmapped_game_fails_before_transport()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Unmapped game must not reach CurseForge.")));
        var provider = CreateProvider(client, "fixture-key");
        var other = GameProfile.Generic(
            "other-game",
            "Other Game",
            Path.GetTempPath(),
            "other.exe",
            "mods");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(other, Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unsafe_download_url_is_rejected()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/mods/search")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("search.json")));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"data\":\"http://download.example/file.zip\"}"));
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, "fixture-key");
        var game = CreateGame();
        var mod = Assert.Single(await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest, Limit: 1),
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.ResolveAcquisitionAsync(
                new CatalogAcquisitionRequest(game, mod, Assert.Single(mod.Files)),
                TestContext.Current.CancellationToken));
    }

    private static CurseForgeCatalogProvider CreateProvider(HttpClient client, string apiKey)
    {
        return new CurseForgeCatalogProvider(
            new CurseForgeTransport(client, apiKey, new Uri("https://api.curseforge.test/")),
            [new CurseForgeCatalogGameSource("fixture-game", "Fixture Game", 777)]);
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
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "CurseForge", name),
            Encoding.UTF8);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
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
