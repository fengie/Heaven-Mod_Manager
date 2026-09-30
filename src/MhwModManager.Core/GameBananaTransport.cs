using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class GameBananaTransportResponse : IDisposable
{
    public GameBananaTransportResponse(
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

public sealed class GameBananaTransportException : HttpRequestException
{
    public GameBananaTransportException(
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

public sealed class GameBananaTransport
{
    public static readonly Uri ProductionBaseUri = new("https://api.gamebanana.com/");

    private static readonly string[] ModFields =
    [
        "name",
        "description",
        "downloads",
        "likes",
        "date",
        "mdate",
        "Owner().name",
        "Category().name",
        "RootCategory().name",
        "Game().name",
        "Files().aFiles()",
        "Preview().sStructuredDataFullsizeUrl()",
        "Url().sDownloadUrl()",
        "Url().sProfileUrl()"
    ];

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public GameBananaTransport(
        HttpClient client,
        Uri? baseUri = null,
        int maxResponseBytes = 4 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        this.client = client;
        this.baseUri = NormalizeBaseUri(baseUri ?? ProductionBaseUri);
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Task<GameBananaTransportResponse> GetNewModsAsync(
        int gameId,
        int page,
        bool includeUpdated,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));

        return SendJsonAsync(
            "Core/List/New",
            [
                ("itemtype", "Mod"),
                ("gameid", gameId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("include_updated", includeUpdated ? "1" : "0"),
                ("format", "json_min"),
                ("flags", "JSON_UNESCAPED_SLASHES")
            ],
            ct);
    }

    public Task<GameBananaTransportResponse> GetModDataAsync(
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));

        return SendJsonAsync(
            "Core/Item/Data",
            [
                ("itemtype", "Mod"),
                ("itemid", modId),
                ("fields", string.Join(",", ModFields)),
                ("return_keys", "1"),
                ("format", "json_min"),
                ("flags", "JSON_UNESCAPED_SLASHES")
            ],
            ct);
    }

    private async Task<GameBananaTransportResponse> SendJsonAsync(
        string relativePath,
        IReadOnlyList<(string Key, string Value)> query,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var queryText = string.Join(
            "&",
            query.Select(pair =>
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
            throw new GameBananaTransportException(
                response.StatusCode,
                ParseRetryAfter(response.Headers.RetryAfter),
                $"GameBanana API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
        }

        if (response.Content is null)
            throw new InvalidDataException("GameBanana API returned a successful response without a body.");

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("GameBanana API returned malformed JSON.", ex);
        }

        return new GameBananaTransportResponse(
            response.StatusCode,
            document,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified);
    }

    private async Task<byte[]> ReadBoundedContentAsync(HttpContent content, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (content.Headers.ContentLength is long length && length > maxResponseBytes)
            throw new InvalidDataException($"GameBanana API response exceeded the {maxResponseBytes} byte safety limit.");

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidDataException($"GameBanana API response exceeded the {maxResponseBytes} byte safety limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string NormalizePositiveId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(
            value?.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var id) || id <= 0)
        {
            throw new ArgumentException("GameBanana item ID must be a positive integer.", parameterName);
        }

        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GameBanana API base URI must be absolute HTTPS.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("GameBanana API base URI must not contain query or fragment components.", nameof(uri));

        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static string NormalizeUserAgent(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("User-Agent must be 1..128 characters without line breaks.", nameof(value));
        return value.Trim();
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
