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
        {
            if (string.IsNullOrWhiteSpace(prefix) || !prefix.StartsWith('/', StringComparison.Ordinal))
                throw new InvalidOperationException("Crawler path prefixes must be non-empty absolute-path prefixes.");
        }

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
}

public sealed class PermittedCatalogCrawler(HttpClient client, CatalogCrawlerManifest manifest)
{
    private readonly HttpClient client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly CatalogCrawlerManifest manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

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

        EnsureAllowedUri(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.TryAddWithoutValidation("User-Agent", manifest.UserAgent);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var effective = response.RequestMessage?.RequestUri ?? uri;
        EnsureAllowedUri(effective);

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
        if (!manifest.AllowedPathPrefixes.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Crawler request escaped the manifest-approved path prefixes.");
        }
    }
}
