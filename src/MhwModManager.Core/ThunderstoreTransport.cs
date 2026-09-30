using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class ThunderstoreTransportResponse : IDisposable
{
    public ThunderstoreTransportResponse(
        HttpStatusCode statusCode,
        JsonDocument? document,
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
    public JsonDocument? Document { get; }
    public string? ETag { get; }
    public DateTimeOffset? LastModified { get; }
    public bool NotModified
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return StatusCode == HttpStatusCode.NotModified;
        }
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Document?.Dispose();
    }
}

public sealed class ThunderstoreTransportException : HttpRequestException
{
    public ThunderstoreTransportException(
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

public sealed class ThunderstoreTransport
{
    private static readonly Uri DefaultBaseUri = new("https://thunderstore.io/");

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public ThunderstoreTransport(
        HttpClient client,
        Uri? baseUri = null,
        int maxResponseBytes = 32 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 128 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        this.client = client;
        this.baseUri = NormalizeBaseUri(baseUri ?? DefaultBaseUri);
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = NormalizeUserAgent(userAgent);
    }

    public Task<ThunderstoreTransportResponse> GetPackagesAsync(
        string communityIdentifier,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var community = ThunderstoreCatalogIdentity.NormalizeCommunityIdentifier(
            communityIdentifier,
            nameof(communityIdentifier));
        return SendJsonAsync(
            $"c/{community}/api/v1/package/",
            requireArrayRoot: true,
            ifModifiedSince,
            ct);
    }

    public Task<ThunderstoreTransportResponse> GetPackageAsync(
        string communityIdentifier,
        string packageUuid,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var community = ThunderstoreCatalogIdentity.NormalizeCommunityIdentifier(
            communityIdentifier,
            nameof(communityIdentifier));
        var packageId = ThunderstoreCatalogIdentity.NormalizeUuid(
            packageUuid,
            nameof(packageUuid));
        return SendJsonAsync(
            $"c/{community}/api/v1/package/{packageId}/",
            requireArrayRoot: false,
            ifModifiedSince: null,
            ct);
    }

    private async Task<ThunderstoreTransportResponse> SendJsonAsync(
        string relativePath,
        bool requireArrayRoot,
        DateTimeOffset? ifModifiedSince,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var uri = new Uri(baseUri, relativePath);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.IfModifiedSince = ifModifiedSince;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(
                "Thunderstore API request failed before receiving a response.",
                ex,
                ex.StatusCode);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new ThunderstoreTransportResponse(
                    response.StatusCode,
                    document: null,
                    response.Headers.ETag?.ToString(),
                    response.Content?.Headers.LastModified);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ThunderstoreTransportException(
                    response.StatusCode,
                    ParseRetryAfter(response.Headers.RetryAfter),
                    $"Thunderstore API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
            }

            if (response.Content is null)
                throw new InvalidDataException("Thunderstore API returned a successful response without a body.");

            var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(bytes);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Thunderstore API returned malformed JSON.", ex);
            }

            try
            {
                var expectedKind = requireArrayRoot ? JsonValueKind.Array : JsonValueKind.Object;
                if (document.RootElement.ValueKind != expectedKind)
                {
                    throw new InvalidDataException(
                        requireArrayRoot
                            ? "Thunderstore package-list response must be a JSON array."
                            : "Thunderstore package-detail response must be a JSON object.");
                }

                return new ThunderstoreTransportResponse(
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
            throw new InvalidDataException($"Thunderstore API response exceeded the {maxResponseBytes} byte safety limit.");

        var encodings = content.Headers.ContentEncoding;
        if (encodings.Any(value =>
                !value.Equals("gzip", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Thunderstore API returned an unsupported content encoding.");
        }

        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        if (encodings.Any(value => value.Equals("gzip", StringComparison.OrdinalIgnoreCase)))
        {
            await using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: false);
            return await ReadBoundedStreamAsync(gzip, ct).ConfigureAwait(false);
        }

        return await ReadBoundedStreamAsync(source, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadBoundedStreamAsync(Stream stream, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidDataException($"Thunderstore API response exceeded the {maxResponseBytes} byte safety limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("thunderstore.io", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Thunderstore API base URI must be exactly the HTTPS thunderstore.io origin.",
                nameof(uri));
        }

        return new Uri("https://thunderstore.io/", UriKind.Absolute);
    }

    private static string NormalizeUserAgent(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("User-Agent must be 1..128 characters without line breaks.", nameof(value));
        return value.Trim();
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (header?.Delta is not null)
            return header.Delta;
        if (header?.Date is not null)
        {
            var delay = header.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
