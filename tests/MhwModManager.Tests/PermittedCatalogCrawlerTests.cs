using System.Net;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class PermittedCatalogCrawlerTests
{
    [Fact]
    public async Task Approved_manifest_fetches_only_approved_html_path()
    {
        using var client = new HttpClient(new RoutingHandler((request, _) =>
        {
            Assert.Equal("catalog.example", request.RequestUri?.Host);
            Assert.Equal("/mods/fixture", request.RequestUri?.AbsolutePath);
            return Task.FromResult(HtmlResponse("<html>ok</html>"));
        }));
        var crawler = new PermittedCatalogCrawler(client, CreateManifest());

        var html = await crawler.FetchHtmlAsync(
            new Uri("https://catalog.example/mods/fixture"),
            new DateOnly(2026, 9, 30),
            TestContext.Current.CancellationToken);

        Assert.Contains("ok", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kill_switch_fails_closed_before_network()
    {
        const string variable = "MHW_TEST_CRAWLER_DISABLED";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "1");
        try
        {
            using var client = new HttpClient(new RoutingHandler((_, _) =>
                throw new Xunit.Sdk.XunitException("Disabled crawler must not perform a request.")));
            var crawler = new PermittedCatalogCrawler(client, CreateManifest(variable));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => crawler.FetchHtmlAsync(
                    new Uri("https://catalog.example/mods/fixture"),
                    new DateOnly(2026, 9, 30),
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Theory]
    [InlineData("https://other.example/mods/fixture")]
    [InlineData("https://catalog.example/forum/fixture")]
    [InlineData("http://catalog.example/mods/fixture")]
    public async Task Origin_scheme_and_path_escape_fail_before_network(string uri)
    {
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Rejected crawler URI must not perform a request.")));
        var crawler = new PermittedCatalogCrawler(client, CreateManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri(uri),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_or_stale_robots_review_fails_closed()
    {
        var manifest = CreateManifest() with { RobotsReviewedOn = new DateOnly(2026, 1, 1) };
        using var client = new HttpClient(new RoutingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("Stale compliance must not perform a request.")));
        var crawler = new PermittedCatalogCrawler(client, manifest);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/fixture"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unexpected_content_type_and_oversized_body_fail_closed()
    {
        using (var jsonClient = new HttpClient(new RoutingHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        })))
        {
            var crawler = new PermittedCatalogCrawler(jsonClient, CreateManifest());
            await Assert.ThrowsAsync<InvalidDataException>(
                () => crawler.FetchHtmlAsync(
                    new Uri("https://catalog.example/mods/fixture"),
                    new DateOnly(2026, 9, 30),
                    TestContext.Current.CancellationToken));
        }

        using var largeClient = new HttpClient(new RoutingHandler((request, _) =>
        {
            var response = HtmlResponse(new string('x', 4096));
            response.RequestMessage = request;
            return Task.FromResult(response);
        }));
        var bounded = new PermittedCatalogCrawler(largeClient, CreateManifest() with { MaxResponseBytes = 1024 });
        await Assert.ThrowsAsync<InvalidDataException>(
            () => bounded.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/fixture"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));
    }

    private static CatalogCrawlerManifest CreateManifest(string killSwitch = "MHW_TEST_CRAWLER_OFF")
    {
        return new CatalogCrawlerManifest(
            "fixture-html",
            new Uri("https://catalog.example/"),
            new Uri("https://catalog.example/developers"),
            new Uri("https://catalog.example/terms"),
            new DateOnly(2026, 9, 30),
            new Uri("https://catalog.example/robots.txt"),
            new DateOnly(2026, 9, 30),
            ["/mods/"],
            killSwitch);
    }

    private static HttpResponseMessage HtmlResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
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
