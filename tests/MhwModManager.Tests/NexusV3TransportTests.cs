using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusV3TransportTests
{
    [Fact]
    public async Task Public_trending_uses_v3_endpoint_without_credentials()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.nexusmods.com/v3/games/monsterhunterworld/trending-mods",
                request.RequestUri?.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("apikey"));
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));

            var response = JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json"));
            response.Headers.ETag = new EntityTagHeaderValue("\"trend-v1\"");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        using var result = await transport.GetTrendingModsAsync("monsterhunterworld", ct: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("\"trend-v1\"", result.ETag);
        Assert.NotNull(result.Document);
        Assert.Equal(
            2,
            result.Document!.RootElement
                .GetProperty("data")
                .GetProperty("mods")
                .GetArrayLength());
    }

    [Fact]
    public async Task Api_key_auth_uses_apikey_header_and_escaped_v3_mod_path()
    {
        const string secret = "fixture-api-key-value";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.nexusmods.com/v3/games/monster%20hunter/mods/10%2F20",
                request.RequestUri?.AbsoluteUri);
            Assert.Equal(secret, Assert.Single(request.Headers.GetValues("apikey")));
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")));
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        using var result = await transport.GetModAsync(
            "monster hunter",
            "10/20",
            NexusV3Credential.ApiKey(secret),
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Document);
        Assert.Equal(
            "Fixture One",
            result.Document!.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Bearer_auth_uses_authorization_header_for_mod_files()
    {
        const string token = "fixture-bearer-token";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.nexusmods.com/v3/mods/fixture-mod-global-id/files",
                request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(token, request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("apikey"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod-files.json")));
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        using var result = await transport.GetModFilesAsync(
            "fixture-mod-global-id",
            NexusV3Credential.Bearer(token),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(
            "fixture-file-id",
            result.Document!.RootElement
                .GetProperty("data")
                .GetProperty("mod_files")[0]
                .GetProperty("id")
                .GetString());
    }

    [Fact]
    public async Task Rate_limit_surfaces_retry_after_without_automatic_retry_or_secret_leak()
    {
        const string secret = "do-not-leak-this-token";
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);

        var exception = await Assert.ThrowsAsync<NexusV3TransportException>(
            () => transport.GetModAsync(
                "monsterhunterworld",
                "101",
                NexusV3Credential.Bearer(secret),
                ct: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(17), exception.RetryAfter);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Missing_data_envelope_fails_closed_as_schema_drift()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"unexpected\":true}")));

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetTrendingModsAsync("monsterhunterworld", ct: TestContext.Current.CancellationToken));

        Assert.Contains("data envelope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_declared_response_is_rejected_before_parsing()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new ByteArrayContent(new byte[2048]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client, maxResponseBytes: 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetTrendingModsAsync("monsterhunterworld"));

        Assert.Contains("safety limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conditional_cache_validators_are_emitted_and_304_has_no_document()
    {
        var lastModified = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("\"fixture-etag\"", Assert.Single(request.Headers.IfNoneMatch).Tag);
            Assert.Equal(lastModified, request.Headers.IfModifiedSince);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        using var result = await transport.GetTrendingModsAsync(
            "monsterhunterworld",
            new NexusV3ConditionalRequest("\"fixture-etag\"", lastModified),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsNotModified);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task Cancellation_token_reaches_transport_handler()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json"));
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetTrendingModsAsync("monsterhunterworld", ct: cts.Token));
    }

    [Fact]
    public async Task Mod_file_versions_endpoint_is_bounded_to_v3_path()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.nexusmods.com/v3/mod-files/file%2Fchain/versions",
                request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"data\":{\"versions\":[]}}"));
        });

        using var client = new HttpClient(handler);
        var transport = new NexusV3Transport(client);
        using var result = await transport.GetModFileVersionsAsync(
            "file/chain",
            NexusV3Credential.ApiKey("fixture-key"),
            ct: TestContext.Current.CancellationToken);

        Assert.Empty(result.Document!.RootElement.GetProperty("data").GetProperty("versions").EnumerateArray());
    }

    [Fact]
    public void Nexus_compliance_record_is_current_and_disallows_html_and_direct_download_by_default()
    {
        var compliance = NexusV3CatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.False(compliance.AllowsHtmlParsing);
        Assert.False(compliance.AllowsDirectDownload);
    }

    [Fact]
    public void Non_https_base_uri_is_rejected()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("trending.json")))));

        Assert.Throws<ArgumentException>(
            () => new NexusV3Transport(client, new Uri("http://api.example.test/v3/")));
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
