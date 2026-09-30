using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class ModIoCredential
{
    private readonly string apiKey;
    private readonly string? accessToken;

    private ModIoCredential(string apiKey, string? accessToken)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.apiKey = NormalizeSecret(apiKey, nameof(apiKey));
        this.accessToken = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : NormalizeSecret(accessToken, nameof(accessToken));
    }

    public static ModIoCredential ReadOnly(string apiKey)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new ModIoCredential(apiKey, null);
    }

    public static ModIoCredential Authenticated(string apiKey, string accessToken)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new ModIoCredential(apiKey, accessToken);
    }

    internal void AddApiKey(List<(string Key, string Value)> query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(query);
        query.Add(("api_key", apiKey));
    }

    internal void ApplyAccessToken(HttpRequestMessage request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string NormalizeSecret(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("mod.io credential value is invalid.", parameterName);
        return value.Trim();
    }
}

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
    private readonly HttpClient client;
    private readonly long gameId;
    private readonly ModIoCredential credential;
    private readonly Uri baseUri;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public ModIoTransport(
        HttpClient client,
        long gameId,
        ModIoCredential credential,
        int maxResponseBytes = 4 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        this.client = client;
        this.gameId = gameId;
        this.credential = credential;
        this.baseUri = new Uri($"https://g-{gameId}.modapi.io/v1/");
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Task<ModIoTransportResponse> GetModsAsync(
        CatalogBrowseMode mode,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        var sort = mode switch
        {
            CatalogBrowseMode.Latest => "-date_live",
            CatalogBrowseMode.RecentlyUpdated => "-date_updated",
            CatalogBrowseMode.Popular => "-downloads_total",
            CatalogBrowseMode.Trending => "-downloads_today",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        return SendJsonAsync(
            $"games/{gameId}/mods",
            [
                ("_sort", sort),
                ("_limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("_offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ],
            requireDataArray: true,
            ct);
    }

    public Task<ModIoTransportResponse> GetModAsync(
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        return SendJsonAsync(
            $"games/{gameId}/mods/{modId}",
            Array.Empty<(string, string)>(),
            requireDataArray: false,
            ct);
    }

    public Task<ModIoTransportResponse> GetModFilesAsync(
        string providerModId,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        return SendJsonAsync(
            $"games/{gameId}/mods/{modId}/files",
            [
                ("_limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("_offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ],
            requireDataArray: true,
            ct);
    }

    public Task<ModIoTransportResponse> GetModFileAsync(
        string providerModId,
        string providerFileId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        var fileId = NormalizePositiveId(providerFileId, nameof(providerFileId));
        return SendJsonAsync(
            $"games/{gameId}/mods/{modId}/files/{fileId}",
            Array.Empty<(string, string)>(),
            requireDataArray: false,
            ct);
    }

    private async Task<ModIoTransportResponse> SendJsonAsync(
        string relativePath,
        (string Key, string Value)[] query,
        bool requireDataArray,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var allQuery = new List<(string Key, string Value)>(query.Count + 1);
        foreach (var pair in query) allQuery.Add(pair);
        credential.AddApiKey(allQuery);

        var queryText = string.Join(
            "&",
            allQuery.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var uri = new Uri(baseUri, relativePath + "?" + queryText);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        credential.ApplyAccessToken(request);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException("mod.io API request failed before a response was received.");
        }

        using (response)
        {
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

            try
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("mod.io API response must be a JSON object.");

                if (requireDataArray
                    && (!document.RootElement.TryGetProperty("data", out var data)
                        || data.ValueKind != JsonValueKind.Array))
                {
                    throw new InvalidDataException("mod.io API list response is missing the required data array.");
                }

                return new ModIoTransportResponse(
                    response.StatusCode,
                    document,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified);
            }
            catch
            {
                document.Dispose();
                throw;
            }
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
        {
            throw new ArgumentException("mod.io id must be a positive integer.", parameterName);
        }

        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
