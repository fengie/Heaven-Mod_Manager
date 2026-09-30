using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ThunderstoreCatalogProviderTests
{
    private static readonly Uri BaseUri = new("https://thunderstore.io/");

    [Fact]
    public void Compliance_stays_fail_closed_until_current_terms_reference_is_confirmed()
    {
        var compliance = ThunderstoreCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.True(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
        Assert.False(compliance.Enabled);
        Assert.Contains("Terms of Service", compliance.DisabledReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_uses_community_scoped_v1_api_and_conditional_timestamp()
    {
        var observed = false;
        using var client = new HttpClient(new RoutingHandler((request, _) =>
        {
            observed = true;
            Assert.Equal("thunderstore.io", request.RequestUri?.Host);
            Assert.Equal(
                "/c/lethal-company/api/v1/package/",
                request.RequestUri?.AbsolutePath);
            Assert.Equal(
                new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero),
                request.Headers.IfModifiedSince);
            Assert.Contains(
                "MHW-Manual-Mod-Manager",
                request.Headers.GetValues("User-Agent"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"));
        }));

        var transport = new ThunderstoreTransport(client, BaseUri);
        using var response = await transport.GetPackagesAsync(
            "lethal-company",
            new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.True(observed);
        Assert.NotNull(response.Document);
        Assert.Equal(JsonValueKind.Array, response.Document.RootElement.ValueKind);
    }

    [Fact]
    public async Task Transport_decompresses_gzip_and_bounds_decoded_json()
    {
        var json = ReadFixture("packages.json");
        using var client = new HttpClient(new RoutingHandler((_, _) =>
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                gzip.Write(bytes, 0, bytes.Length);

            var content = new ByteArrayContent(compressed.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }));

        var transport = new ThunderstoreTransport(client, BaseUri, maxResponseBytes: 1024 * 1024);
        using var response = await transport.GetPackagesAsync(
            "lethal-company",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, response.Document?.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Rate_limit_surfaces_retry_after_without_retry_storm()
    {
        var calls = 0;
        using var client = new HttpClient(new RoutingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(37));
            return Task.FromResult(response);
        }));

        var transport = new ThunderstoreTransport(client, BaseUri);
        var exception = await Assert.ThrowsAsync<ThunderstoreTransportException>(
            () => transport.GetPackagesAsync(
                "lethal-company",
                ct: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(37), exception.RetryAfter);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Not_modified_response_does_not_require_or_parse_body()
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified))));

        var transport = new ThunderstoreTransport(client, BaseUri);
        using var response = await transport.GetPackagesAsync(
            "lethal-company",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            TestContext.Current.CancellationToken);

        Assert.True(response.NotModified);
        Assert.Null(response.Document);
    }

    [Fact]
    public void Normalizer_preserves_package_version_dependency_and_download_identity()
    {
        var game = CreateGame();
        using var document = JsonDocument.Parse(ReadFixture("packages.json"));

        var mod = Assert.Single(
            ThunderstoreCatalogNormalizer.NormalizePackages(
                game,
                "lethal-company",
                document));

        Assert.Equal(
            "thunderstore:lethal-company:11111111-1111-1111-1111-111111111111",
            mod.CanonicalId);
        Assert.Equal("1.2.3", mod.Version);
        Assert.Equal(1334, mod.Downloads);
        Assert.Equal(42, mod.Endorsements);
        Assert.Equal(2, mod.Files.Count);

        var latest = mod.Files[0];
        Assert.True(latest.Recommended);
        Assert.Equal(CatalogFileCategory.Main, latest.Category);
        Assert.Equal(
            "22222222-2222-2222-2222-222222222222",
            latest.ProviderFileId);

        var dependency = Assert.Single(latest.Dependencies ?? []);
        Assert.Equal("BepInEx-BepInExPack", dependency.Name);
        Assert.Equal(ThunderstoreCatalogPolicy.ProviderId, dependency.ProviderId);
        Assert.DoesNotContain(
            "download_url",
            latest.ProviderMetadata ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        using var detail = JsonDocument.Parse(document.RootElement[0].GetRawText());
        var acquisition = ThunderstoreCatalogNormalizer.NormalizeAcquisition(
            "lethal-company",
            mod.ProviderModId,
            latest.ProviderFileId,
            detail);

        Assert.Equal(
            "https://thunderstore.io/package/download/ExampleTeam/CoolMod/1.2.3/",
            acquisition.DownloadUri.AbsoluteUri);
    }

    [Fact]
    public void Normalizer_rejects_external_or_identity_mismatched_downloads()
    {
        var json = ReadFixture("packages.json")
            .Replace(
                "https://thunderstore.io/package/download/ExampleTeam/CoolMod/1.2.3/",
                "https://evil.example/CoolMod.zip",
                StringComparison.Ordinal);

        using var list = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(
            () => ThunderstoreCatalogNormalizer.NormalizePackages(
                CreateGame(),
                "lethal-company",
                list));
    }

    [Fact]
    public async Task Provider_refuses_network_access_while_compliance_is_disabled()
    {
        var calls = 0;
        using var client = new HttpClient(new RoutingHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("packages.json")));
        }));
        var provider = new ThunderstoreCatalogProvider(
            new ThunderstoreTransport(client, BaseUri),
            [new ThunderstoreCatalogGameSource(
                "fixture-game",
                "Fixture Game",
                "lethal-company")]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchModsAsync(
                new CatalogBrowseRequest(CreateGame()),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, calls);
        var health = await provider.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CatalogProviderState.Disabled, health.State);
    }

    [Theory]
    [InlineData("http://thunderstore.io/")]
    [InlineData("https://example.com/")]
    [InlineData("https://thunderstore.io/api/")]
    public void Transport_rejects_noncanonical_api_origin(string uri)
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"))));

        Assert.Throws<ArgumentException>(
            () => new ThunderstoreTransport(client, new Uri(uri)));
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
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Catalog",
                "Thunderstore",
                name),
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
