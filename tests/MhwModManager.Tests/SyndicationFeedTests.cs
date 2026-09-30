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
    public void Parser_and_transport_require_https_sources()
    {
        Assert.Throws<ArgumentException>(() =>
            SyndicationFeedParser.Parse("<rss/>", new Uri("http://rss.moddb.com/downloads/feed/rss.xml")));

        using var client = new HttpClient(new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("HTTP request should not be sent.")));
        var transport = new SyndicationFeedTransport(client);

        Assert.ThrowsAsync<ArgumentException>(() =>
            transport.GetAsync(new Uri("http://rss.moddb.com/downloads/feed/rss.xml")));
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
