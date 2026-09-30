using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class ModIoTransportResponse : IDisposable
{
    public ModIoTransportResponse(
        HttpStatusCode statusCode,
        JsonDocument document,
        string? etag,
        DateTimeOffset? lastModified)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        StatusCode = statusCode;
        Document = document;
        ETag = etag;
        LastModified = lastModified;
    }

    public HttpStatusCode StatusCode { get; }
    public JsonDocument Document { get; }
    public string? ETag { get; }
    public DateTimeOffset? LastModified { get; }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Document.Dispose();
    }
}

public sealed class ModIoTransportException : HttpRequestException
{
    public ModIoTransportException(
        HttpStatusCode statusCode,
        TimeSpan? retryAfter,
        string message)
        : base(message, null, statusCode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

public sealed class ModIoTransport
{
    private static readonly HashSet<string> AllowedSorts = new(StringComparer.Ordinal)
    {
        "-date_live",
        "-date_updated",
        "-downloads_total",
        "-downloads_today",
        "-ratings_weighted_aggregate"
    };

    private readonly HttpClient client;
    private readonly string apiKey;
    private readonly Uri baseUri;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public ModIoTransport(
        HttpClient client,
        string apiKey,
        Uri apiBaseUri,
        int maxResponseBytes = 8 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        this.client = client;
        this.apiKey = NormalizeApiKey(apiKey);
        baseUri = NormalizeBaseUri(apiBaseUri);
        this.baseUri = baseUri;
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Task<ModIoTransportResponse> GetModsAsync(
        int gameId,
        string? query,
        string sort,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidatePositiveId(gameId, nameof(gameId));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var normalizedSort = NormalizeSort(sort);

        var parameters = new List<(string Key, string Value)>
        {
            ("_offset", offset.ToString(CultureInfo.InvariantCulture)),
            ("_limit", limit.ToString(CultureInfo.InvariantCulture)),
            ("_sort", normalizedSort)
        };

        if (!string.IsNullOrWhiteSpace(query))
            parameters.Add(("_q", query.Trim()));

        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods",
            parameters,
            ct);
    }

    public Task<ModIoTransportResponse> GetModAsync(
        int gameId,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidatePositiveId(gameId, nameof(gameId));
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods/{modId}",
            Array.Empty<(string Key, string Value)>(),
            ct);
    }

    public Task<ModIoTransportResponse> GetModFilesAsync(
        int gameId,
        string providerModId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidatePositiveId(gameId, nameof(gameId));
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));

        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods/{modId}/files",
            [
                ("_offset", offset.ToString(CultureInfo.InvariantCulture)),
                ("_limit", limit.ToString(CultureInfo.InvariantCulture)),
                ("_sort", "-date_added")
            ],
            ct);
    }

    public Task<ModIoTransportResponse> GetModFileAsync(
        int gameId,
        string providerModId,
        string providerFileId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidatePositiveId(gameId, nameof(gameId));
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        var fileId = NormalizePositiveId(providerFileId, nameof(providerFileId));

        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods/{modId}/files/{fileId}",
            Array.Empty<(string Key, string Value)>(),
            ct);
    }

    private async Task<ModIoTransportResponse> SendJsonAsync(
        string relativePath,
        IReadOnlyList<(string Key, string Value)> query,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var parameters = new List<(string Key, string Value)>(query.Count + 1)
        {
            ("api_key", apiKey)
        };
        parameters.AddRange(query);

        var queryText = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var uri = new Uri(baseUri, relativePath + "?" + queryText);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ModIoTransportException(
                response.StatusCode,
                ParseRetryAfter(response.Headers.RetryAfter),
                $"mod.io API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
        }

        if (response.Content is null)
            throw new InvalidDataException("mod.io API returned a successful response without a body.");

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("mod.io API returned malformed JSON.", ex);
        }

        return new ModIoTransportResponse(
            response.StatusCode,
            document,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified);
    }

    private async Task<byte[]> ReadBoundedContentAsync(HttpContent content, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (content.Headers.ContentLength is long length && length > maxResponseBytes)
            throw new InvalidDataException($"mod.io API response exceeded the {maxResponseBytes} byte safety limit.");

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidDataException($"mod.io API response exceeded the {maxResponseBytes} byte safety limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string NormalizeApiKey(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("mod.io API key is required.", nameof(value));

        var normalized = value.Trim();
        if (normalized.Length is < 16 or > 256
            || normalized.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch)))
        {
            throw new ArgumentException("mod.io API key has an invalid format.", nameof(value));
        }

        return normalized;
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("mod.io API base URI must be absolute HTTPS.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("mod.io API base URI must not contain query or fragment components.", nameof(uri));

        var host = uri.IdnHost;
        if (!host.Equals("modapi.io", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".modapi.io", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("mod.io API credentials may only be sent to modapi.io hosts.", nameof(uri));
        }

        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static string NormalizeUserAgent(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Contains('\r')
            || value.Contains('\n'))
        {
            throw new ArgumentException("User-Agent must be 1..128 characters without line breaks.", nameof(value));
        }

        return value.Trim();
    }

    private static string NormalizeSort(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || !AllowedSorts.Contains(value.Trim()))
            throw new ArgumentException("Unsupported mod.io sort expression.", nameof(value));
        return value.Trim();
    }

    private static string NormalizePositiveId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var id) || id <= 0)
        {
            throw new ArgumentException("mod.io IDs must be positive integers.", parameterName);
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static void ValidatePositiveId(int value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (header?.Delta is not null) return header.Delta;
        if (header?.Date is not null)
        {
            var delay = header.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
