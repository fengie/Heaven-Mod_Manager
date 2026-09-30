using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MhwModManager.Core;

public sealed record ModDbFeedSource(
    string GameId,
    string GameDisplayName,
    Uri FeedUri,
    string FeedLabel,
    IReadOnlyList<string>? Tags = null);

public sealed class ModDbFeedCatalogProvider : IModCatalogProvider
{
    private readonly SyndicationFeedTransport transport;
    private readonly ModDbFeedSource[] sources;

    public ModDbFeedCatalogProvider(
        SyndicationFeedTransport transport,
        IEnumerable<ModDbFeedSource> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sources);

        this.transport = transport;
        this.sources = sources.Select(ValidateSource).ToArray();
        if (this.sources.Length == 0)
            throw new ArgumentException("At least one Mod DB RSS feed source is required.", nameof(sources));

        var duplicates = this.sources
            .GroupBy(source => $"{source.GameId}|{source.FeedUri.AbsoluteUri}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException("Duplicate Mod DB feed sources are not allowed.", nameof(sources));
    }

    public string ProviderId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ModDbFeedCatalogPolicy.ProviderId;
        }
    }

    public string DisplayName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return "Mod DB RSS";
        }
    }

    public CatalogProviderCapabilities Capabilities
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogProviderCapabilities.Browse
                | CatalogProviderCapabilities.Search
                | CatalogProviderCapabilities.Metadata
                | CatalogProviderCapabilities.BrowserAssistedDownload
                | CatalogProviderCapabilities.Updates;
        }
    }

    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ModDbFeedCatalogPolicy.Compliance;
        }
    }

    public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<CatalogGame> games = sources
            .GroupBy(source => source.GameId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CatalogGame(group.Key, group.First().GameDisplayName))
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(games);
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        var limit = Math.Clamp(request.Limit, 1, 100);

        var results = new List<CatalogMod>();
        foreach (var source in SourcesForGame(request.Game.Id))
        {
            ct.ThrowIfCancellationRequested();
            var response = await transport.GetAsync(source.FeedUri, ct).ConfigureAwait(false);
            foreach (var entry in response.Feed.Entries)
            {
                var mod = ToCatalogMod(source, entry);
                if (MatchesQuery(mod, source, request.Query))
                    results.Add(mod);
            }
        }

        return results
            .OrderByDescending(mod => mod.UpdatedAt ?? mod.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(limit)
            .ToArray();
    }

    public async Task<CatalogMod?> GetModAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(providerModId))
            return null;

        foreach (var source in SourcesForGame(game.Id))
        {
            ct.ThrowIfCancellationRequested();
            var response = await transport.GetAsync(source.FeedUri, ct).ConfigureAwait(false);
            var entry = response.Feed.Entries.FirstOrDefault(candidate =>
                string.Equals(BuildProviderModId(source, candidate), providerModId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                return ToCatalogMod(source, entry);
        }

        return null;
    }

    public async Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mod = await GetModAsync(game, providerModId, ct).ConfigureAwait(false);
        return mod?.Files ?? Array.Empty<CatalogModFile>();
    }

    public async Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
        CatalogAcquisitionRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Mod.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.File.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Mod.ProviderModId, request.File.ProviderModId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Mod.GameId, request.Game.Id, StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable("The selected feed item does not belong to this Mod DB provider/game.");
        }

        foreach (var source in SourcesForGame(request.Game.Id))
        {
            ct.ThrowIfCancellationRequested();
            var response = await transport.GetAsync(source.FeedUri, ct).ConfigureAwait(false);
            var entry = response.Feed.Entries.FirstOrDefault(candidate =>
                string.Equals(BuildProviderModId(source, candidate), request.Mod.ProviderModId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;

            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Assisted,
                null,
                entry.Link,
                null,
                "Open the current Mod DB item page to follow the provider-authorized download flow.");
        }

        return Unavailable("The Mod DB feed entry is no longer present in the configured feed.");
    }

    public async Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            await transport.GetAsync(sources[0].FeedUri, ct).ConfigureAwait(false);
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Connected,
                "Mod DB RSS feed is reachable.",
                null,
                DateTimeOffset.UtcNow);
        }
        catch (SyndicationFeedTransportException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.RateLimited,
                "Mod DB RSS feed is rate limited.",
                new CatalogRateLimit(null, null, null, null, ex.RetryAfter, DateTimeOffset.UtcNow),
                DateTimeOffset.UtcNow);
        }
        catch (SyndicationFeedTransportException ex) when (
            ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Limited,
                "Mod DB RSS feed access was refused.",
                null,
                DateTimeOffset.UtcNow);
        }
        catch (InvalidDataException)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Limited,
                "Mod DB RSS feed format was not accepted.",
                null,
                DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Offline,
                "Mod DB RSS feed is unavailable.",
                null,
                DateTimeOffset.UtcNow);
        }
    }

    private IEnumerable<ModDbFeedSource> SourcesForGame(string gameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return sources.Where(source =>
            string.Equals(source.GameId, gameId, StringComparison.OrdinalIgnoreCase));
    }

    private static CatalogMod ToCatalogMod(ModDbFeedSource source, SyndicationFeedEntry entry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var providerModId = BuildProviderModId(source, entry);
        var tags = (source.Tags ?? Array.Empty<string>())
            .Concat(entry.Categories)
            .Append("moddb")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var syntheticFile = new CatalogModFile(
            ModDbFeedCatalogPolicy.ProviderId,
            providerModId,
            providerModId,
            entry.Title,
            entry.Title,
            CatalogFileCategory.Unknown,
            null,
            null,
            "Feed-discovered item; acquisition remains browser-assisted.",
            entry.UpdatedAt ?? entry.PublishedAt,
            false,
            false,
            Array.Empty<CatalogDependency>(),
            null);

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ModDbFeedCatalogPolicy.ProviderId, providerModId),
            ModDbFeedCatalogPolicy.ProviderId,
            providerModId,
            source.GameId,
            entry.Title,
            entry.Summary,
            entry.Summary,
            string.IsNullOrWhiteSpace(entry.Author) ? "Mod DB" : entry.Author,
            null,
            source.FeedLabel,
            tags,
            null,
            Array.Empty<CatalogImage>(),
            entry.PublishedAt,
            entry.UpdatedAt ?? entry.PublishedAt,
            null,
            null,
            null,
            Array.Empty<CatalogDependency>(),
            entry.Link.AbsoluteUri,
            [syntheticFile],
            null);
    }

    private static bool MatchesQuery(CatalogMod mod, ModDbFeedSource source, string? query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var needle = query.Trim();
        return mod.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Author.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || source.FeedLabel.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Tags.Any(tag => tag.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildProviderModId(ModDbFeedSource source, SyndicationFeedEntry entry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var seed = $"{source.FeedUri.AbsoluteUri}\n{entry.Id}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    }

    private static ModDbFeedSource ValidateSource(ModDbFeedSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.FeedLabel);
        ArgumentNullException.ThrowIfNull(source.FeedUri);

        if (!source.FeedUri.IsAbsoluteUri
            || !string.Equals(source.FeedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.FeedUri.Host, "rss.moddb.com", StringComparison.OrdinalIgnoreCase)
            || !source.FeedUri.IsDefaultPort
            || !string.IsNullOrEmpty(source.FeedUri.UserInfo)
            || !string.IsNullOrEmpty(source.FeedUri.Query)
            || !string.IsNullOrEmpty(source.FeedUri.Fragment))
        {
            throw new ArgumentException(
                "Mod DB feed sources must use credential-free HTTPS URLs on rss.moddb.com without custom ports, query strings, or fragments.",
                nameof(source));
        }

        var segments = source.FeedUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isGameScopedFeed = segments.Length == 5
            && string.Equals(segments[0], "games", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(segments[1])
            && (string.Equals(segments[2], "downloads", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[2], "addons", StringComparison.OrdinalIgnoreCase))
            && string.Equals(segments[3], "feed", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[4], "rss.xml", StringComparison.OrdinalIgnoreCase);

        if (!isGameScopedFeed)
        {
            throw new ArgumentException(
                "Mod DB game catalog sources must use a game-scoped /games/{slug}/downloads|addons/feed/rss.xml feed. Site-wide and mod-scoped feeds cannot be assigned to a game.",
                nameof(source));
        }

        return source with
        {
            GameId = source.GameId.Trim(),
            GameDisplayName = source.GameDisplayName.Trim(),
            FeedLabel = source.FeedLabel.Trim(),
            Tags = source.Tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static CatalogAcquisitionResolution Unavailable(string message)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new CatalogAcquisitionResolution(
            CatalogAcquisitionKind.Unavailable,
            null,
            null,
            null,
            message);
    }
}
