using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3CatalogProviderTests
{
    [Fact]
    public async Task Trending_search_uses_provider_neutral_normalizer()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RoutingHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.nexusmods.com/v3/games/monsterhunterworld/trending-mods",
                request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json")));
        });

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var result = await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, Query: "fixture one", Limit: 10), ct);

        var mod = Assert.Single(result);
        Assert.Equal("nexus:101", mod.CanonicalId);
        Assert.Equal("101", mod.ProviderModId);

        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    [Fact]
    public async Task File_lookup_hydrates_global_mod_id_then_expands_exact_versions()
    {
        var ct = TestContext.Current.CancellationToken;
        var requests = new List<string>();
        var handler = new RoutingHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            requests.Add(path);

            return path switch
            {
                "/v3/games/monsterhunterworld/mods/101" =>
                    Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json"))),
                "/v3/mods/fixture-mod-global-id/files" =>
                    Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod-files.json"))),
                "/v3/mod-files/fixture-file-id/versions" =>
                    Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod-file-versions.json"))),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            };
        });

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(
            new NexusV3Transport(client),
            NexusV3Credential.ApiKey("fixture-key"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var files = await provider.GetModFilesAsync(game, "101", ct);

        Assert.Equal(
            [
                "/v3/games/monsterhunterworld/mods/101",
                "/v3/mods/fixture-mod-global-id/files",
                "/v3/mod-files/fixture-file-id/versions"
            ],
            requests);
        Assert.Equal(2, files.Count);
        Assert.Equal(CatalogFileCategory.Main, files[0].Category);
        Assert.Equal("1.2.0", files[0].Version);
        Assert.True(files[0].Recommended);
        Assert.Equal(CatalogFileCategory.Optional, files[1].Category);

        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.Connected, health.State);
    }

    [Fact]
    public async Task Unavailable_mod_details_return_null_without_schema_drift()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "data": {
                    "id": "fixture-hidden-global-id",
                    "game_scoped_id": "303",
                    "game_id": "fixture-game-global-id",
                    "name": null
                  }
                }
                """)));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(
            new NexusV3Transport(client),
            NexusV3Credential.ApiKey("fixture-key"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = await provider.GetModAsync(game, "303", ct);

        Assert.Null(mod);
        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.Connected, health.State);
    }

    [Fact]
    public async Task Missing_credentials_mark_authentication_required()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetModAsync(game, "101", ct));

        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.AuthenticationRequired, health.State);
    }

    [Fact]
    public async Task Auth_failure_marks_provider_authentication_required_without_secret_leak()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RoutingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(
            new NexusV3Transport(client),
            NexusV3Credential.ApiKey("secret-that-must-not-leak"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = await Assert.ThrowsAsync<NexusV3TransportException>(
            () => provider.GetModAsync(game, "101", ct));

        Assert.DoesNotContain("secret-that-must-not-leak", exception.Message, StringComparison.Ordinal);
        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.AuthenticationRequired, health.State);
    }

    [Fact]
    public async Task Rate_limit_marks_health_and_preserves_retry_after_without_retrying()
    {
        var ct = TestContext.Current.CancellationToken;
        var calls = 0;
        var handler = new RoutingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter =
                new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(
            new NexusV3Transport(client),
            NexusV3Credential.ApiKey("fixture-key"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<NexusV3TransportException>(
            () => provider.GetModAsync(game, "101", ct));

        Assert.Equal(1, calls);
        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.RateLimited, health.State);
        Assert.NotNull(health.RateLimit?.RetryAfter);
    }

    [Fact]
    public async Task Malformed_inner_schema_fails_closed_and_marks_provider_limited()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"data":{"mods":[{"name":"Missing identity"}]}}""")));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.SearchModsAsync(new CatalogBrowseRequest(game), ct));

        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.Limited, health.State);
        Assert.Contains("schema drift", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transport_timeout_marks_provider_offline()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RoutingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("fixture timeout")));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => provider.SearchModsAsync(new CatalogBrowseRequest(game), ct));

        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.Offline, health.State);
        Assert.Contains("timed out", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_failure_marks_provider_offline()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RoutingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture offline")));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.SearchModsAsync(new CatalogBrowseRequest(game), ct));

        var health = await provider.GetHealthAsync(ct);
        Assert.Equal(CatalogProviderState.Offline, health.State);
    }

    [Fact]
    public async Task Acquisition_remains_assisted_and_provider_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json")))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = new CatalogMod(
            "nexus:101",
            "nexus",
            "101",
            game.Id,
            "Fixture",
            "",
            "",
            "Author",
            null,
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
            "https://www.nexusmods.com/monsterhunterworld/mods/101",
            []);
        var file = new CatalogModFile(
            "nexus",
            "101",
            "fixture-version-id",
            "Main",
            "Main",
            CatalogFileCategory.Main);

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file), ct);

        Assert.Equal(CatalogAcquisitionKind.Assisted, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101?tab=files",
            resolution.AssistedUri?.AbsoluteUri);
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "NexusV3", name),
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
