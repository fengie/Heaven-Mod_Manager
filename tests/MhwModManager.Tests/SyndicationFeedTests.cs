using System.Net;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class SyndicationFeedTests
{
    [Fact]
    public void Rss_fixture_parses_moddb_fields_and_sanitizes_markup()
    {
        var feed = SyndicationFeedParser.Parse(
            ReadFixture("ModDb", "downloads-rss.xml"),
            new Uri("https://rss.moddb.com/downloads/feed/rss.xml"));

        Assert.Equal("Mod DB Fixture Downloads", feed.Title);
        var entry = Assert.Single(feed.Entries);
        Assert.Equal("moddb-fixture-download-12345", entry.Id);
        Assert.Equal("Fixture MHW Download", entry.Title);
        Assert.Equal("Fixture release summary.", entry.Summary);
        Assert.Equal("Fixture Author", entry.Author);
        Assert.Equal(
            "https://www.moddb.com/mods/fixture-mhw/downloads/fixture-release",
            entry.Link.AbsoluteUri);
        Assert.Contains("Full Version", entry.Categories);
        Assert.Contains("Monster Hunter: World", entry.Categories);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 2, 30, 0, TimeSpan.Zero), entry.PublishedAt);
    }

    [Fact]
    public void Atom_fixture_is_supported_without_provider_specific_payload_types()
    {
        var feed = SyndicationFeedParser.Parse(
            ReadFixture("Syndication", "atom.xml"),
            new Uri("https://feeds.example.test/mhw.xml"));

        var entry = Assert.Single(feed.Entries);
        Assert.Equal("Atom Fixture Mod", entry.Title);
        Assert.Equal("Atom summary.", entry.Summary);
        Assert.Equal("Atom Author", entry.Author);
        Assert.Equal("https://example.test/mods/atom-fixture", entry.Link.AbsoluteUri);
        Assert.Contains("update", entry.Categories);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero), entry.UpdatedAt);
    }

    [Fact]
    public void Xml_dtd_and_external_entity_payloads_fail_closed()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///windows/win.ini">]>
            <rss version="2.0"><channel><title>&xxe;</title></channel></rss>
            """;

        var ex = Assert.Throws<InvalidDataException>(() =>
            SyndicationFeedParser.Parse(xml, new Uri("https://rss.moddb.com/downloads/feed/rss.xml")));

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Conditional_transport_sends_validators_and_accepts_304_without_parsing_body()
    {
        var calls = 0;
        var lastModified = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            Assert.Equal("\"fixture-v1\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            Assert.Equal(lastModified, request.Headers.IfModifiedSince);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client);

        var result = await transport.GetConditionalAsync(
            new Uri("https://feeds.example.test/catalog/rss.xml"),
            "\"fixture-v1\"",
            lastModified,
            TestContext.Current.CancellationToken);

        Assert.True(result.NotModified);
        Assert.Null(result.Feed);
        Assert.Equal("\"fixture-v1\"", result.ETag);
        Assert.Equal(lastModified, result.LastModified);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Conditional_transport_rejects_malformed_etag_before_network()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            throw new Xunit.Sdk.XunitException("Malformed validators must fail before network I/O.");
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            transport.GetConditionalAsync(
                new Uri("https://feeds.example.test/catalog/rss.xml"),
                "not-an-etag",
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Conditional_transport_returns_fresh_feed_and_response_validators()
    {
        var lastModified = new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Empty(request.Headers.IfNoneMatch);
            Assert.Null(request.Headers.IfModifiedSince);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    ReadFixture("ModDb", "downloads-rss.xml"),
                    Encoding.UTF8,
                    "application/rss+xml")
            };
            response.Headers.ETag =
                new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture-v2\"");
            response.Content.Headers.LastModified = lastModified;
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client);

        var result = await transport.GetConditionalAsync(
            new Uri("https://feeds.example.test/catalog/rss.xml"),
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.NotModified);
        Assert.NotNull(result.Feed);
        Assert.Single(result.Feed!.Entries);
        Assert.Equal("\"fixture-v2\"", result.ETag);
        Assert.Equal(lastModified, result.LastModified);
    }

    [Fact]
    public async Task Transport_surfaces_429_without_retry_storm()
    {
        var calls = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(response);
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client);

        var ex = await Assert.ThrowsAsync<SyndicationFeedTransportException>(() =>
            transport.GetAsync(
                new Uri("https://rss.moddb.com/downloads/feed/rss.xml"),
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.NotNull(ex.RetryAfter);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transport_rejects_oversized_payload_before_xml_parse()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new ByteArrayContent(new byte[2048]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/rss+xml");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client, maxResponseBytes: 1024);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            transport.GetAsync(
                new Uri("https://rss.moddb.com/downloads/feed/rss.xml"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transport_rejects_unexpected_media_type()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new StringContent("<rss/>", Encoding.UTF8, "text/html");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            transport.GetAsync(
                new Uri("https://rss.moddb.com/downloads/feed/rss.xml"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transport_propagates_pre_cancelled_request()
    {
        var handler = new RecordingHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(handler);
        var transport = new SyndicationFeedTransport(client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.GetAsync(new Uri("https://rss.moddb.com/downloads/feed/rss.xml"), cts.Token));
    }

    [Fact]
    public async Task Parser_and_transport_require_https_sources()
    {
        Assert.Throws<ArgumentException>(() =>
            SyndicationFeedParser.Parse("<rss/>", new Uri("http://rss.moddb.com/downloads/feed/rss.xml")));

        using var client = new HttpClient(new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("HTTP request should not be sent.")));
        var transport = new SyndicationFeedTransport(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            transport.GetAsync(
                new Uri("http://rss.moddb.com/downloads/feed/rss.xml"),
                TestContext.Current.CancellationToken));
    }

    private static string ReadFixture(string directory, string name)
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", directory, name),
            Encoding.UTF8);
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
