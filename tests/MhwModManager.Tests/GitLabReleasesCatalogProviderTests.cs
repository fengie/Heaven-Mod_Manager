using System.Net;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GitLabReleasesCatalogProviderTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v2.0.0",
          "description": "Fixture release for Monster Hunter World.",
          "name": "Fixture GitLab Mod v2",
          "created_at": "2026-09-29T12:00:00Z",
          "released_at": "2026-09-29T13:00:00Z",
          "author": {
            "id": 7,
            "name": "Fixture Author",
            "username": "fixture-author"
          },
          "assets": {
            "count": 3,
            "sources": [],
            "links": [
              {
                "id": 11,
                "name": "fixture-mod-v2.zip",
                "url": "https://gitlab.com/example/mhw-mod/-/jobs/123/artifacts/raw/fixture-mod-v2.zip",
                "direct_asset_url": "https://gitlab.com/example/mhw-mod/-/releases/v2.0.0/downloads/fixture-mod-v2.zip",
                "link_type": "package"
              },
              {
                "id": 12,
                "name": "manual-download.txt",
                "url": "https://downloads.example.invalid/manual-download.txt",
                "link_type": "other"
              },
              {
                "id": 13,
                "name": "preview.png",
                "url": "https://gitlab.com/example/mhw-mod/-/raw/main/preview.png",
                "direct_asset_url": "https://gitlab.com/example/mhw-mod/-/releases/v2.0.0/downloads/preview.png",
                "link_type": "image"
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task Curated_latest_release_uses_exact_official_endpoint_and_normalizes_catalog_item()
    {
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            Assert.Equal(
                "https://gitlab.com/api/v4/projects/example%2Fmhw-mod/releases/permalink/latest",
                request.RequestUri?.AbsoluteUri);
            Assert.Contains(
                request.Headers.Accept,
                value => string.Equals(value.MediaType, "application/json", StringComparison.Ordinal));
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReleaseJson));
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mods = await provider.SearchModsAsync(
            new CatalogBrowseRequest(game, "fixture author"),
            TestContext.Current.CancellationToken);
        var mod = Assert.Single(mods);

        Assert.Equal("gitlab-releases", mod.ProviderId);
        Assert.Equal("example/mhw-mod", mod.ProviderModId);
        Assert.Equal("gitlab-releases:example/mhw-mod", mod.CanonicalId);
        Assert.Equal("Fixture GitLab Mod", mod.Name);
        Assert.Equal("v2.0.0", mod.Version);
        Assert.Equal("fixture-author", mod.Author);
        Assert.Equal(
            "https://gitlab.com/example/mhw-mod/-/releases/v2.0.0",
            mod.SourceUrl);
        Assert.Equal(2, mod.Files.Count);
        Assert.Equal("11", mod.Files[0].ProviderFileId);
        Assert.Contains("\"direct\":true", mod.Files[0].ProviderMetadata!, StringComparison.Ordinal);
        Assert.DoesNotContain("gitlab.com", mod.Files[0].ProviderMetadata!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Uncurated_project_is_never_queried()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Transport should not be called for an uncurated project."));

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());

        var mod = await provider.GetModAsync(
            game,
            "someone/unknown-mod",
            TestContext.Current.CancellationToken);

        Assert.Null(mod);
    }

    [Fact]
    public async Task Direct_acquisition_uses_current_same_host_direct_asset_url()
    {
        var calls = 0;
        using var client = new HttpClient(new RecordingHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReleaseJson));
        }));
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = await provider.GetModAsync(
            game,
            "example/mhw-mod",
            TestContext.Current.CancellationToken);
        Assert.NotNull(mod);
        var file = Assert.Single(mod!.Files, candidate => candidate.ProviderFileId == "11");

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Direct, resolution.Kind);
        Assert.Equal(
            "https://gitlab.com/example/mhw-mod/-/releases/v2.0.0/downloads/fixture-mod-v2.zip",
            resolution.DownloadUri?.AbsoluteUri);
        Assert.Equal(
            "https://gitlab.com/example/mhw-mod/-/releases/v2.0.0",
            resolution.AssistedUri?.AbsoluteUri);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Asset_without_same_host_direct_url_falls_back_to_release_page()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReleaseJson))));
        var provider = CreateProvider(client);
        var game = GameProfile.MonsterHunterWorld(Path.GetTempPath());
        var mod = await provider.GetModAsync(
            game,
            "example/mhw-mod",
            TestContext.Current.CancellationToken);
        Assert.NotNull(mod);
        var file = Assert.Single(mod!.Files, candidate => candidate.ProviderFileId == "12");

        var resolution = await provider.ResolveAcquisitionAsync(
            new CatalogAcquisitionRequest(game, mod, file),
            TestContext.Current.CancellationToken);

        Assert.Equal(CatalogAcquisitionKind.Assisted, resolution.Kind);
        Assert.Null(resolution.DownloadUri);
        Assert.Equal(
            "https://gitlab.com/example/mhw-mod/-/releases/v2.0.0",
            resolution.AssistedUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Private_token_and_rate_limit_failure_do_not_leak_secret_or_retry()
    {
        const string secret = "gitlab-secret-do-not-leak";
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            Assert.Equal(secret, Assert.Single(request.Headers.GetValues("PRIVATE-TOKEN")));
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("RateLimit-Limit", "600");
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation("Retry-After", "60");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GitLabReleasesTransport(
            client,
            new GitLabCatalogCredential(secret));

        var exception = await Assert.ThrowsAsync<GitLabReleaseTransportException>(
            () => transport.GetLatestReleaseAsync(
                "example/mhw-mod",
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(600, exception.RateLimit?.HourlyLimit);
        Assert.Equal(0, exception.RateLimit?.HourlyRemaining);
        Assert.NotNull(exception.RateLimit?.RetryAfter);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Health_treats_reachable_project_without_release_as_connected()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound);
            response.Headers.TryAddWithoutValidation("RateLimit-Limit", "600");
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "599");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CatalogProviderState.Connected, health.State);
        Assert.Equal(600, health.RateLimit?.HourlyLimit);
        Assert.Equal(599, health.RateLimit?.HourlyRemaining);
    }

    [Fact]
    public async Task Malformed_release_schema_fails_closed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                "{\"tag_name\":\"v1\",\"author\":{\"name\":\"A\"}}")));

        using var client = new HttpClient(handler);
        var transport = new GitLabReleasesTransport(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetLatestReleaseAsync(
                "example/mhw-mod",
                TestContext.Current.CancellationToken));

        Assert.Contains("assets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cross_host_direct_asset_url_fails_closed()
    {
        const string json = """
            {
              "tag_name":"v1",
              "author":{"name":"A"},
              "assets":{"links":[
                {
                  "id":1,
                  "name":"mod.zip",
                  "link_type":"package",
                  "direct_asset_url":"https://evil.example/mod.zip"
                }
              ]}
            }
            """;
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, json))));
        var transport = new GitLabReleasesTransport(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetLatestReleaseAsync(
                "example/mhw-mod",
                TestContext.Current.CancellationToken));

        Assert.Contains("configured GitLab host", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_retry()
    {
        var calls = 0;
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, ReleaseJson);
        });

        using var client = new HttpClient(handler);
        var transport = new GitLabReleasesTransport(client);
        using var cts = new CancellationTokenSource();

        var task = transport.GetLatestReleaseAsync("example/mhw-mod", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Declared_oversized_response_fails_closed_before_body_read()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = JsonResponse(HttpStatusCode.OK, ReleaseJson);
            response.Content!.Headers.ContentLength = 2048;
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GitLabReleasesTransport(client, maxResponseBytes: 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetLatestReleaseAsync(
                "example/mhw-mod",
                TestContext.Current.CancellationToken));

        Assert.Contains("exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compliance_record_is_current_api_only_and_direct_asset_capable()
    {
        var compliance = GitLabReleasesCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 30));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.True(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
    }

    [Fact]
    public void Provider_requires_curated_sources_and_rejects_duplicate_project_identity()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReleaseJson))));
        var transport = new GitLabReleasesTransport(client);

        Assert.Throws<ArgumentException>(
            () => new GitLabReleasesCatalogProvider(
                transport,
                Array.Empty<GitLabReleaseCatalogSource>()));

        var source = CreateSource();
        Assert.Throws<ArgumentException>(
            () => new GitLabReleasesCatalogProvider(
                transport,
                new[] { source, source }));
    }

    [Fact]
    public void Credential_cannot_be_bound_to_non_gitlab_api_origin()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Credential-origin validation must happen before any request.")));

        Assert.Throws<ArgumentException>(
            () => new GitLabReleasesTransport(
                client,
                new GitLabCatalogCredential("fixture-secret"),
                new Uri("https://gitlab.example/api/v4/")));
    }

    [Fact]
    public void Non_https_transport_base_is_rejected()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReleaseJson))));

        Assert.Throws<ArgumentException>(
            () => new GitLabReleasesTransport(
                client,
                baseUri: new Uri("http://gitlab.example/api/v4/")));
    }

    private static GitLabReleasesCatalogProvider CreateProvider(HttpClient client)
    {
        return new GitLabReleasesCatalogProvider(
            new GitLabReleasesTransport(client),
            new[] { CreateSource() });
    }

    private static GitLabReleaseCatalogSource CreateSource()
    {
        return new GitLabReleaseCatalogSource(
            "monster-hunter-world",
            "Monster Hunter: World",
            "example/mhw-mod",
            "Fixture GitLab Mod",
            new[] { "mhw", "gitlab" });
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
