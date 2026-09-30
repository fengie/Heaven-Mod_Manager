using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MhwModManager.Core;

public sealed record SyndicationFeedEntry(
    string Id,
    string Title,
    string Summary,
    string Author,
    Uri Link,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Categories);

public sealed record SyndicationFeedSnapshot(
    string Title,
    string Description,
    Uri SourceUri,
    IReadOnlyList<SyndicationFeedEntry> Entries);

public sealed record SyndicationFeedTransportResult(
    SyndicationFeedSnapshot Feed,
    string? ETag,
    DateTimeOffset? LastModified);

public sealed record SyndicationFeedConditionalResult(
    SyndicationFeedSnapshot? Feed,
    string? ETag,
    DateTimeOffset? LastModified,
    bool NotModified);

public sealed class SyndicationFeedTransportException : HttpRequestException
{
    public SyndicationFeedTransportException(HttpStatusCode statusCode, DateTimeOffset? retryAfter, string message)
        : base(message, null, statusCode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RetryAfter = retryAfter;
    }

    public DateTimeOffset? RetryAfter { get; }
}

public static class SyndicationFeedParser
{
    private const int MaxTextLength = 64 * 1024;

    public static SyndicationFeedSnapshot Parse(string xml, Uri sourceUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(xml);
        return Parse(System.Text.Encoding.UTF8.GetBytes(xml), sourceUri);
    }

