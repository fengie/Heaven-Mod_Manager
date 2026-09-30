using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GameBananaCatalogProviderTests
{
    [Fact]
    public void Capabilities_do_not_claim_search_or_direct_download()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"))));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));

        Assert.True(provider.Capabilities.HasFlag(CatalogProviderCapabilities.Browse));
        Assert.True(provider.Capabilities.HasFlag(CatalogProviderCapabilities.BrowserAssistedDownload));
        Assert.False(provider.Capabilities.HasFlag(CatalogProviderCapabilities.Search));
        Assert.False(provider.Capabilities.HasFlag(CatalogProviderCapabilities.DirectDownload));
    }

    [Fact]
    public async Task Provider_exposes_Monster_Hunter_World_GameBanana_identity()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"))));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));

        var games = await provider.GetGamesAsync(TestContext.Current.CancellationToken);

        var game = Assert.Single(games);
        Assert.Equal("monster-hunter-world", game.Id);
        Assert.Equal("Monster Hunter: World", game.DisplayName);
        Assert.Equal("9081", game.ProviderGameId);
    }

    [Fact]
    public async Task Latest_browse_is_game_scoped_and_hydrates_details()
    {
        var listCalls = 0;
        var detailCalls = 0;
        var handler = new RoutingHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/Core/List/New")
            {
                listCalls++;
                var query = request.RequestUri?.Query ?? string.Empty;
                Assert.Contains("gameid=9081", query, StringComparison.Ordinal);
                Assert.Contains("include_updated=0", query, StringComparison.Ordinal);
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json")));
            }

            if (request.RequestUri?.AbsolutePath == "/Core/Item/Data")
            {
                detailCalls++;
                Assert.Contains("itemid=653359", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var client = new HttpClient(handler);
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var result = await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest, Limit: 1),
            TestContext.Current.CancellationToken);

        var mod = Assert.Single(result);
        Assert.Equal("gamebanana:653359", mod.CanonicalId);
        Assert.Equal(1, listCalls);
        Assert.Equal(1, detailCalls);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Connected, health.State);
    }

    [Fact]
    public async Task Recently_updated_browse_sets_include_updated_without_inventing_search()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            Assert.Equal("/Core/List/New", request.RequestUri?.AbsolutePath);
            Assert.Contains("include_updated=1", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"));
        });

        using var client = new HttpClient(handler);
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var result = await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.RecentlyUpdated, Limit: 5),
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Connected, health.State);
    }

    [Fact]
    public async Task Query_and_unsupported_browse_modes_fail_closed_before_transport()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Unsupported discovery must not reach transport.")));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(
                    game,
                    Query: "weapon",
                    Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Trending),
                TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Popular),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Mod_files_are_hydrated_from_the_same_strict_detail_record()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            Assert.Equal("/Core/Item/Data", request.RequestUri?.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")));
        });

        using var client = new HttpClient(handler);
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var files = await provider.GetModFilesAsync(
            game,
            "653359",
            TestContext.Current.CancellationToken);

        var file = Assert.Single(files);
        Assert.Equal("1625805", file.ProviderFileId);
        Assert.Equal("653359", file.ProviderModId);
    }

    [Fact]
    public async Task Schema_drift_marks_provider_limited()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/Core/List/New")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, "[[\"Mod\",653359]]"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"unexpected\":true}"));
        });

        using var client = new HttpClient(handler);
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest, Limit: 1),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    [Fact]
    public async Task Rate_limit_marks_health_and_preserves_retry_after()
    {
        var handler = new RoutingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(19));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<GameBananaTransportException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.RateLimited, health.State);
        Assert.NotNull(health.RateLimit?.RetryAfter);
    }

    [Fact]
    public async Task Timeout_and_network_failure_mark_provider_offline()
    {
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        using (var timeoutClient = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("fixture timeout")))))
        {
            var provider = new GameBananaCatalogProvider(new GameBananaTransport(timeoutClient));
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => provider.SearchModsAsync(
                    new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest),
                    TestContext.Current.CancellationToken));

            var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CatalogProviderState.Offline, health.State);
            Assert.Contains("timed out", health.Message, StringComparison.OrdinalIgnoreCase);
        }

        using (var offlineClient = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture offline")))))
        {
            var provider = new GameBananaCatalogProvider(new GameBananaTransport(offlineClient));
            await Assert.ThrowsAsync<HttpRequestException>(
                () => provider.SearchModsAsync(
                    new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest),
                    TestContext.Current.CancellationToken));

            var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CatalogProviderState.Offline, health.State);
            Assert.Contains("unreachable", health.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Caller_cancellation_does_not_reclassify_provider_health()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var client = new HttpClient(new RoutingHandler((_, ct) =>
            Task.FromCanceled<HttpResponseMessage>(ct)));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest),
                cancellation.Token));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    [Fact]
    public async Task Acquisition_is_provider_scoped_and_browser_assisted()
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadFixture("mod.json"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document);
        var file = Assert.Single(mod.Files);

        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Acquisition must not perform an API request.")));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Assisted, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Equal(
            "https://gamebanana.com/dl/1625805",
            resolution.AssistedUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Acquisition_rejects_invalid_mod_identity_before_opening_assisted_page()
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadFixture("mod.json"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document) with
        {
            ProviderModId = "not-a-positive-id"
        };
        var file = Assert.Single(mod.Files);

        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Invalid acquisition must not perform an API request.")));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ResolveAcquisitionAsync(
                new CatalogAcquisitionRequest(game, mod, file),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Acquisition_rejects_cross_provider_identity_mismatch()
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadFixture("mod.json"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = GameBananaCatalogNormalizer.NormalizeMod(game, "653359", document);
        var file = Assert.Single(mod.Files) with { ProviderId = "nexus" };

        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Invalid acquisition must not perform an API request.")));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ResolveAcquisitionAsync(
                new CatalogAcquisitionRequest(game, mod, file),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Game_without_GameBanana_identity_fails_before_transport()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Missing game identity must not reach transport.")));
        var provider = new GameBananaCatalogProvider(new GameBananaTransport(client));
        var game = GameProfile.Generic(
            "fixture",
            "Fixture Game",
            Path.GetTempPath(),
            "fixture.exe",
            "mods");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Mode: CatalogBrowseMode.Latest),
                TestContext.Current.CancellationToken));
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "GameBanana", name),
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
