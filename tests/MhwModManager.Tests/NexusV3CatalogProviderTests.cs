using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3CatalogProviderTests
{
    [Fact]
    public void Capabilities_do_not_claim_full_search_before_transport_support_exists()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json")))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));

        Assert.True(provider.Capabilities.HasFlag(CatalogProviderCapabilities.Browse));
        Assert.False(provider.Capabilities.HasFlag(CatalogProviderCapabilities.Search));
    }

    [Fact]
    public async Task Query_and_non_trending_modes_fail_closed_instead_of_returning_partial_results()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Unsupported discovery must not reach transport.")));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game, Query: "weapon"),
                TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(
                    game,
                    Mode: CatalogBrowseMode.RecentlyUpdated),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Trending_rejects_lookalike_nexus_hostname()
    {
        const string payload =
            """
            {
              "data": {
                "mods": [
                  {
                    "name": "Lookalike",
                    "author": "Fixture",
                    "summary": "Must not be trusted.",
                    "mod_page_url": "https://evilnexusmods.com/monsterhunterworld/mods/999"
                  }
                ]
              }
            }
            """;

        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, payload))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game),
                TestContext.Current.CancellationToken));

        Assert.Contains("trusted Nexus source URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trending_entries_are_normalized_with_stable_game_scoped_identity()
    {
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
            new CatalogBrowseRequest(game, Limit: 10),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        var first = result[0];
        Assert.Equal("nexus:101", first.CanonicalId);
        Assert.Equal("101", first.ProviderModId);
        Assert.Equal("Fixture One", first.Name);
        Assert.Equal("AuthorOne", first.Author);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101",
            first.SourceUrl);
        Assert.Equal(
            "https://staticdelivery.nexusmods.com/fixture-one.jpg",
            first.Thumbnail);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    [Fact]
    public async Task File_lookup_hydrates_global_mod_id_then_expands_exact_versions()
    {
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

        var files = await provider.GetModFilesAsync(game, "101", TestContext.Current.CancellationToken);

        Assert.Collection(
            requests,
            item => Assert.Equal("/v3/games/monsterhunterworld/mods/101", item),
            item => Assert.Equal("/v3/mods/fixture-mod-global-id/files", item),
            item => Assert.Equal("/v3/mod-files/fixture-file-id/versions", item));

        Assert.Equal(2, files.Count);

        var main = files[0];
        Assert.Equal("fixture-version-id", main.ProviderFileId);
        Assert.Equal(CatalogFileCategory.Main, main.Category);
        Assert.Equal("1.2.0", main.Version);
        Assert.True(main.Recommended);
        Assert.Contains("fixture-file-id", main.ProviderMetadata!, StringComparison.Ordinal);

        var optional = files[1];
        Assert.Equal("fixture-optional-version-id", optional.ProviderFileId);
        Assert.Equal(CatalogFileCategory.Optional, optional.Category);
        Assert.False(optional.Recommended);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Connected, health.State);
    }

    [Fact]
    public async Task Mod_details_preserve_global_identity_only_as_provider_metadata()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            Assert.Equal(
                "/v3/games/monsterhunterworld/mods/101",
                request.RequestUri?.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")));
        });

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(
            new NexusV3Transport(client),
            NexusV3Credential.Bearer("fixture-token"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = await provider.GetModAsync(game, "101", TestContext.Current.CancellationToken);

        Assert.NotNull(mod);
        Assert.Equal("101", mod!.ProviderModId);
        Assert.Equal("nexus:101", mod.CanonicalId);
        Assert.Contains("fixture-mod-global-id", mod.ProviderMetadata!, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-token", mod.ProviderMetadata!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_inner_schema_fails_closed_and_marks_provider_limited()
    {
        var handler = new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"data":{"mods":[{"name":"Missing identity"}]}}""")));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game),
                TestContext.Current.CancellationToken));

        Assert.Contains("mod_page_url", exception.Message, StringComparison.Ordinal);
        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Limited, health.State);
        Assert.Contains("schema drift", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_marks_provider_offline()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("fixture timeout"))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Offline, health.State);
        Assert.Contains("timed out", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_failure_marks_provider_offline()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("fixture offline"))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game),
                TestContext.Current.CancellationToken));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Offline, health.State);
        Assert.Contains("unreachable", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_reclassify_provider_health()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var client = new HttpClient(new RoutingHandler((_, ct) =>
            Task.FromCanceled<HttpResponseMessage>(ct)));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(game),
                cancellation.Token));

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Limited, health.State);
    }

    [Fact]
    public async Task Rate_limit_marks_health_and_keeps_retry_after_without_retrying()
    {
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

        var exception = await Assert.ThrowsAsync<NexusV3TransportException>(
            () => provider.GetModAsync(game, "101", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(1, calls);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.RateLimited, health.State);
        Assert.NotNull(health.RateLimit?.RetryAfter);
    }

    [Fact]
    public async Task Auth_failure_marks_provider_authentication_required()
    {
        var handler = new RoutingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        using var client = new HttpClient(handler);
        var provider = new NexusV3CatalogProvider(
            new NexusV3Transport(client),
            NexusV3Credential.ApiKey("secret-that-must-not-leak"));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var exception = await Assert.ThrowsAsync<NexusV3TransportException>(
            () => provider.GetModAsync(game, "101", TestContext.Current.CancellationToken));

        Assert.DoesNotContain("secret-that-must-not-leak", exception.Message, StringComparison.Ordinal);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.AuthenticationRequired, health.State);
    }

    [Fact]
    public async Task Acquisition_is_assisted_until_provider_authorized_direct_flow_is_implemented()
    {
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
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Assisted, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Equal(
            "https://www.nexusmods.com/monsterhunterworld/mods/101?tab=files",
            resolution.AssistedUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Acquisition_rejects_cross_provider_identity_mismatch()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json")))));
        var provider = new NexusV3CatalogProvider(new NexusV3Transport(client));
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = new CatalogMod(
            "github:101",
            "github",
            "101",
            game.Id,
            "Wrong provider",
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
            "https://example.invalid/mod",
            []);

        var file = new CatalogModFile(
            "github",
            "101",
            "file-1",
            "Wrong provider file",
            "Wrong provider file",
            CatalogFileCategory.Main);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ResolveAcquisitionAsync(
                new CatalogAcquisitionRequest(game, mod, file),
                TestContext.Current.CancellationToken));
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