    public static SyndicationFeedSnapshot Parse(ReadOnlyMemory<byte> payload, Uri sourceUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!sourceUri.IsAbsoluteUri || !string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Syndication feed source URI must be absolute HTTPS.", nameof(sourceUri));
        if (payload.IsEmpty)
            throw new InvalidDataException("Syndication feed payload is empty.");

        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = Math.Max(payload.Length * 4L, 4096)
        };

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Syndication feed XML is malformed.", ex);
        }

        var root = document.Root ?? throw new InvalidDataException("Syndication feed XML has no document root.");
        if (root.Name.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase))
            return ParseRss(root, sourceUri);
        if (root.Name.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase))
            return ParseAtom(root, sourceUri);
        throw new InvalidDataException($"Unsupported syndication feed root '{root.Name.LocalName}'.");
    }

    private static SyndicationFeedSnapshot ParseRss(XElement root, Uri sourceUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var channel = Child(root, "channel") ?? throw new InvalidDataException("RSS feed is missing its channel element.");
        var title = CleanText(Value(channel, "title"));
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidDataException("RSS feed is missing a channel title.");

        var entries = channel.Elements()
            .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
            .Select(item => ParseRssItem(item, sourceUri))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();

        return new SyndicationFeedSnapshot(title, CleanText(Value(channel, "description")), sourceUri, entries);
    }

    private static SyndicationFeedEntry? ParseRssItem(XElement item, Uri sourceUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var title = CleanText(Value(item, "title"));
        if (string.IsNullOrWhiteSpace(title) || !TryResolveHttpsUri(sourceUri, Value(item, "link"), out var link))
            return null;

        var id = CleanText(Value(item, "guid"));
        if (string.IsNullOrWhiteSpace(id))
            id = link.AbsoluteUri;

        var author = CleanText(Value(item, "creator"));
        if (string.IsNullOrWhiteSpace(author))
            author = CleanText(Value(item, "author"));

        var categories = item.Elements()
            .Where(element => element.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))
            .Select(element => CleanText(element.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var published = ParseDate(Value(item, "pubDate"));
        return new SyndicationFeedEntry(
            id,
            title,
            CleanText(Value(item, "description")),
            author,
            link,
            published,
            ParseDate(Value(item, "updated")) ?? published,
            categories);
    }

    private static SyndicationFeedSnapshot ParseAtom(XElement root, Uri sourceUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var title = CleanText(Value(root, "title"));
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidDataException("Atom feed is missing a title.");

        var entries = root.Elements()
            .Where(element => element.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase))
            .Select(entry => ParseAtomEntry(entry, sourceUri))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();

        return new SyndicationFeedSnapshot(title, CleanText(Value(root, "subtitle")), sourceUri, entries);
    }

    private static SyndicationFeedEntry? ParseAtomEntry(XElement entry, Uri sourceUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var title = CleanText(Value(entry, "title"));
        var linkElement = entry.Elements()
            .Where(element => element.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(element =>
                string.IsNullOrWhiteSpace((string?)element.Attribute("rel")) ||
                string.Equals((string?)element.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(title) ||
            !TryResolveHttpsUri(sourceUri, (string?)linkElement?.Attribute("href"), out var link))
            return null;

        var id = CleanText(Value(entry, "id"));
        if (string.IsNullOrWhiteSpace(id))
            id = link.AbsoluteUri;

        var summary = CleanText(Value(entry, "summary"));
        if (string.IsNullOrWhiteSpace(summary))
            summary = CleanText(Value(entry, "content"));

        var authorElement = Child(entry, "author");
        var author = authorElement is null ? string.Empty : CleanText(Value(authorElement, "name"));

        var categories = entry.Elements()
            .Where(element => element.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))
            .Select(element => CleanText((string?)element.Attribute("term") ?? element.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SyndicationFeedEntry(
            id,
            title,
            summary,
            author,
            link,
            ParseDate(Value(entry, "published")),
            ParseDate(Value(entry, "updated")),
            categories);
    }

    private static XElement? Child(XElement parent, string localName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return parent.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
    }

    private static string Value(XElement parent, string localName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Child(parent, localName)?.Value ?? string.Empty;
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool TryResolveHttpsUri(Uri sourceUri, string? value, out Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Uri.TryCreate(sourceUri, value.Trim(), out var resolved) ||
            !resolved.IsAbsoluteUri ||
            !string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        uri = resolved;
        return true;
    }

    private static string CleanText(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decoded = WebUtility.HtmlDecode(value);
        var withoutTags = Regex.Replace(decoded, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        var normalized = Regex.Replace(withoutTags, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        normalized = Regex.Replace(normalized, @"\s+([.,;:!?])", "$1", RegexOptions.CultureInvariant);
        return normalized.Length <= MaxTextLength ? normalized : normalized[..MaxTextLength];
    }
}

public sealed class SyndicationFeedTransport
{
    private static readonly string[] AllowedMediaTypes =
    [
        "application/rss+xml",
        "application/atom+xml",
        "application/xml",
        "text/xml"
    ];

    private readonly HttpClient client;
    private readonly int maxResponseBytes;
    private readonly string userAgent;

    public SyndicationFeedTransport(
        HttpClient client,
        int maxResponseBytes = 4 * 1024 * 1024,
        string userAgent = "MHW-Manual-Mod-Manager")
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);
        if (maxResponseBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes), "Response bound must be between 1 KiB and 64 MiB.");
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Contains('\r') || userAgent.Contains('\n'))
            throw new ArgumentException("User-Agent is invalid.", nameof(userAgent));

        this.client = client;
        this.maxResponseBytes = maxResponseBytes;
        this.userAgent = userAgent.Trim();
    }

    public async Task<SyndicationFeedTransportResult> GetAsync(
        Uri feedUri,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var response = await GetConditionalAsync(
            feedUri,
            etag: null,
            ifModifiedSince: null,
            ct).ConfigureAwait(false);

        if (response.NotModified || response.Feed is null)
            throw new InvalidDataException(
                "Syndication feed returned HTTP 304 without a conditional request.");

        return new SyndicationFeedTransportResult(
            response.Feed,
            response.ETag,
            response.LastModified);
    }

    public async Task<SyndicationFeedConditionalResult> GetConditionalAsync(
        Uri feedUri,
        string? etag,
        DateTimeOffset? ifModifiedSince,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateFeedUri(feedUri);

        EntityTagHeaderValue? parsedETag = null;
        if (!string.IsNullOrWhiteSpace(etag))
        {
            var trimmed = etag.Trim();
            if (trimmed == "*" || !EntityTagHeaderValue.TryParse(trimmed, out parsedETag))
                throw new ArgumentException(
                    "Syndication feed ETag validator is malformed.",
                    nameof(etag));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, feedUri);
        request.Headers.UserAgent.ParseAdd(userAgent);
        foreach (var mediaType in AllowedMediaTypes)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));

        if (parsedETag is not null)
            request.Headers.IfNoneMatch.Add(parsedETag);
        if (ifModifiedSince is not null)
            request.Headers.IfModifiedSince = ifModifiedSince.Value.ToUniversalTime();

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        var responseETag = response.Headers.ETag?.ToString()
            ?? parsedETag?.ToString();
        var responseLastModified = response.Content.Headers.LastModified
            ?? ifModifiedSince;

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (parsedETag is null && ifModifiedSince is null)
                throw new InvalidDataException(
                    "Syndication feed returned HTTP 304 without a cache validator.");

            return new SyndicationFeedConditionalResult(
                Feed: null,
                responseETag,
                responseLastModified,
                NotModified: true);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SyndicationFeedTransportException(
                response.StatusCode,
                ResolveRetryAfter(response),
                "Syndication feed provider rate limited the request.");

        if (!response.IsSuccessStatusCode)
            throw new SyndicationFeedTransportException(
                response.StatusCode,
                ResolveRetryAfter(response),
                $"Syndication feed request failed with HTTP {(int)response.StatusCode}.");

        ValidateContentType(response.Content.Headers.ContentType?.MediaType);

        if (response.Content.Headers.ContentLength is > 0 &&
            response.Content.Headers.ContentLength > maxResponseBytes)
            throw new InvalidDataException(
                "Syndication feed response exceeds the configured size limit.");

        var payload = await ReadBoundedAsync(response.Content, ct).ConfigureAwait(false);
        var feed = SyndicationFeedParser.Parse(payload, feedUri);
        return new SyndicationFeedConditionalResult(
            feed,
            responseETag,
            responseLastModified,
            NotModified: false);
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var input = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            if (output.Length + read > maxResponseBytes)
                throw new InvalidDataException("Syndication feed response exceeds the configured size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static void ValidateFeedUri(Uri feedUri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(feedUri);
        if (!feedUri.IsAbsoluteUri ||
            !string.Equals(feedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(feedUri.Host))
            throw new ArgumentException("Syndication feed URI must be absolute HTTPS.", nameof(feedUri));
        if (!string.IsNullOrEmpty(feedUri.UserInfo))
            throw new ArgumentException("Syndication feed URI must not contain embedded credentials.", nameof(feedUri));
    }

    private static void ValidateContentType(string? mediaType)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(mediaType) ||
            !AllowedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Syndication feed returned an unsupported content type.");
    }

    private static DateTimeOffset? ResolveRetryAfter(HttpResponseMessage response)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (response.Headers.RetryAfter?.Date is { } date)
            return date;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return DateTimeOffset.UtcNow.Add(delta);
        return null;
    }
}
