using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class ModIoApiKey
{
    private readonly string value;

    public ModIoApiKey(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("mod.io API key is invalid.", nameof(value));
        this.value = value;
    }

    internal string Value => value;
}

public sealed class ModIoTransportResponse : IDisposable
{
    public ModIoTransportResponse(HttpStatusCode statusCode, JsonDocument document, string? etag, DateTimeOffset? lastModified)
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
    private readonly Uri baseUri;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public ModIoTransport(
        HttpClient client,
        Uri baseUri,
        int maxResponseBytes = 4 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        this.client = client;
        this.baseUri = NormalizeBaseUri(baseUri);
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Task<ModIoTransportResponse> GetModsAsync(
        int gameId,
        ModIoApiKey apiKey,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(apiKey);
        ValidatePositiveId(gameId, nameof(gameId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));

        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods",
            [
                ("_limit", limit.ToString(CultureInfo.InvariantCulture)),
                ("_offset", offset.ToString(CultureInfo.InvariantCulture))
            ],
            apiKey,
            requireDataEnvelope: true,
            ct);
    }

    public Task<ModIoTransportResponse> GetModAsync(
        int gameId,
        long modId,
        ModIoApiKey apiKey,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(apiKey);
        ValidatePositiveId(gameId, nameof(gameId));
        ValidatePositiveId(modId, nameof(modId));
        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods/{modId.ToString(CultureInfo.InvariantCulture)}",
            Array.Empty<(string Key, string Value)>(),
            apiKey,
            requireDataEnvelope: false,
            ct);
    }

    public Task<ModIoTransportResponse> GetModFilesAsync(
        int gameId,
        long modId,
        ModIoApiKey apiKey,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(apiKey);
        ValidatePositiveId(gameId, nameof(gameId));
        ValidatePositiveId(modId, nameof(modId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));

        return SendJsonAsync(
            $"games/{gameId.ToString(CultureInfo.InvariantCulture)}/mods/{modId.ToString(CultureInfo.InvariantCulture)}/files",
            [
                ("_limit", limit.ToString(CultureInfo.InvariantCulture)),
                ("_offset", offset.ToString(CultureInfo.InvariantCulture))
            ],
            apiKey,
            requireDataEnvelope: true,
            ct);
    }

    private async Task<ModIoTransportResponse> SendJsonAsync(
        string relativePath,
        IReadOnlyList<(string Key, string Value)> query,
        ModIoApiKey apiKey,
        bool requireDataEnvelope,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var pairs = new List<(string Key, string Value)>(query.Count + 1) { ("api_key", apiKey.Value) };
        pairs.AddRange(query);
        var queryText = string.Join("&", pairs.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var uri = new Uri(baseUri, relativePath + "?" + queryText);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
            if (requireDataEnvelope
                && (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array))
            {
                throw new InvalidDataException("mod.io list response is missing the required data array.");
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

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("mod.io API base URI must be absolute HTTPS.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("mod.io API base URI must not contain query or fragment components.", nameof(uri));

        var host = uri.Host;
        var productionHost = host.EndsWith(".modapi.io", StringComparison.OrdinalIgnoreCase);
        var testHost = host.EndsWith(".test.mod.io", StringComparison.OrdinalIgnoreCase);
        if (!productionHost && !testHost)
            throw new ArgumentException("mod.io API base URI must use an official mod.io API host.", nameof(uri));

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.Equals("/v1", StringComparison.Ordinal))
            throw new ArgumentException("mod.io API base URI must target the /v1 API path.", nameof(uri));

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

    private static void ValidatePositiveId(long value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
    }
}
