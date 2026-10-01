using System.Net;
using System.Text;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class PermittedCatalogCrawlerTests
{
    [Fact]
    public void Default_transport_disables_automatic_redirects()
    {
        using var handler = Assert.IsType<HttpClientHandler>(PermittedCatalogCrawler.CreateDefaultHandler());

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task Approved_manifest_fetches_only_approved_html_path()
    {
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                Assert.Equal("catalog.example", request.RequestUri?.Host);
                Assert.Equal("/mods/fixture", request.RequestUri?.AbsolutePath);
                return Task.FromResult(HtmlResponse("<html>ok</html>"));
            }),
            CreateManifest());

        var html = await crawler.FetchHtmlAsync(
            new Uri("https://catalog.example/mods/fixture"),
            new DateOnly(2026, 9, 30),
            TestContext.Current.CancellationToken);

        Assert.Contains("ok", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Segment_boundary_allows_exact_prefix_but_rejects_neighbor_before_network()
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                attempts.Add(Assert.IsType<Uri>(request.RequestUri));
                return Task.FromResult(HtmlResponse("<html>ok</html>"));
            }),
            CreateManifest() with { AllowedPathPrefixes = ["/mods"] });

        var html = await crawler.FetchHtmlAsync(
            new Uri("https://catalog.example/mods"),
            new DateOnly(2026, 9, 30),
            TestContext.Current.CancellationToken);
        Assert.Contains("ok", html, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods-evil/fixture"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));

        Assert.Single(attempts);
        Assert.Equal("/mods", attempts[0].AbsolutePath);
    }

    [Theory]
    [InlineData("mods/")]
    [InlineData("/mods?preview=1")]
    [InlineData("/mods#fragment")]
    [InlineData("/mods\\admin")]
    [InlineData("/mods//nested")]
    [InlineData("/mods/../admin")]
    [InlineData("/mods/%2fadmin")]
    [InlineData("/mods/%2e%2e/admin")]
    [InlineData("/mods/%zz/admin")]
    public void Malformed_or_ambiguous_path_prefixes_fail_closed(string prefix)
    {
        var manifest = CreateManifest() with { AllowedPathPrefixes = [prefix] };

        Assert.Throws<InvalidOperationException>(
            () => manifest.Validate(new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public async Task Allowed_redirect_is_followed_manually()
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                var requestUri = Assert.IsType<Uri>(request.RequestUri);
                attempts.Add(requestUri);

                if (requestUri.AbsolutePath == "/mods/start")
                    return Task.FromResult(RedirectResponse("/mods/final"));

                return Task.FromResult(HtmlResponse("<html>redirected</html>"));
            }),
            CreateManifest());

        var html = await crawler.FetchHtmlAsync(
            new Uri("https://catalog.example/mods/start"),
            new DateOnly(2026, 9, 30),
            TestContext.Current.CancellationToken);

        Assert.Contains("redirected", html, StringComparison.Ordinal);
        Assert.Equal(2, attempts.Count);
        Assert.Equal("/mods/start", attempts[0].AbsolutePath);
        Assert.Equal("/mods/final", attempts[1].AbsolutePath);
    }

    [Theory]
    [InlineData("https://other.example/mods/final")]
    [InlineData("http://catalog.example/mods/final")]
    [InlineData("https://catalog.example:444/mods/final")]
    [InlineData("https://user@catalog.example/mods/final")]
    [InlineData("https://catalog.example/forum/final")]
    [InlineData("https://catalog.example/mods-evil/final")]
    [InlineData("https://catalog.example/mods/%2fadmin")]
    [InlineData("https://catalog.example/mods/%2e%2e/admin")]
    public async Task Disallowed_redirect_target_is_never_contacted(string target)
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                attempts.Add(Assert.IsType<Uri>(request.RequestUri));
                return Task.FromResult(RedirectResponse(target));
            }),
            CreateManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/start"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));

        var attempt = Assert.Single(attempts);
        Assert.Equal("https://catalog.example/mods/start", attempt.AbsoluteUri);
    }

    [Fact]
    public async Task Redirect_without_location_fails_without_follow_up_request()
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                attempts.Add(Assert.IsType<Uri>(request.RequestUri));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found));
            }),
            CreateManifest());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/start"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));

        Assert.Single(attempts);
    }

    [Fact]
    public async Task Malformed_redirect_location_fails_without_follow_up_request()
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                attempts.Add(Assert.IsType<Uri>(request.RequestUri));
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.TryAddWithoutValidation("Location", "http://[::1");
                return Task.FromResult(response);
            }),
            CreateManifest());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/start"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));

        Assert.Single(attempts);
    }

    [Fact]
    public async Task Redirect_loop_is_rejected_before_repeating_a_request()
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                var requestUri = Assert.IsType<Uri>(request.RequestUri);
                attempts.Add(requestUri);
                return Task.FromResult(
                    requestUri.AbsolutePath == "/mods/start"
                        ? RedirectResponse("/mods/step")
                        : RedirectResponse("/mods/start"));
            }),
            CreateManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/start"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, attempts.Count);
        Assert.Equal("/mods/start", attempts[0].AbsolutePath);
        Assert.Equal("/mods/step", attempts[1].AbsolutePath);
    }

    [Fact]
    public async Task Redirect_limit_is_bounded_before_next_hop_is_contacted()
    {
        var attempts = new List<Uri>();
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                var requestUri = Assert.IsType<Uri>(request.RequestUri);
                attempts.Add(requestUri);
                var current = int.Parse(requestUri.Segments[^1], System.Globalization.CultureInfo.InvariantCulture);
                return Task.FromResult(RedirectResponse($"/mods/hop/{current + 1}"));
            }),
            CreateManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/hop/0"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));

        Assert.Equal(6, attempts.Count);
        Assert.Equal("/mods/hop/5", attempts[^1].AbsolutePath);
    }

    [Fact]
    public async Task Transport_uri_rewrite_is_rejected()
    {
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                var response = HtmlResponse("<html>unexpected</html>");
                response.RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://other.example/mods/final");
                return Task.FromResult(response);
            }),
            CreateManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/start"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Kill_switch_fails_closed_before_network()
    {
        const string variable = "MHW_TEST_CRAWLER_DISABLED";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "1");
        try
        {
            using var crawler = new PermittedCatalogCrawler(
                new RoutingHandler((_, _) =>
                    throw new Xunit.Sdk.XunitException("Disabled crawler must not perform a request.")),
                CreateManifest(variable));

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
    [InlineData("https://catalog.example/mods-evil/fixture")]
    [InlineData("https://catalog.example/mods/%2fadmin")]
    [InlineData("https://catalog.example/mods/%2e%2e/admin")]
    [InlineData("http://catalog.example/mods/fixture")]
    public async Task Origin_scheme_and_path_escape_fail_before_network(string uri)
    {
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((_, _) =>
                throw new Xunit.Sdk.XunitException("Rejected crawler URI must not perform a request.")),
            CreateManifest());

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
        using var crawler = new PermittedCatalogCrawler(
            new RoutingHandler((_, _) =>
                throw new Xunit.Sdk.XunitException("Stale compliance must not perform a request.")),
            manifest);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => crawler.FetchHtmlAsync(
                new Uri("https://catalog.example/mods/fixture"),
                new DateOnly(2026, 9, 30),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unexpected_content_type_and_oversized_body_fail_closed()
    {
        using (var jsonCrawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }),
            CreateManifest()))
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => jsonCrawler.FetchHtmlAsync(
                    new Uri("https://catalog.example/mods/fixture"),
                    new DateOnly(2026, 9, 30),
                    TestContext.Current.CancellationToken));
        }

        using var boundedCrawler = new PermittedCatalogCrawler(
            new RoutingHandler((request, _) =>
            {
                var response = HtmlResponse(new string('x', 4096));
                response.RequestMessage = request;
                return Task.FromResult(response);
            }),
            CreateManifest() with { MaxResponseBytes = 1024 });
        await Assert.ThrowsAsync<InvalidDataException>(
            () => boundedCrawler.FetchHtmlAsync(
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

    private static HttpResponseMessage RedirectResponse(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
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
