using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MhwModManager.Core;

public enum NexusV3CredentialKind
{
    ApiKey,
    Bearer
}

public sealed class NexusV3Credential
{
    private readonly NexusV3CredentialKind kind;
    private readonly string secret;

    private NexusV3Credential(NexusV3CredentialKind kind, string secret)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Credential value is required.", nameof(secret));
        if (secret.Length > 8192 || secret.Contains('\r') || secret.Contains('\n'))
            throw new ArgumentException("Credential value is invalid.", nameof(secret));

        this.kind = kind;
        this.secret = secret;
    }

    public static NexusV3Credential ApiKey(string apiKey)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new NexusV3Credential(NexusV3CredentialKind.ApiKey, apiKey);
    }

    public static NexusV3Credential Bearer(string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new NexusV3Credential(NexusV3CredentialKind.Bearer, token);
    }

    internal void Apply(HttpRequestMessage request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);

        if (kind == NexusV3CredentialKind.ApiKey)
        {
            request.Headers.TryAddWithoutValidation("apikey", secret);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
    }
}

public sealed record NexusV3ConditionalRequest(string? ETag = null, DateTimeOffset? LastModified = null);

public sealed class NexusV3TransportResponse : IDisposable
{
    public NexusV3TransportResponse(
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

    public bool IsNotModified
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

public sealed class NexusV3TransportException : HttpRequestException
{
    public NexusV3TransportException(
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

public sealed class NexusV3Transport
{
    public static readonly Uri ProductionBaseUri = new("https://api.nexusmods.com/v3/");

    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly int maxResponseBytes;
    private readonly string applicationName;

    public NexusV3Transport(
        HttpClient client,
        Uri? baseUri = null,
        int maxResponseBytes = 4 * 1024 * 1024,
        string applicationName = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "Response bound must be between 1 KiB and 64 MiB.");

        this.client = client;
        this.baseUri = NormalizeBaseUri(baseUri ?? ProductionBaseUri);
        this.maxResponseBytes = maxResponseBytes;
        this.applicationName = NormalizeApplicationName(applicationName);
    }

    public Task<NexusV3TransportResponse> GetTrendingModsAsync(
        string gameDomain,
        NexusV3ConditionalRequest? conditional = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var path = $"games/{EscapeSegment(gameDomain, nameof(gameDomain))}/trending-mods";
        return SendJsonAsync(path, credential: null, conditional, ct);
    }

    public Task<NexusV3TransportResponse> GetModAsync(
        string gameDomain,
        string gameScopedId,
        NexusV3Credential credential,
        NexusV3ConditionalRequest? conditional = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(credential);
        var path = $"games/{EscapeSegment(gameDomain, nameof(gameDomain))}/mods/{EscapeSegment(gameScopedId, nameof(gameScopedId))}";
        return SendJsonAsync(path, credential, conditional, ct);
    }

    public Task<NexusV3TransportResponse> GetModFilesAsync(
        string modId,
        NexusV3Credential credential,
        NexusV3ConditionalRequest? conditional = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(credential);
        var path = $"mods/{EscapeSegment(modId, nameof(modId))}/files";
        return SendJsonAsync(path, credential, conditional, ct);
    }

    public Task<NexusV3TransportResponse> GetModFileVersionsAsync(
        string modFileId,
        NexusV3Credential credential,
        NexusV3ConditionalRequest? conditional = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(credential);
        var path = $"mod-files/{EscapeSegment(modFileId, nameof(modFileId))}/versions";
        return SendJsonAsync(path, credential, conditional, ct);
    }

    private async Task<NexusV3TransportResponse> SendJsonAsync(
        string relativePath,
        NexusV3Credential? credential,
        NexusV3ConditionalRequest? conditional,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Application-Name", applicationName);
        request.Headers.TryAddWithoutValidation("User-Agent", applicationName);

        credential?.Apply(request);
        ApplyConditionalHeaders(request, conditional);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        var etag = response.Headers.ETag?.ToString();
        var lastModified = response.Content?.Headers.LastModified;

        if (response.StatusCode == HttpStatusCode.NotModified)
            return new NexusV3TransportResponse(response.StatusCode, null, etag, lastModified);

        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = ParseRetryAfter(response.Headers.RetryAfter);
            throw new NexusV3TransportException(
                response.StatusCode,
                retryAfter,
                $"Nexus Mods API v3 request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown"}).");
        }

        if (response.Content is null)
            throw new InvalidDataException("Nexus Mods API v3 returned a successful response without a body.");

        var bytes = await ReadBoundedContentAsync(response.Content, ct).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Nexus Mods API v3 returned malformed JSON.", ex);
        }

        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out _))
            {
                throw new InvalidDataException("Nexus Mods API v3 response is missing the required data envelope.");
            }

            return new NexusV3TransportResponse(response.StatusCode, document, etag, lastModified);
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
        if (content.Headers.ContentLength is long declaredLength && declaredLength > maxResponseBytes)
            throw new InvalidDataException($"Nexus Mods API v3 response exceeded the {maxResponseBytes} byte safety limit.");

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxResponseBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidDataException($"Nexus Mods API v3 response exceeded the {maxResponseBytes} byte safety limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void ApplyConditionalHeaders(
        HttpRequestMessage request,
        NexusV3ConditionalRequest? conditional)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (conditional is null) return;

        if (!string.IsNullOrWhiteSpace(conditional.ETag))
        {
            if (conditional.ETag.Contains('\r') || conditional.ETag.Contains('\n'))
                throw new ArgumentException("ETag contains invalid characters.", nameof(conditional));
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(conditional.ETag));
        }

        if (conditional.LastModified is not null)
            request.Headers.IfModifiedSince = conditional.LastModified;
    }

    private static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (header is null) return null;
        if (header.Delta is not null) return header.Delta;
        if (header.Date is not null)
        {
            var delay = header.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return null;
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Nexus API base URI must be absolute HTTPS.", nameof(uri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Nexus API base URI must not contain query or fragment components.", nameof(uri));

        var text = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(text, UriKind.Absolute);
    }

    private static string NormalizeApplicationName(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("Application name must be 1..128 characters.", nameof(value));
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("Application name contains invalid characters.", nameof(value));
        return value.Trim();
    }

    private static string EscapeSegment(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            throw new ArgumentException("Nexus API path segment must be 1..256 characters.", parameterName);
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("Nexus API path segment contains invalid characters.", parameterName);
        return Uri.EscapeDataString(value.Trim());
    }
}
