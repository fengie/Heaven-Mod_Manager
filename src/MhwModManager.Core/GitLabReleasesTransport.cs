using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class GitLabCatalogCredential
{
    private readonly string token;

    public GitLabCatalogCredential(string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("GitLab credential value is required.", nameof(token));
        if (token.Length > 8192 || token.Contains('\r') || token.Contains('\n'))
            throw new ArgumentException("GitLab credential value is invalid.", nameof(token));
        this.token = token;
    }

    internal void Apply(HttpRequestMessage request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
    }
}

public sealed record GitLabReleaseAssetSnapshot(
    long Id,
    string Name,
    string LinkType,
    Uri? DirectAssetUri);

public sealed record GitLabReleaseSnapshot(
    string TagName,
    string Name,
    string Description,
    string Author,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ReleasedAt,
    IReadOnlyList<GitLabReleaseAssetSnapshot> Assets);

public sealed record GitLabReleaseTransportResult(
    GitLabReleaseSnapshot? Release,
    CatalogRateLimit? RateLimit);

public sealed class GitLabReleaseTransportException : HttpRequestException
{
    public GitLabReleaseTransportException(
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

public sealed class GitLabReleasesTransport
{
    public static readonly Uri ProductionBaseUri = new("https://gitlab.com/api/v4/");

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly GitLabCatalogCredential? credential;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public GitLabReleasesTransport(
        HttpClient client,
        GitLabCatalogCredential? credential = null,
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
        if (credential is not null && !this.baseUri.Equals(ProductionBaseUri))
        {
            throw new ArgumentException(
                "GitLab credentials may only be sent to the canonical gitlab.com API origin.",
                nameof(baseUri));
        }

        this.credential = credential;
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Uri WebBaseUri
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new Uri($"{baseUri.Scheme}://{baseUri.Host}/", UriKind.Absolute);
        }
    }

    public Task<GitLabReleaseTransportResult> GetLatestReleaseAsync(
        string projectPath,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var encodedProject = EscapeProjectPath(projectPath, nameof(projectPath));
        return SendReleaseAsync($"projects/{encodedProject}/releases/permalink/latest", ct);
    }

    private async Task<GitLabReleaseTransportResult> SendReleaseAsync(
        string relativePath,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var response = await SendAsync(relativePath, ct).ConfigureAwait(false);
        var observed = DateTimeOffset.UtcNow;
        var rateLimit = ParseRateLimit(response, observed);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new GitLabReleaseTransportResult(null, rateLimit);

        if (!response.IsSuccessStatusCode)
            throw CreateTransportException(response, rateLimit);

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return new GitLabReleaseTransportResult(
                ParseRelease(document.RootElement, baseUri.Host),
                rateLimit);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("GitLab Releases API returned malformed JSON.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string relativePath, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        credential?.Apply(request);
        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadBoundedContentAsync(HttpContent? content, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (content is null)
            throw new InvalidDataException("GitLab API returned a successful response without a body.");
        if (content.Headers.ContentLength is long declaredLength && declaredLength > maxResponseBytes)
            throw new InvalidDataException($"GitLab API response exceeded the {maxResponseBytes} byte safety limit.");

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidDataException($"GitLab API response exceeded the {maxResponseBytes} byte safety limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static GitLabReleaseTransportException CreateTransportException(
        HttpResponseMessage response,
        CatalogRateLimit? rateLimit)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new GitLabReleaseTransportException(
            response.StatusCode,
            rateLimit,
            $"GitLab Releases API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
    }

    private static GitLabReleaseSnapshot ParseRelease(JsonElement root, string expectedHost)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RequireObject(root, "GitLab release response");

        if (OptionalBoolean(root, "upcoming_release") == true)
            throw new InvalidDataException("GitLab latest-release response unexpectedly contained an upcoming release.");

        var authorObject = RequireNestedObject(root, "author", "GitLab release");
        var assetsObject = RequireNestedObject(root, "assets", "GitLab release");
        var links = RequireArray(assetsObject, "links", "GitLab release assets");
        var assets = links
            .EnumerateArray()
            .Select(link => ParseAsset(link, expectedHost))
            .ToArray();

        var tagName = RequireString(root, "tag_name", "GitLab release");
        return new GitLabReleaseSnapshot(
            tagName,
            OptionalString(root, "name") ?? tagName,
            OptionalString(root, "description") ?? string.Empty,
            OptionalString(authorObject, "username")
                ?? RequireString(authorObject, "name", "GitLab release author"),
            OptionalDateTimeOffset(root, "created_at"),
            OptionalDateTimeOffset(root, "released_at"),
            assets);
    }

    private static GitLabReleaseAssetSnapshot ParseAsset(JsonElement asset, string expectedHost)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RequireObject(asset, "GitLab release asset");
        var linkType = OptionalString(asset, "link_type") ?? "other";
        var directUri = OptionalString(asset, "direct_asset_url") is string direct
            ? RequireHttpsUri(direct, "GitLab release asset direct_asset_url", expectedHost)
            : null;

        return new GitLabReleaseAssetSnapshot(
            RequireInt64(asset, "id", "GitLab release asset"),
            RequireString(asset, "name", "GitLab release asset"),
            linkType,
            directUri);
    }

    private static CatalogRateLimit? ParseRateLimit(HttpResponseMessage response, DateTimeOffset observed)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var limit = ParseHeaderInt32(response, "RateLimit-Limit");
        var remaining = ParseHeaderInt32(response, "RateLimit-Remaining");
        var retryAfter = response.Headers.RetryAfter?.Delta is TimeSpan delta
            ? observed.Add(delta)
            : response.Headers.RetryAfter?.Date;

        if (retryAfter is null
            && response.Headers.TryGetValues("RateLimit-ResetTime", out var resetValues))
        {
            var value = resetValues.FirstOrDefault();
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var reset))
            {
                retryAfter = reset;
            }
        }

