using System.Globalization;
using System.Net;

namespace MhwModManager.Core;

public sealed record GitHubReleaseCatalogSource(
    string GameId,
    string GameDisplayName,
    string Owner,
    string Repository,
    string ModName,
    IReadOnlyList<string>? Tags = null)
{
    public string ProviderModId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"{Owner.Trim().ToLowerInvariant()}/{Repository.Trim().ToLowerInvariant()}";
        }
    }
}

public sealed class GitHubReleasesCatalogProvider : IModCatalogProvider
{
    private readonly GitHubReleasesTransport transport;
    private readonly GitHubReleaseCatalogSource[] sources;

    public GitHubReleasesCatalogProvider(
        GitHubReleasesTransport transport,
        IEnumerable<GitHubReleaseCatalogSource> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sources);

        this.transport = transport;
        this.sources = sources.Select(ValidateSource).ToArray();
        if (this.sources.Length == 0)
            throw new ArgumentException("At least one curated GitHub release source is required.", nameof(sources));

        var duplicates = this.sources
            .GroupBy(source => source.ProviderModId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException(
                $"Curated GitHub release sources contain duplicate repositories: {string.Join(", ", duplicates)}.",
                nameof(sources));
    }

    public string ProviderId => GitHubReleasesCatalogPolicy.ProviderId;
    public string DisplayName => "GitHub Releases";
    public CatalogProviderCapabilities Capabilities =>
        CatalogProviderCapabilities.Browse
        | CatalogProviderCapabilities.Search
        | CatalogProviderCapabilities.Metadata
        | CatalogProviderCapabilities.FileList
        | CatalogProviderCapabilities.DirectDownload
        | CatalogProviderCapabilities.Updates;
    public CatalogProviderCompliance Compliance => GitHubReleasesCatalogPolicy.Compliance;

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
        var matchingSources = sources
            .Where(source => string.Equals(source.GameId, request.Game.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var results = new List<CatalogMod>(Math.Min(limit, matchingSources.Length));
        foreach (var source in matchingSources)
        {
            ct.ThrowIfCancellationRequested();
            var result = await transport.GetLatestReleaseAsync(source.Owner, source.Repository, ct).ConfigureAwait(false);
            if (result.Release is not null)
            {
                var mod = ToCatalogMod(source, result.Release);
                if (MatchesQuery(mod, source, request.Query))
                    results.Add(mod);
            }

            if (result.RateLimit?.HourlyRemaining == 0)
                break;
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
        var source = FindSource(game.Id, providerModId);
        if (source is null) return null;
        var result = await transport.GetLatestReleaseAsync(source.Owner, source.Repository, ct).ConfigureAwait(false);
        return result.Release is null ? null : ToCatalogMod(source, result.Release);
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
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Unavailable,
                null,
                null,
                null,
                "The selected catalog file does not belong to this GitHub Releases provider item.");
        }

        var source = FindSource(request.Game.Id, request.Mod.ProviderModId);
        if (source is null)
        {
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Unavailable,
                null,
                null,
                null,
                "The GitHub repository is not present in the curated catalog allowlist.");
        }

        var result = await transport.GetLatestReleaseAsync(source.Owner, source.Repository, ct).ConfigureAwait(false);
        var asset = result.Release?.Assets.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id.ToString(CultureInfo.InvariantCulture),
                request.File.ProviderFileId,
                StringComparison.Ordinal));

        if (asset is null)
        {
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Unavailable,
                null,
                result.Release?.HtmlUri,
                null,
                "The release asset is no longer present in the latest curated GitHub release.");
        }

        return new CatalogAcquisitionResolution(
            CatalogAcquisitionKind.Direct,
            asset.BrowserDownloadUri,
            result.Release?.HtmlUri,
            null,
            "GitHub release asset resolved from the current official API response.");
    }

    public async Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var rate = await transport.GetRateLimitAsync(ct).ConfigureAwait(false);
            var state = rate.HourlyRemaining == 0
                ? CatalogProviderState.RateLimited
                : CatalogProviderState.Connected;
            var message = state == CatalogProviderState.RateLimited
                ? "GitHub REST API rate limit is exhausted."
                : "GitHub REST API is reachable.";

            return new CatalogProviderHealth(
                ProviderId,
                state,
                message,
                rate,
                DateTimeOffset.UtcNow);
        }
        catch (GitHubReleaseTransportException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.AuthenticationRequired,
                "GitHub rejected the configured credential.",
                ex.RateLimit,
                DateTimeOffset.UtcNow);
        }
        catch (GitHubReleaseTransportException ex) when (
            ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.RateLimited,
                "GitHub REST API is rate limited.",
                ex.RateLimit,
                DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Offline,
                "GitHub REST API is unavailable.",
                null,
                DateTimeOffset.UtcNow);
        }
    }

    private GitHubReleaseCatalogSource? FindSource(string gameId, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(providerModId))
            return null;

        return sources.FirstOrDefault(source =>
            string.Equals(source.GameId, gameId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(source.ProviderModId, providerModId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static CatalogMod ToCatalogMod(
        GitHubReleaseCatalogSource source,
        GitHubReleaseSnapshot release)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var providerModId = source.ProviderModId;
        var files = release.Assets
            .Select(asset => new CatalogModFile(
                GitHubReleasesCatalogPolicy.ProviderId,
                providerModId,
                asset.Id.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(asset.Label) ? asset.Name : asset.Label!,
                asset.Name,
                CatalogFileCategory.Miscellaneous,
                release.TagName,
                asset.SizeBytes,
                asset.ContentType,
                asset.CreatedAt,
                false,
                false,
                Array.Empty<CatalogDependency>(),
                ProviderMetadata: null,
                ContentDigest: asset.Digest))
            .ToArray();

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(GitHubReleasesCatalogPolicy.ProviderId, providerModId),
            GitHubReleasesCatalogPolicy.ProviderId,
            providerModId,
            source.GameId,
            source.ModName,
            release.Name,
            release.Body,
            release.Author,
            release.TagName,
            "GitHub Release",
            (source.Tags ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            null,
            Array.Empty<CatalogImage>(),
            release.CreatedAt,
            release.PublishedAt ?? release.CreatedAt,
            release.Assets.Sum(asset => asset.DownloadCount),
            null,
            null,
            Array.Empty<CatalogDependency>(),
            release.HtmlUri.AbsoluteUri,
            files,
            null);
    }

    private static bool MatchesQuery(
        CatalogMod mod,
        GitHubReleaseCatalogSource source,
        string? query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = query.Trim();

        return mod.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Author.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || source.ProviderModId.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Tags.Any(tag => tag.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static GitHubReleaseCatalogSource ValidateSource(GitHubReleaseCatalogSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.ModName);
        ValidateRepositorySegment(source.Owner, nameof(source.Owner));
        ValidateRepositorySegment(source.Repository, nameof(source.Repository));

        return source with
        {
            GameId = source.GameId.Trim(),
            GameDisplayName = source.GameDisplayName.Trim(),
            Owner = source.Owner.Trim(),
            Repository = source.Repository.Trim(),
            ModName = source.ModName.Trim(),
            Tags = source.Tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void ValidateRepositorySegment(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
            throw new ArgumentException("GitHub owner/repository segment must be 1..100 characters.", parameterName);
        if (value.Contains('/') || value.Contains('\\') || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("GitHub owner/repository segment contains invalid characters.", parameterName);
    }
}
