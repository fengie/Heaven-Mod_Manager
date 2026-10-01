using System.Net;
using System.Net.Http.Headers;

namespace MhwModManager.Core;

public sealed record CatalogCrawlerManifest(
    string ProviderId,
    Uri BaseUri,
    Uri DocumentationUri,
    Uri TermsUri,
    DateOnly TermsReviewedOn,
    Uri RobotsUri,
    DateOnly RobotsReviewedOn,
    IReadOnlyList<string> AllowedPathPrefixes,
    string KillSwitchEnvironmentVariable,
    int MaxResponseBytes = 1024 * 1024,
    string UserAgent = "MHW-Manual-Mod-Manager",
    bool AttributionRequired = false,
    string? AttributionText = null)
{
    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId,
                CatalogSourceKind.Html,
                DocumentationUri,
                TermsUri,
                TermsReviewedOn,
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: false,
                AllowsHtmlParsing: true,
                AttributionRequired,
                RobotsUri,
                RobotsReviewedOn,
                AttributionText);
        }
    }

    public void Validate(DateOnly today)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        CatalogProviderComplianceValidator.EnsureUsable(Compliance, today);
        if (!BaseUri.IsAbsoluteUri
            || !BaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(BaseUri.Query)
            || !string.IsNullOrEmpty(BaseUri.Fragment)
            || !string.IsNullOrEmpty(BaseUri.UserInfo))
        {
            throw new InvalidOperationException("Crawler base URI must be credential-free absolute HTTPS.");
        }

        if (AllowedPathPrefixes is null || AllowedPathPrefixes.Count == 0)
            throw new InvalidOperationException("Crawler manifest requires at least one allowed path prefix.");
        foreach (var prefix in AllowedPathPrefixes)
            ValidateAllowedPathPrefix(prefix);

        if (string.IsNullOrWhiteSpace(KillSwitchEnvironmentVariable)
            || KillSwitchEnvironmentVariable.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
        {
            throw new InvalidOperationException("Crawler kill-switch environment variable is malformed.");
        }

        if (MaxResponseBytes is < 1024 or > 8 * 1024 * 1024)
            throw new InvalidOperationException("Crawler response bound must be between 1 KiB and 8 MiB.");
        if (string.IsNullOrWhiteSpace(UserAgent) || UserAgent.Length > 128 || UserAgent.Contains('\r') || UserAgent.Contains('\n'))
            throw new InvalidOperationException("Crawler User-Agent must be 1..128 characters without line breaks.");
    }

    private static void ValidateAllowedPathPrefix(string prefix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(prefix)
            || prefix[0] != '/'
            || prefix.Contains('\\')
            || prefix.Contains('?')
            || prefix.Contains('#')
            || prefix.Any(char.IsControl)
            || (prefix.Length > 1 && prefix.Contains("//", StringComparison.Ordinal))
            || prefix.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("%5c", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Crawler path prefixes must be canonical absolute paths without query, fragment, backslash, duplicate separators, or encoded separators.");
        }

        foreach (var segment in prefix.Split('/', StringSplitOptions.None))
        {
            if (HasMalformedPercentEncoding(segment))
            {
                throw new InvalidOperationException(
                    "Crawler path prefixes must use valid percent encoding.");
            }

            var decoded = Uri.UnescapeDataString(segment);
            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\'))
            {
                throw new InvalidOperationException(
                    "Crawler path prefixes must not contain encoded separators or dot-segment traversal.");
            }
        }
    }
}

public sealed class PermittedCatalogCrawler : IDisposable
{
    private const int MaxRedirectHops = 5;

    private readonly HttpClient client;
    private readonly CatalogCrawlerManifest manifest;

    public PermittedCatalogCrawler(CatalogCrawlerManifest manifest)
        : this(CreateDefaultHandler(), manifest)
    {
    }

    internal PermittedCatalogCrawler(HttpMessageHandler handler, CatalogCrawlerManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(handler);
        this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        client = new HttpClient(handler, disposeHandler: true);
    }

