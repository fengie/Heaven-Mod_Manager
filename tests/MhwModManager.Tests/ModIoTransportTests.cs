using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ModIoTransportTests
{
    [Fact]
    public async Task Browse_uses_game_scoped_https_host_api_key_and_sort()
    {
        const string apiKey = "fixture-api-key";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("g-42.modapi.io", request.RequestUri?.Host);
            Assert.Equal("/v1/games/42/mods", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("api_key=fixture-api-key", query, StringComparison.Ordinal);
            Assert.Contains("_sort=-date_updated", query, StringComparison.Ordinal);
            Assert.Contains("_limit=25", query, StringComparison.Ordinal);
            Assert.Contains("_offset=50", query, StringComparison.Ordinal);
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{"data":[],"result_count":0}"));
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly(apiKey));
        using var response = await transport.GetModsAsync(
            CatalogBrowseMode.RecentlyUpdated,
            25,
            50,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Document.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Authenticated_flow_keeps_api_key_and_adds_bearer_header()
    {
        const string apiKey = "fixture-api-key";
        const string token = "fixture-access-token";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Contains("api_key=fixture-api-key", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(token, request.Headers.Authorization?.Parameter);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{"id":7,"game_id":42,"name":"Fixture"}"));
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.Authenticated(apiKey, token));
        using var response = await transport.GetModAsync("7", TestContext.Current.CancellationToken);

        Assert.Equal(7, response.Document.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Modfiles_use_published_game_mod_files_endpoint()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/v1/games/42/mods/7/files", request.RequestUri?.AbsolutePath);
            Assert.Contains("_limit=100", request.RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{"data":[]}"));
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly("fixture-key"));
        using var response = await transport.GetModFilesAsync("7", ct: TestContext.Current.CancellationToken);

        Assert.Empty(response.Document.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Rate_limit_surfaces_retry_after_without_retry_or_secret_leak()
    {
        const string secret = "do-not-leak-api-key";
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(19));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly(secret));

        var exception = await Assert.ThrowsAsync<ModIoTransportException>(
            () => transport.GetModsAsync(
                CatalogBrowseMode.Latest,
                ct: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(19), exception.RetryAfter);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Network_failure_is_sanitized_even_if_handler_mentions_request_uri()
    {
        const string secret = "secret-query-key";
        var handler = new RecordingHandler((request, _) =>
            throw new HttpRequestException("failed request " + request.RequestUri));

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly(secret));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetModAsync("7", TestContext.Current.CancellationToken));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Missing_data_array_fails_closed_for_list_endpoint()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{"unexpected":true}")));

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly("fixture-key"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetModsAsync(
                CatalogBrowseMode.Popular,
                ct: TestContext.Current.CancellationToken));

        Assert.Contains("data array", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_json_fails_closed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{not-json")));

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly("fixture-key"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetModAsync("7", TestContext.Current.CancellationToken));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_declared_response_is_rejected()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new ByteArrayContent(new byte[2048]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly("fixture-key"), maxResponseBytes: 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetModAsync("7", TestContext.Current.CancellationToken));

        Assert.Contains("safety limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compliance_record_is_official_api_and_direct_download_is_fail_closed_until_consent_flow()
    {
        var compliance = ModIoCatalogPolicy.Compliance;
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
    public void Invalid_game_and_provider_ids_are_rejected_before_request()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{"data":[]}"))));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModIoTransport(client, 0, ModIoCredential.ReadOnly("fixture-key")));

        var transport = new ModIoTransport(client, 42, ModIoCredential.ReadOnly("fixture-key"));
        Assert.Throws<ArgumentException>(() => transport.GetModAsync("../7"));
        Assert.Throws<ArgumentException>(() => transport.GetModFileAsync("7", "0"));
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
