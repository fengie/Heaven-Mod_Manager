using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GameBananaTransportTests
{
    [Fact]
    public async Task New_mods_uses_game_scoped_mod_only_core_endpoint()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/Core/List/New", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("itemtype=Mod", query, StringComparison.Ordinal);
            Assert.Contains("gameid=9081", query, StringComparison.Ordinal);
            Assert.Contains("page=2", query, StringComparison.Ordinal);
            Assert.Contains("include_updated=1", query, StringComparison.Ordinal);
            Assert.Contains("format=json_min", query, StringComparison.Ordinal);
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));

            var response = JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json"));
            response.Headers.ETag = new EntityTagHeaderValue("\"gb-new-v1\"");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);
        using var response = await transport.GetNewModsAsync(
            9081,
            2,
            includeUpdated: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"gb-new-v1\"", response.ETag);
        Assert.Equal(2, response.Document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Mod_detail_requests_only_allowlisted_fields_with_return_keys()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/Core/Item/Data", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("itemtype=Mod", query, StringComparison.Ordinal);
            Assert.Contains("itemid=653359", query, StringComparison.Ordinal);
            Assert.Contains("return_keys=1", query, StringComparison.Ordinal);
            Assert.Contains("Files%28%29.aFiles%28%29", query, StringComparison.Ordinal);
            Assert.Contains("Url%28%29.sProfileUrl%28%29", query, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")));
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);
        using var response = await transport.GetModDataAsync(
            "653359",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "Effecient Seliana Suite",
            response.Document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rate_limit_surfaces_retry_after_without_retrying()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(23));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);

        var exception = await Assert.ThrowsAsync<GameBananaTransportException>(
            () => transport.GetNewModsAsync(
                9081,
                1,
                includeUpdated: false,
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(23), exception.RetryAfter);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Malformed_json_fails_closed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{not-json")));

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetNewModsAsync(
                9081,
                1,
                includeUpdated: false,
                TestContext.Current.CancellationToken));

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
        var transport = new GameBananaTransport(client, maxResponseBytes: 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetNewModsAsync(
                9081,
                1,
                includeUpdated: false,
                TestContext.Current.CancellationToken));

        Assert.Contains("safety limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_token_reaches_transport_handler()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json"));
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetNewModsAsync(9081, 1, false, cancellation.Token));
    }

    [Fact]
    public void Non_https_base_uri_is_rejected()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json")))));

        Assert.Throws<ArgumentException>(
            () => new GameBananaTransport(client, new Uri("http://api.gamebanana.test/")));
    }

    [Fact]
    public void Compliance_record_is_current_api_only_and_assisted_download_only()
    {
        var compliance = GameBananaCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.False(compliance.AllowsHtmlParsing);
        Assert.False(compliance.AllowsDirectDownload);
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
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class GameBananaTransportTests
{
    [Fact]
    public async Task New_mods_uses_game_scoped_mod_only_core_endpoint()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/Core/List/New", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("itemtype=Mod", query, StringComparison.Ordinal);
            Assert.Contains("gameid=9081", query, StringComparison.Ordinal);
            Assert.Contains("page=2", query, StringComparison.Ordinal);
            Assert.Contains("include_updated=1", query, StringComparison.Ordinal);
            Assert.Contains("format=json_min", query, StringComparison.Ordinal);
            Assert.Contains("MHW-Manual-Mod-Manager", request.Headers.GetValues("User-Agent"));

            var response = JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json"));
            response.Headers.ETag = new EntityTagHeaderValue("\"gb-new-v1\"");
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);
        using var response = await transport.GetNewModsAsync(
            9081,
            2,
            includeUpdated: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"gb-new-v1\"", response.ETag);
        Assert.Equal(2, response.Document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Mod_detail_requests_only_allowlisted_fields_with_return_keys()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/Core/Item/Data", request.RequestUri?.AbsolutePath);
            var query = request.RequestUri?.Query ?? string.Empty;
            Assert.Contains("itemtype=Mod", query, StringComparison.Ordinal);
            Assert.Contains("itemid=653359", query, StringComparison.Ordinal);
            Assert.Contains("return_keys=1", query, StringComparison.Ordinal);
            Assert.Contains("Files%28%29.aFiles%28%29", query, StringComparison.Ordinal);
            Assert.Contains("Url%28%29.sProfileUrl%28%29", query, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("mod.json")));
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);
        using var response = await transport.GetModDataAsync(
            "653359",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "Effecient Seliana Suite",
            response.Document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rate_limit_surfaces_retry_after_without_retrying()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(23));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);

        var exception = await Assert.ThrowsAsync<GameBananaTransportException>(
            () => transport.GetNewModsAsync(
                9081,
                1,
                includeUpdated: false,
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(23), exception.RetryAfter);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Malformed_json_fails_closed()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{not-json")));

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetNewModsAsync(
                9081,
                1,
                includeUpdated: false,
                TestContext.Current.CancellationToken));

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
        var transport = new GameBananaTransport(client, maxResponseBytes: 1024);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetNewModsAsync(
                9081,
                1,
                includeUpdated: false,
                TestContext.Current.CancellationToken));

        Assert.Contains("safety limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_token_reaches_transport_handler()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json"));
        });

        using var client = new HttpClient(handler);
        var transport = new GameBananaTransport(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetNewModsAsync(9081, 1, false, cancellation.Token));
    }

    [Fact]
    public void Non_https_base_uri_is_rejected()
    {
        using var client = new HttpClient(new RecordingHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, ReadFixture("new-mods.json")))));

        Assert.Throws<ArgumentException>(
            () => new GameBananaTransport(client, new Uri("http://api.gamebanana.test/")));
    }

    [Fact]
    public void Compliance_record_is_current_api_only_and_assisted_download_only()
    {
        var compliance = GameBananaCatalogPolicy.Compliance;
        var errors = CatalogProviderComplianceValidator.Validate(
            compliance,
            new DateOnly(2026, 9, 29));

        Assert.Empty(errors);
        Assert.Equal(CatalogSourceKind.OfficialApi, compliance.SourceKind);
        Assert.True(compliance.AllowsCatalogDiscovery);
        Assert.False(compliance.AllowsHtmlParsing);
        Assert.False(compliance.AllowsDirectDownload);
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
