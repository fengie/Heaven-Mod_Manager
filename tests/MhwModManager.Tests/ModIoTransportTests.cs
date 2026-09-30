using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class ModIoTransportTests
{
    private static readonly Uri BaseUri = new("https://g-42.modapi.io/v1/");

    [Fact]
    public async Task Mods_request_uses_official_game_api_path_api_key_and_pagination()
    {
        const string key = "fixture-modio-api-key";
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/v1/games/42/mods", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("api_key=fixture-modio-api-key", query, StringComparison.Ordinal);
            Assert.Contains("_limit=25", query, StringComparison.Ordinal);
            Assert.Contains("_offset=50", query, StringComparison.Ordinal);
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mods.json")));
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, BaseUri);
        using var result = await transport.GetModsAsync(
            42,
            new ModIoApiKey(key),
            limit: 25,
            offset: 50,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Document.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Modfiles_request_preserves_dynamic_download_url_only_in_response_payload()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/v1/games/42/mods/101/files", request.RequestUri?.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("modfiles.json")));
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, BaseUri);
        using var result = await transport.GetModFilesAsync(
            42,
            101,
            new ModIoApiKey("fixture-key"),
            ct: TestContext.Current.CancellationToken);

        var download = result.Document.RootElement
            .GetProperty("data")[0]
            .GetProperty("download");
        Assert.Contains("/download/", download.GetProperty("binary_url").GetString(), StringComparison.Ordinal);
        Assert.True(download.GetProperty("date_expires").GetInt64() > 0);
    }

    [Fact]
    public async Task Rate_limit_surfaces_retry_after_without_retry_or_secret_leak()
    {
        const string secret = "do-not-leak-modio-secret";
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(31));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, BaseUri);
        var exception = await Assert.ThrowsAsync<ModIoTransportException>(
            () => transport.GetModsAsync(
                42,
                new ModIoApiKey(secret),
                ct: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(31), exception.RetryAfter);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Missing_data_array_fails_closed_as_schema_drift()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"unexpected\":true}")));

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, BaseUri);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetModsAsync(
                42,
                new ModIoApiKey("fixture-key"),
                ct: TestContext.Current.CancellationToken));

        Assert.Contains("data array", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_response_is_rejected_before_json_parse()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new ByteArrayContent(new byte[2048]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, BaseUri, maxResponseBytes: 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetModsAsync(
                42,
                new ModIoApiKey("fixture-key"),
                ct: TestContext.Current.CancellationToken));

        Assert.Contains("safety limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_reaches_http_handler()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, ReadFixture("mods.json"));
        });

        using var client = new HttpClient(handler);
        var transport = new ModIoTransport(client, BaseUri);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetModsAsync(42, new ModIoApiKey("fixture-key"), ct: cancellation.Token));
    }

    [Fact]
    public void Compliance_is_current_api_only_and_allows_authorized_direct_download()
    {
        var compliance = ModIoCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(compliance, new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.True(compliance.AllowsDirectDownload);
        Assert.False(compliance.AllowsHtmlParsing);
    }

    [Theory]
    [InlineData("http://g-42.modapi.io/v1/")]
    [InlineData("https://example.com/v1/")]
    [InlineData("https://g-42.modapi.io/v2/")]
    public void Invalid_base_uri_is_rejected(string uri)
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mods.json")))));

        Assert.Throws<ArgumentException>(() => new ModIoTransport(client, new Uri(uri)));
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", "ModIo", name),
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
