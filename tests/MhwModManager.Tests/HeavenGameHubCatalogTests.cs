using System.Net;
using System.Net.Http;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class HeavenGameHubCatalogTests
{
    // An exact, offline copy of the public Heaven.gg games.json schema v1.
    private const string Valid = """
        {
          "schema": "heaven-gaming/game-registry/v1",
          "repository": "fengie/Heaven.gg",
          "games": [
            {"id":"celtic-heroes","name":"Celtic Heroes",
             "experience":"research","siteRoute":"./#celtic-heroes"},
            {"id":"aion2","name":"AION 2",
             "experience":"research","siteRoute":"./#aion2"},
            {"id":"monster-hunter-world","name":"Monster Hunter: World",
             "experience":"windows-companion","siteRoute":"./#mod-manager",
             "companion":{
               "repository":"fengie/Heaven-Mod_Manager",
               "releasePage":"https://github.com/fengie/Heaven-Mod_Manager/releases/latest",
               "sourcePage":"https://github.com/fengie/Heaven-Mod_Manager",
               "platform":"windows-x64","integration":"download-link-only"
             }}
          ]
        }
        """;

    private sealed class StubHandler(string payload, HttpStatusCode code = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(HeavenGameHubCatalog.CatalogAddress, request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    [Theory]
    [InlineData("celtic-heroes", "#celtic-heroes")]
    [InlineData("aion2", "#aion2")]
    [InlineData("monster-hunter-world", "#mod-manager")]
    public async Task Explicit_public_guide_lookup_reads_approved_registry_only(
        string id, string fragment)
    {
        var handler = new StubHandler(Valid);
        using var http = new HttpClient(handler);
        var link = await HeavenGameHubCatalog.FindGuideAsync(id, http, TestContext.Current.CancellationToken);
        Assert.NotNull(link);
        Assert.Equal("https://fengie.github.io/Heaven.gg/" + fragment, link.AbsoluteUri);
        Assert.Equal(1, handler.Requests);
        Assert.Equal("monster-hunter-world",
            GameProfile.MonsterHunterWorld(Path.GetTempPath()).Id);
    }

    [Theory]
    [InlineData("someone-else")]
    [InlineData("mhw")]
    [InlineData("../unsafe")]
    [InlineData("")]
    public async Task Unknown_or_noncanonical_games_never_trigger_public_network_calls(
        string id)
    {
        var handler = new StubHandler(Valid);
        using var http = new HttpClient(handler);
        Assert.Null(await HeavenGameHubCatalog.FindGuideAsync(id, http, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Malformed_and_hostile_catalogs_never_provide_navigation()
    {
        var mutated = new[]
        {
            Valid.Replace("fengie/Heaven.gg", "other/site", StringComparison.Ordinal),
            Valid.Replace("./#aion2", "https://evil.example/", StringComparison.Ordinal),
            Valid.Replace("https://github.com/fengie/Heaven-Mod_Manager/releases/latest",
                "https://evil.example/mod.exe", StringComparison.Ordinal),
            Valid.Replace("\"integration\":\"download-link-only\"",
                "\"integration\":\"remote-install\"", StringComparison.Ordinal),
            Valid.Replace("\"id\":\"aion2\"", "\"id\":\"celtic-heroes\"", StringComparison.Ordinal),
            Valid.Replace("\"siteRoute\":\"./#mod-manager\"",
                "\"siteRoute\":\"javascript:alert(1)\"", StringComparison.Ordinal),
            Valid.Replace("\"platform\":\"windows-x64\"",
                "\"platform\":\"android\"", StringComparison.Ordinal),
            Valid.Replace("\"schema\": \"heaven-gaming/game-registry/v1\"",
                "\"schema\": \"unknown/v1\"", StringComparison.Ordinal),
            Valid.Replace("\"id\":\"aion2\"",
                "\"id\":\"aion2\",\"id\":\"aion2\"", StringComparison.Ordinal),
            Valid.Replace("\"id\":\"aion2\"",
                "\"id\":\"aion2\",\"permissions\":\"file-write\"", StringComparison.Ordinal),
            "{}",
            "[1,2,3]"
        };
        foreach (var payload in mutated)
        {
            using var http = new HttpClient(new StubHandler(payload));
            Assert.Null(await HeavenGameHubCatalog.FindGuideAsync("monster-hunter-world", http, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Oversized_or_redirected_public_catalog_is_rejected()
    {
        using var huge = new HttpClient(new StubHandler(Valid + new string(' ', 20000)));
        Assert.Null(await HeavenGameHubCatalog.FindGuideAsync("monster-hunter-world", huge, TestContext.Current.CancellationToken));

        using var redirected = new HttpClient(new StubHandler(Valid, HttpStatusCode.Redirect));
        Assert.Null(await HeavenGameHubCatalog.FindGuideAsync("monster-hunter-world", redirected, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_remains_cancellable()
    {
        using var http = new HttpClient(new StubHandler(Valid));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HeavenGameHubCatalog.FindGuideAsync("monster-hunter-world", http, cts.Token));
    }
}