        if (limit is null && remaining is null && retryAfter is null)
            return null;

        return new CatalogRateLimit(limit, remaining, null, null, retryAfter, observed);
    }

    private static int? ParseHeaderInt32(HttpResponseMessage response, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
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
            throw new InvalidDataException($"GitLab API property '{name}' must be a string when present.");
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
            _ => throw new InvalidDataException($"GitLab API property '{name}' must be a boolean when present.")
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

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var text = OptionalString(element, name);
        if (text is null) return null;
        if (!DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value))
        {
            throw new InvalidDataException($"GitLab API property '{name}' is not a valid timestamp.");
        }
        return value;
    }

    private static Uri RequireHttpsUri(string value, string context, string expectedHost)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException($"{context} must be an absolute HTTPS URI without user information.");
        }

        if (!uri.Host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} must use the configured GitLab host.");
        return uri;
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GitLab API base URI must be absolute HTTPS.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "GitLab API base URI must not contain user info, query, or fragment components.",
                nameof(uri));
        }

        var text = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(text, UriKind.Absolute);
    }

    private static string NormalizeUserAgent(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Contains('\r')
            || value.Contains('\n'))
        {
            throw new ArgumentException("GitLab User-Agent must be 1..128 safe characters.", nameof(value));
        }
        return value.Trim();
    }

    private static string EscapeProjectPath(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
            throw new ArgumentException("GitLab project path must be 1..255 characters.", parameterName);

        var normalized = value.Trim().Trim('/');
        if (normalized.Length == 0
            || normalized.Contains('\\')
            || normalized.Contains('\r')
            || normalized.Contains('\n'))
        {
            throw new ArgumentException("GitLab project path contains invalid characters.", parameterName);
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment =>
            string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment.Length > 100))
        {
            throw new ArgumentException("GitLab project path contains an invalid segment.", parameterName);
        }

        return Uri.EscapeDataString(normalized);
    }
}
