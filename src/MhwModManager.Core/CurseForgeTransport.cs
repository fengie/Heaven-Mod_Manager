using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class CurseForgeTransportException(
    HttpStatusCode statusCode,
    TimeSpan? retryAfter,
    string message)
    : HttpRequestException(message, null, statusCode)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class CurseForgeTransportResponse : IDisposable
{
    public CurseForgeTransportResponse(HttpStatusCode statusCode, JsonDocument document)
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

public sealed class CurseForgeTransport
{
    public static readonly Uri ProductionBaseUri = new("https://api.curseforge.com/");
    private const int DefaultMaxResponseBytes = 4 * 1024 * 1024;

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly string apiKey;
    private readonly string userAgent;
    private readonly int maxResponseBytes;

    public CurseForgeTransport(
        HttpClient client,
        string apiKey,
        Uri? baseUri = null,
        int maxResponseBytes = DefaultMaxResponseBytes,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (apiKey.Length > 4096 || apiKey.Contains('\r') || apiKey.Contains('\n'))
            throw new ArgumentException("CurseForge API key is malformed.", nameof(apiKey));
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length > 128 || userAgent.Contains('\r') || userAgent.Contains('\n'))
            throw new ArgumentException("User-Agent must be 1..128 characters without line breaks.", nameof(userAgent));

        this.client = client;
        this.apiKey = apiKey.Trim();
        this.baseUri = NormalizeBaseUri(baseUri ?? ProductionBaseUri);
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = userAgent.Trim();
    }

    public Task<CurseForgeTransportResponse> SearchModsAsync(
        int gameId,
        string? query,
        int sortField,
        int index,
        int pageSize,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (pageSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var parameters = new List<(string Key, string Value)>
        {
            ("gameId", gameId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("sortField", sortField.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("sortOrder", "desc"),
            ("index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrWhiteSpace(query))
            parameters.Add(("searchFilter", query.Trim()));

        return SendJsonAsync("v1/mods/search", parameters, ct);
    }

    public Task<CurseForgeTransportResponse> GetModAsync(
        long modId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(modId);
        return SendJsonAsync(
            $"v1/mods/{modId.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            [],
            ct);
    }

    public Task<CurseForgeTransportResponse> GetModFilesAsync(
        long modId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(modId);
        return SendJsonAsync(
            $"v1/mods/{modId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/files",
            [("pageSize", "50")],
            ct);
    }

    public Task<CurseForgeTransportResponse> GetDownloadUrlAsync(
        long modId,
        long fileId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(modId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileId);
        return SendJsonAsync(
            $"v1/mods/{modId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/files/{fileId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/download-url",
            [],
            ct);
    }

    private async Task<CurseForgeTransportResponse> SendJsonAsync(
        string relativePath,
        List<(string Key, string Value)> query,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var queryText = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var relative = query.Count == 0 ? relativePath : relativePath + "?" + queryText;
        var uri = new Uri(baseUri, relative);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new CurseForgeTransportException(
                response.StatusCode,
                ParseRetryAfter(response.Headers.RetryAfter),
                $"CurseForge API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
        }

        if (response.Content is null)
            throw new InvalidDataException("CurseForge API returned a successful response without a body.");

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("CurseForge API returned malformed JSON.", ex);
        }

        return new CurseForgeTransportResponse(response.StatusCode, document);
    }

    private async Task<byte[]> ReadBoundedContentAsync(HttpContent content, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (content.Headers.ContentLength is long length && length > maxResponseBytes)
            throw new InvalidDataException($"CurseForge API response exceeded the {maxResponseBytes} byte safety limit.");

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > maxResponseBytes)
                throw new InvalidDataException($"CurseForge API response exceeded the {maxResponseBytes} byte safety limit.");
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("CurseForge API base URI must be credential-free absolute HTTPS.", nameof(uri));
        }

        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (header?.Delta is not null) return header.Delta;
        if (header?.Date is null) return null;
        var delay = header.Date.Value - DateTimeOffset.UtcNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }
}
