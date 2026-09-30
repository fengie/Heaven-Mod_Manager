using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GitHubReleasesCatalogProviderTests
{
    [Fact]
    public async Task Curated_latest_release_uses_exact_official_endpoint_and_normalizes_catalog_item()
    {
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            Assert.Equal(
                "https://api.github.com/repos/example/mhw-mod/releases/latest",
                request.RequestUri?.AbsoluteUri);
            Assert.Contains(request.Headers.Accept, value => string.Equals(value.MediaType, "application/vnd.github+json", StringComparison.Ordinal));
            Assert.Equal("2026-03-10", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("latest.json")));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mods = await provider.SearchModsAsync(new CatalogBrowseRequest(game, "elder dragon"), TestContext.Current.CancellationToken);
        var mod = Assert.Single(mods);

        Assert.Equal("github-releases", mod.ProviderId);
        Assert.Equal("example/mhw-mod", mod.ProviderModId);
        Assert.Equal("github-releases:example/mhw-mod", mod.CanonicalId);
        Assert.Equal("Fixture MHW Mod", mod.Name);
        Assert.Equal("v1.2.3", mod.Version);
        Assert.NotNull(mod.Downloads);
        Assert.Equal(51L, mod.Downloads.Value);
        Assert.Equal("https://github.com/example/mhw-mod/releases/tag/v1.2.3", mod.SourceUrl);
        Assert.Equal(2, mod.Files.Count);
        Assert.Equal("501", mod.Files[0].ProviderFileId);
        Assert.Null(mod.ProviderMetadata);
        Assert.All(mod.Files, file => Assert.Null(file.ProviderMetadata));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Uncurated_repository_is_never_queried()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Transport should not be called for an uncurated repository."));

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = await provider.GetModAsync(game, "someone/unknown-mod", TestContext.Current.CancellationToken);

        Assert.Null(mod);
    }

    [Fact]
    public async Task Direct_acquisition_is_resolved_from_current_api_asset_not_catalog_metadata()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("latest.json")));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = Assert.NotNull(await provider.GetModAsync(game, "example/mhw-mod", TestContext.Current.CancellationToken));
        var file = Assert.Single(mod.Files, candidate => candidate.ProviderFileId == "501");

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file), TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Direct, resolution.Kind);
        Assert.Equal(
            "https://github.com/example/mhw-mod/releases/download/v1.2.3/mhw-mod-v1.2.3.zip",
            resolution.DownloadUri?.AbsoluteUri);
        Assert.Null(resolution.ExpiresAt);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Missing_release_is_a_clean_empty_curated_result()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mods = await provider.SearchModsAsync(new CatalogBrowseRequest(game), TestContext.Current.CancellationToken);

        Assert.Empty(mods);
    }

    [Fact]
    public async Task Malformed_release_schema_fails_closed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"id\":1,\"tag_name\":\"v1\"}")));

        using var client = new HttpClient(handler);
        var transport = new GitHubReleasesTransport(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetLatestReleaseAsync("example", "mhw-mod", TestContext.Current.CancellationToken));

        Assert.Contains("html_url", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bearer_auth_and_rate_limit_failure_do_not_leak_secret_or_retry_automatically()
    {
        const string secret = "github-secret-do-not-leak";
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(secret, request.Headers.Authorization?.Parameter);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "5000");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1790726400");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GitHubReleasesTransport(client, new GitHubCatalogCredential(secret));

        var exception = await Assert.ThrowsAsync<GitHubReleaseTransportException>(
            () => transport.GetLatestReleaseAsync("example", "mhw-mod", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(0, exception.RateLimit?.HourlyRemaining);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Health_uses_core_rate_limit_headers_and_body()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("https://api.github.com/rate_limit", request.RequestUri?.AbsoluteUri);
            var response = JsonResponse(
                HttpStatusCode.OK,
                "{\"resources\":{\"core\":{\"limit\":60,\"remaining\":17,\"reset\":1790726400}}}");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "60");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "17");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1790726400");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogProviderState.Connected, health.State);
        Assert.Equal(60, health.RateLimit?.HourlyLimit);
        Assert.Equal(17, health.RateLimit?.HourlyRemaining);
    }

    [Fact]
    public void Compliance_record_is_current_api_only_and_direct_asset_capable()
    {
        var compliance = GitHubReleasesCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.True(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
    }

    [Fact]
    public void Provider_requires_explicit_curated_sources_and_rejects_duplicate_repo_identity()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("latest.json")))));
        var transport = new GitHubReleasesTransport(client);

        Assert.Throws<ArgumentException>(
            () => new GitHubReleasesCatalogProvider(transport, Array.Empty<GitHubReleaseCatalogSource>()));

        var source = CreateSource();
        Assert.Throws<ArgumentException>(
            () => new GitHubReleasesCatalogProvider(transport, new[] { source, source }));
    }

    [Fact]
    public void Credential_cannot_be_bound_to_non_github_api_origin()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Credential-origin validation must happen before any request.")));

        Assert.Throws<ArgumentException>(
            () => new GitHubReleasesTransport(
                client,
                new GitHubCatalogCredential("fixture-secret"),
                new Uri("https://example.invalid/")));
    }

    [Fact]
    public async Task Acquisition_rejects_mod_from_different_selected_game()
    {
        var calls = 0;
        using var client = new HttpClient(new RecordingHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("latest.json")));
        }));
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = Assert.NotNull(await provider.GetModAsync(
            game,
            "example/mhw-mod",
            TestContext.Current.CancellationToken));
        var file = Assert.Single(mod.Files, candidate => candidate.ProviderFileId == "501");

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(
                game,
                mod with { GameId = "different-game" },
                file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Unavailable, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Non_https_transport_base_is_rejected()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("latest.json")))));

        Assert.Throws<ArgumentException>(
            () => new GitHubReleasesTransport(client, baseUri: new Uri("http://api.github.test/")));
    }

    private static GitHubReleasesCatalogProvider CreateProvider(HttpClient client)
    {
        return new GitHubReleasesCatalogProvider(
            new GitHubReleasesTransport(client),
            new[] { CreateSource() });
    }

    private static GitHubReleaseCatalogSource CreateSource()
    {
        return new GitHubReleaseCatalogSource(
            "monster-hunter-world",
            "Monster Hunter: World",
            "example",
            "mhw-mod",
            "Fixture MHW Mod",
            new[] { "mhw", "rebalance" });
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "GitHubReleases", name),
            Encoding.UTF8);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
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