    internal static HttpMessageHandler CreateDefaultHandler()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
    }

    public async Task<string> FetchHtmlAsync(
        Uri uri,
        DateOnly complianceDate,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        manifest.Validate(complianceDate);
        if (IsKillSwitchEnabled())
            throw new InvalidOperationException($"Crawler '{manifest.ProviderId}' is disabled by its local kill switch.");

        var currentUri = uri;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redirectCount = 0;

        while (true)
        {
            EnsureAllowedUri(currentUri);
            if (!visited.Add(BuildRequestKey(currentUri)))
                throw new InvalidOperationException("Crawler redirect loop detected.");

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.TryAddWithoutValidation("User-Agent", manifest.UserAgent);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            var effective = response.RequestMessage?.RequestUri ?? currentUri;
            EnsureAllowedUri(effective);
            if (!string.Equals(
                    BuildRequestKey(effective),
                    BuildRequestKey(currentUri),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Crawler transport followed or rewrote the request URI; automatic redirects are forbidden.");
            }

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaxRedirectHops)
                    throw new InvalidOperationException($"Crawler exceeded the {MaxRedirectHops}-redirect safety limit.");

                var nextUri = ResolveRedirectUri(currentUri, response.Headers);
                EnsureAllowedUri(nextUri);
                currentUri = nextUri;
                redirectCount++;
                continue;
            }

            response.EnsureSuccessStatusCode();

            var mediaType = response.Content?.Headers.ContentType?.MediaType;
            if (mediaType is null
                || (!mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                    && !mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Crawler response content type is not HTML.");
            }

            if (response.Content is null)
                throw new InvalidDataException("Crawler returned no response body.");
            if (response.Content.Headers.ContentLength is long declared && declared > manifest.MaxResponseBytes)
                throw new InvalidDataException($"Crawler response exceeded the {manifest.MaxResponseBytes} byte safety limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var memory = new MemoryStream(Math.Min(manifest.MaxResponseBytes, 64 * 1024));
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > manifest.MaxResponseBytes)
                    throw new InvalidDataException($"Crawler response exceeded the {manifest.MaxResponseBytes} byte safety limit.");
                memory.Write(buffer, 0, read);
            }

            return System.Text.Encoding.UTF8.GetString(memory.ToArray());
        }
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        client.Dispose();
    }

    private bool IsKillSwitchEnabled()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (Environment.GetEnvironmentVariable(manifest.KillSwitchEnvironmentVariable) ?? string.Empty)
            .Trim()
            .ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private void EnsureAllowedUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!uri.IsAbsoluteUri
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.IdnHost, manifest.BaseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || uri.Port != manifest.BaseUri.Port)
        {
            throw new InvalidOperationException("Crawler request escaped the manifest-approved HTTPS origin.");
        }

        var path = uri.AbsolutePath;
        if (!manifest.AllowedPathPrefixes.Any(prefix => PathMatchesPrefix(path, prefix)))
            throw new InvalidOperationException("Crawler request escaped the manifest-approved path prefixes.");
    }

    private static bool PathMatchesPrefix(string path, string prefix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (prefix == "/")
            return true;

        var boundary = prefix.EndsWith("/", StringComparison.Ordinal)
            ? prefix[..^1]
            : prefix;

        return path.Equals(boundary, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(boundary + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (int)statusCode is 301 or 302 or 303 or 307 or 308;
    }

    private static Uri ResolveRedirectUri(Uri currentUri, HttpResponseHeaders headers)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!headers.TryGetValues("Location", out var values))
            throw new InvalidDataException("Crawler redirect response omitted the Location header.");

        var locations = values.ToArray();
        if (locations.Length != 1 || string.IsNullOrWhiteSpace(locations[0]))
            throw new InvalidDataException("Crawler redirect Location header must contain exactly one URI.");

        try
        {
            if (!Uri.TryCreate(locations[0], UriKind.RelativeOrAbsolute, out var location))
                throw new InvalidDataException("Crawler redirect Location header is malformed.");

            return location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidDataException("Crawler redirect Location header is malformed.", ex);
        }
    }

    private static bool HasMalformedPercentEncoding(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
                continue;

            if (index + 2 >= value.Length
                || !IsHexDigit(value[index + 1])
                || !IsHexDigit(value[index + 2]))
            {
                return true;
            }

            index += 2;
        }

        return false;
    }

    private static bool IsHexDigit(char value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
    }

    private static string BuildRequestKey(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }
}
