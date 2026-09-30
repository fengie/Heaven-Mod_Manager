using System.Net;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ModDbFeedCatalogProviderTests
{
    [Fact]
    public async Task Feed_item_normalizes_to_catalog_and_acquisition_stays_browser_assisted()
    {
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            Assert.Equal("https://rss.moddb.com/games/fixture-game/downloads/feed/rss.xml", request.RequestUri?.AbsoluteUri);
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));
            return Task.FromResult(RssResponse(ReadFixture()));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = CreateGame();

        var mods = await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, "fixture"),
            TestContext.Current.CancellationToken);

        var mod = Assert.Single(mods);
        Assert.Equal("moddb-rss", mod.ProviderId);
        Assert.Equal("Fixture MHW Download", mod.Name);
        Assert.Equal("Fixture release summary.", mod.Summary);
        Assert.Equal("Fixture Author", mod.Author);
        Assert.Contains("moddb", mod.Tags);
        Assert.Contains("Full Version", mod.Tags);
        Assert.False(provider.Capabilities.HasFlag(CatalogProviderCapabilities.DirectDownload));
        Assert.True(provider.Capabilities.HasFlag(CatalogProviderCapabilities.BrowserAssistedDownload));

        var file = Assert.Single(mod.Files);
        Assert.Equal(mod.ProviderModId, file.ProviderFileId);

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Assisted, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Equal(mod.SourceUrl, resolution.AssistedUri?.AbsoluteUri);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Repeated_feed_reads_revalidate_with_etag_and_last_modified()
    {
        var calls = 0;
        var lastModified = new DateTimeOffset(2026, 9, 30, 2, 30, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Empty(request.Headers.IfNoneMatch);
                Assert.Null(request.Headers.IfModifiedSince);
                var response = RssResponse(ReadFixture());
                response.Headers.ETag =
                    new System.Net.Http.Headers.EntityTagHeaderValue("\"moddb-fixture-v1\"");
                response.Content.Headers.LastModified = lastModified;
                return Task.FromResult(response);
            }

            Assert.Equal(
                "\"moddb-fixture-v1\"",
                Assert.Single(request.Headers.IfNoneMatch).ToString());
            Assert.Equal(lastModified, request.Headers.IfModifiedSince);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = CreateGame();
        var request = new CatalogBrowseRequest(game, "fixture");

        var first = await provider.SearchModsAsync(
            request,
            TestContext.Current.CancellationToken);
        var second = await provider.SearchModsAsync(
            request,
            TestContext.Current.CancellationToken);

        var firstMod = Assert.Single(first);
        Assert.Equal(firstMod.CanonicalId, Assert.Single(second).CanonicalId);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Wrong_game_acquisition_fails_before_second_feed_request()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(RssResponse(ReadFixture()));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = CreateGame();
        var mod = Assert.Single(await provider.SearchModsAsync(
            new CatalogBrowseRequest(game),
            TestContext.Current.CancellationToken));
        var file = Assert.Single(mod.Files);

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(
                game,
                mod with { GameId = "different-game" },
                file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Unavailable, resolution.Kind);
        Assert.Null(resolution.AssistedUri);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Provider_requires_official_moddb_feed_origin()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("No request expected.")));
        var transport = new SyndicationFeedTransport(client);

        Assert.Throws<ArgumentException>(() =>
            new ModDbFeedCatalogProvider(
                transport,
                [
                    new ModDbFeedSource(
                        "monster-hunter-world",
                        "Monster Hunter: World",
                        new Uri("https://example.test/downloads/feed/rss.xml"),
                        "Downloads")
                ]));

        Assert.Throws<ArgumentException>(() =>
            new ModDbFeedCatalogProvider(
                transport,
                [
                    new ModDbFeedSource(
                        "monster-hunter-world",
                        "Monster Hunter: World",
                        new Uri("http://rss.moddb.com/downloads/feed/rss.xml"),
                        "Downloads")
                ]));

        Assert.Throws<ArgumentException>(() =>
            new ModDbFeedCatalogProvider(
                transport,
                [
                    new ModDbFeedSource(
                        "fixture-game",
                        "Fixture Game",
                        new Uri("https://rss.moddb.com/downloads/feed/rss.xml"),
                        "Downloads")
                ]));

        Assert.Throws<ArgumentException>(() =>
            new ModDbFeedCatalogProvider(
                transport,
                [
                    new ModDbFeedSource(
                        "fixture-game",
                        "Fixture Game",
                        new Uri("https://rss.moddb.com/mods/fixture-mod/downloads/feed/rss.xml"),
                        "Downloads")
                ]));
    }

    [Fact]
    public void Compliance_is_feed_only_requires_attribution_and_forbids_direct_or_html_acquisition()
    {
        var compliance = ModDbFeedCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(compliance, new DateOnly(2026, 9, 30));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.Feed, compliance.SourceKind);
        Assert.True(compliance.AttributionRequired);
        Assert.Equal("Mod DB - Game Development", compliance.AttributionText);
        Assert.False(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
    }

    [Fact]
    public async Task Health_maps_429_to_rate_limited_without_retrying()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogProviderState.RateLimited, health.State);
        Assert.NotNull(health.RateLimit?.RetryAfter);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Malformed_feed_health_fails_limited_not_connected()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new StringContent("<html>not rss</html>", Encoding.UTF8, "application/rss+xml");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    private static GameProfile CreateGame()
    {
        return GameProfile.Generic(
            "fixture-game",
            "Fixture Game",
            Path.Combine(Path.GetTempPath(), "mhw-mod-manager-catalog-fixture-game"),
            "FixtureGame.exe",
            "Mods");
    }

    private static ModDbFeedCatalogProvider CreateProvider(HttpClient client)
    {
        return new ModDbFeedCatalogProvider(
            new SyndicationFeedTransport(client),
            [
                new ModDbFeedSource(
                    "fixture-game",
                    "Fixture Game",
                    new Uri("https://rss.moddb.com/games/fixture-game/downloads/feed/rss.xml"),
                    "Downloads",
                    ["fixture"])
            ]);
    }

    private static string ReadFixture()
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "ModDb", "downloads-rss.xml"),
            Encoding.UTF8);
    }

    private static HttpResponseMessage RssResponse(string xml)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml")
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
