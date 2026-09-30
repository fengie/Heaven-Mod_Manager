using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class GitHubCatalogCredential
{
    private readonly string token;

    public GitHubCatalogCredential(string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("GitHub credential value is required.", nameof(token));
        if (token.Length > 8192 || token.Contains('\r') || token.Contains('\n'))
            throw new ArgumentException("GitHub credential value is invalid.", nameof(token));
        this.token = token;
    }

    internal void Apply(HttpRequestMessage request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public sealed record GitHubReleaseAssetSnapshot(
    long Id,
    string Name,
    string? Label,
    string ContentType,
    long SizeBytes,
    long DownloadCount,
    DateTimeOffset? CreatedAt,
    Uri BrowserDownloadUri);

public sealed record GitHubReleaseSnapshot(
    long Id,
    string TagName,
    string Name,
    string Body,
    string Author,
    Uri HtmlUri,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<GitHubReleaseAssetSnapshot> Assets);

public sealed record GitHubReleaseTransportResult(
    GitHubReleaseSnapshot? Release,
    CatalogRateLimit? RateLimit);

public sealed class GitHubReleaseTransportException : HttpRequestException
{
    public GitHubReleaseTransportException(
        HttpStatusCode statusCode,
        CatalogRateLimit? rateLimit,
        string message)
        : base(message, null, statusCode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RateLimit = rateLimit;
    }

    public CatalogRateLimit? RateLimit { get; }
}

public sealed class GitHubReleasesTransport
{
    public static readonly Uri ProductionBaseUri = new("https://api.github.com/");
    private const string ApiVersion = "2026-03-10";

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly GitHubCatalogCredential? credential;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public GitHubReleasesTransport(
        HttpClient client,
        GitHubCatalogCredential? credential = null,
        Uri? baseUri = null,
        int maxResponseBytes = 4 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "Response bound must be between 1 KiB and 64 MiB.");

        this.client = client;
        this.baseUri = NormalizeBaseUri(baseUri ?? ProductionBaseUri);
        if (credential is not null
            && !this.baseUri.Host.Equals(ProductionBaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "GitHub credentials may only be sent to the canonical api.github.com origin.",
                nameof(baseUri));
        }

        this.credential = credential;
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Task<GitHubReleaseTransportResult> GetLatestReleaseAsync(
        string owner,
        string repository,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var path = $"repos/{EscapeSegment(owner, nameof(owner))}/{EscapeSegment(repository, nameof(repository))}/releases/latest";
        return SendReleaseAsync(path, ct);
    }

    public async Task<CatalogRateLimit> GetRateLimitAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var response = await SendAsync("rate_limit", ct).ConfigureAwait(false);
        var observed = DateTimeOffset.UtcNow;
        var headerLimit = ParseRateLimit(response, observed);

        if (!response.IsSuccessStatusCode)
            throw CreateTransportException(response, headerLimit);

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var resources = RequireNestedObject(document.RootElement, "resources", "GitHub rate-limit response");
            var core = RequireNestedObject(resources, "core", "GitHub rate-limit resources");
            var limit = RequireInt32(core, "limit", "GitHub rate-limit core resource");
            var remaining = RequireInt32(core, "remaining", "GitHub rate-limit core resource");
            var reset = RequireInt64(core, "reset", "GitHub rate-limit core resource");
            var retryAfter = remaining == 0
                ? DateTimeOffset.FromUnixTimeSeconds(reset)
                : (DateTimeOffset?)null;
            return new CatalogRateLimit(
                limit,
                remaining,
                null,
                null,
                retryAfter,
                observed);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("GitHub rate-limit API returned malformed JSON.", ex);
        }
    }

    private async Task<GitHubReleaseTransportResult> SendReleaseAsync(
        string relativePath,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var response = await SendAsync(relativePath, ct).ConfigureAwait(false);
        var observed = DateTimeOffset.UtcNow;
        var rateLimit = ParseRateLimit(response, observed);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new GitHubReleaseTransportResult(null, rateLimit);

        if (!response.IsSuccessStatusCode)
            throw CreateTransportException(response, rateLimit);

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return new GitHubReleaseTransportResult(ParseRelease(document.RootElement), rateLimit);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("GitHub Releases API returned malformed JSON.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string relativePath, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        credential?.Apply(request);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadBoundedContentAsync(HttpContent? content, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (content is null)
            throw new InvalidDataException("GitHub API returned a successful response without a body.");
        if (content.Headers.ContentLength is long declaredLength && declaredLength > maxResponseBytes)
            throw new InvalidDataException($"GitHub API response exceeded the {maxResponseBytes} byte safety limit.");

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidDataException($"GitHub API response exceeded the {maxResponseBytes} byte safety limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static GitHubReleaseTransportException CreateTransportException(
        HttpResponseMessage response,
        CatalogRateLimit? rateLimit)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new GitHubReleaseTransportException(
            response.StatusCode,
            rateLimit,
            $"GitHub Releases API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
    }

    private static GitHubReleaseSnapshot ParseRelease(JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RequireObject(root, "GitHub release response");
        if (OptionalBoolean(root, "draft") == true)
            throw new InvalidDataException("GitHub latest-release response unexpectedly contained a draft release.");

        var htmlUri = RequireHttpsUri(
            RequireString(root, "html_url", "GitHub release"),
            "GitHub release html_url",
            requireGitHubHost: true);
        var assets = RequireArray(root, "assets", "GitHub release")
            .EnumerateArray()
            .Select(ParseAsset)
            .ToArray();
        var author = RequireNestedObject(root, "author", "GitHub release");

        return new GitHubReleaseSnapshot(
            RequireInt64(root, "id", "GitHub release"),
            RequireString(root, "tag_name", "GitHub release"),
            OptionalString(root, "name") ?? RequireString(root, "tag_name", "GitHub release"),
            OptionalString(root, "body") ?? string.Empty,
            RequireString(author, "login", "GitHub release author"),
            htmlUri,
            OptionalDateTimeOffset(root, "created_at"),
            OptionalDateTimeOffset(root, "published_at"),
            assets);
    }

    private static GitHubReleaseAssetSnapshot ParseAsset(JsonElement asset)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RequireObject(asset, "GitHub release asset");
        var browserDownload = RequireHttpsUri(
            RequireString(asset, "browser_download_url", "GitHub release asset"),
            "GitHub release asset browser_download_url",
            requireGitHubHost: true);

        return new GitHubReleaseAssetSnapshot(
            RequireInt64(asset, "id", "GitHub release asset"),
            RequireString(asset, "name", "GitHub release asset"),
            OptionalString(asset, "label"),
            OptionalString(asset, "content_type") ?? "application/octet-stream",
            RequireInt64(asset, "size", "GitHub release asset"),
            RequireInt64(asset, "download_count", "GitHub release asset"),
            OptionalDateTimeOffset(asset, "created_at"),
            browserDownload);
    }

    private static CatalogRateLimit? ParseRateLimit(HttpResponseMessage response, DateTimeOffset observed)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var limit = ParseHeaderInt32(response, "X-RateLimit-Limit");
        var remaining = ParseHeaderInt32(response, "X-RateLimit-Remaining");
        var resetEpoch = ParseHeaderInt64(response, "X-RateLimit-Reset");
        var retryAfter = response.Headers.RetryAfter?.Delta is TimeSpan delta
            ? observed.Add(delta)
            : response.Headers.RetryAfter?.Date;

        if (retryAfter is null && remaining == 0 && resetEpoch is not null)
            retryAfter = DateTimeOffset.FromUnixTimeSeconds(resetEpoch.Value);
        if (limit is null && remaining is null && retryAfter is null)
            return null;

        return new CatalogRateLimit(limit, remaining, null, null, retryAfter, observed);
    }

    private static int? ParseHeaderInt32(HttpResponseMessage response, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static long? ParseHeaderInt64(HttpResponseMessage response, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var value = values.FirstOrDefault();
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static JsonElement RequireProperty(JsonElement element, string name, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(name, out var property))
            throw new InvalidDataException($"{context} is missing required property '{name}'.");
        return property;
    }

    private static JsonElement RequireObject(JsonElement element, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context} must be a JSON object.");
        return element;
    }

    private static JsonElement RequireNestedObject(JsonElement element, string name, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var property = RequireProperty(element, name, context);
        return RequireObject(property, $"{context} property '{name}'");
    }

    private static JsonElement RequireArray(JsonElement element, string name, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var property = RequireProperty(element, name, context);
        if (property.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{context} property '{name}' must be an array.");
        return property;
    }

    private static string RequireString(JsonElement element, string name, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var property = RequireProperty(element, name, context);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidDataException($"{context} property '{name}' must be a non-empty string.");
        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"GitHub API property '{name}' must be a string when present.");
        return property.GetString();
    }

    private static bool? OptionalBoolean(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"GitHub API property '{name}' must be a boolean when present.")
        };
    }

    private static long RequireInt64(JsonElement element, string name, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var property = RequireProperty(element, name, context);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value))
            throw new InvalidDataException($"{context} property '{name}' must be an integer.");
        return value;
    }

    private static int RequireInt32(JsonElement element, string name, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequireInt64(element, name, context);
        if (value is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException($"{context} property '{name}' is outside Int32 range.");
        return (int)value;
    }

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var text = OptionalString(element, name);
        if (text is null) return null;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw new InvalidDataException($"GitHub API property '{name}' is not a valid timestamp.");
        return value;
    }

    private static Uri RequireHttpsUri(string value, string context, bool requireGitHubHost)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException($"{context} must be an absolute HTTPS URI without user information.");
        if (requireGitHubHost && !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} must use github.com.");
        return uri;
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GitHub API base URI must be absolute HTTPS.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("GitHub API base URI must not contain user info, query, or fragment components.", nameof(uri));

        var text = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(text, UriKind.Absolute);
    }

    private static string NormalizeUserAgent(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("GitHub User-Agent must be 1..128 safe characters.", nameof(value));
        return value.Trim();
    }

    private static string EscapeSegment(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
            throw new ArgumentException("GitHub path segment must be 1..100 characters.", parameterName);

        var normalized = value.Trim();
        if (normalized.Contains('/') || normalized.Contains('\\') || normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException("GitHub path segment contains invalid characters.", parameterName);
        return Uri.EscapeDataString(normalized);
    }
}
