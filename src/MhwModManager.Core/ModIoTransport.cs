using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class ModIoCredential
{
    private readonly string apiKey;

    public ModIoCredential(string apiKey)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 256
            || apiKey.Contains('\r') || apiKey.Contains('\n'))
            throw new ArgumentException("mod.io API key is invalid.", nameof(apiKey));
        this.apiKey = apiKey.Trim();
    }

    internal string ApiKey
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return apiKey;
        }
    }
}

public sealed class ModIoTransportResponse : IDisposable
{
    public ModIoTransportResponse(HttpStatusCode statusCode, JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        StatusCode = statusCode;
        Document = document;
    }

    public HttpStatusCode StatusCode { get; }
    public JsonDocument Document { get; }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Document.Dispose();
    }
}

public sealed class ModIoTransportException : HttpRequestException
{
    public ModIoTransportException(HttpStatusCode statusCode, TimeSpan? retryAfter, string message)
        : base(message, null, statusCode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

public sealed class ModIoTransport
{
    private readonly HttpClient client;
    private readonly ModIoCredential credential;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public ModIoTransport(
        HttpClient client,
        ModIoCredential credential,
        int maxResponseBytes = 4 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(credential);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length > 128
            || userAgent.Contains('\r') || userAgent.Contains('\n'))
            throw new ArgumentException("User-Agent must be 1..128 characters without line breaks.", nameof(userAgent));

        this.client = client;
        this.credential = credential;
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = userAgent.Trim();
    }

    public Task<ModIoTransportResponse> GetModsAsync(
        int gameId,
        int offset,
        int limit,
        string sort,
        string? query = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateGameId(gameId);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (sort is not ("-date_updated" or "-date_live" or "-downloads"))
            throw new ArgumentException("Unsupported mod.io sort.", nameof(sort));

        var parameters = new List<(string Key, string Value)>
        {
            ("_offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("_limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("_sort", sort)
        };
        if (!string.IsNullOrWhiteSpace(query))
            parameters.Add(("_q", query.Trim()));

        return SendAsync(gameId, $"games/{gameId}/mods", parameters, ct);
    }

    public Task<ModIoTransportResponse> GetModAsync(
        int gameId,
        string modId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateGameId(gameId);
        var normalizedModId = NormalizePositiveId(modId, nameof(modId));
        return SendAsync(gameId, $"games/{gameId}/mods/{normalizedModId}", [], ct);
    }

    public Task<ModIoTransportResponse> GetModFilesAsync(
        int gameId,
        string modId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateGameId(gameId);
        var normalizedModId = NormalizePositiveId(modId, nameof(modId));
        return SendAsync(
            gameId,
            $"games/{gameId}/mods/{normalizedModId}/files",
            [("_limit", "100"), ("_sort", "-date_added")],
            ct);
    }

    public Task<ModIoTransportResponse> GetModFileAsync(
        int gameId,
        string modId,
        string fileId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateGameId(gameId);
        var normalizedModId = NormalizePositiveId(modId, nameof(modId));
        var normalizedFileId = NormalizePositiveId(fileId, nameof(fileId));
        return SendAsync(
            gameId,
            $"games/{gameId}/mods/{normalizedModId}/files/{normalizedFileId}",
            [],
            ct);
    }

    private async Task<ModIoTransportResponse> SendAsync(
        int gameId,
        string relativePath,
        IReadOnlyList<(string Key, string Value)> query,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var baseUri = new Uri($"https://g-{gameId}.modapi.io/v1/", UriKind.Absolute);
        var allQuery = query.Concat([("api_key", credential.ApiKey)]);
        var queryText = string.Join("&", allQuery.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var uri = new Uri(baseUri, relativePath + "?" + queryText);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("X-Modio-Platform", "WINDOWS");

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
        try
        {
            return new ModIoTransportResponse(response.StatusCode, JsonDocument.Parse(bytes));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("mod.io API returned malformed JSON.", ex);
        }
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

    private static string NormalizePositiveId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(
            value?.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var id) || id <= 0)
            throw new ArgumentException("mod.io ID must be a positive integer.", parameterName);
        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateGameId(int gameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
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
