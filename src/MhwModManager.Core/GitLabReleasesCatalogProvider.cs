using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record GitLabReleaseCatalogSource(
    string GameId,
    string GameDisplayName,
    string ProjectPath,
    string ModName,
    IReadOnlyList<string>? Tags = null)
{
    public string ProviderModId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ProjectPath.Trim().Trim('/').ToLowerInvariant();
        }
    }
}

public sealed class GitLabReleasesCatalogProvider : IModCatalogProvider
{
    private readonly GitLabReleasesTransport transport;
    private readonly GitLabReleaseCatalogSource[] sources;

    public GitLabReleasesCatalogProvider(
        GitLabReleasesTransport transport,
        IEnumerable<GitLabReleaseCatalogSource> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sources);

        this.transport = transport;
        this.sources = sources.Select(ValidateSource).ToArray();
        if (this.sources.Length == 0)
            throw new ArgumentException("At least one curated GitLab release source is required.", nameof(sources));

        var duplicates = this.sources
            .GroupBy(source => source.ProviderModId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new ArgumentException(
                $"Curated GitLab release sources contain duplicate projects: {string.Join(", ", duplicates)}.",
                nameof(sources));
        }
    }

    public string ProviderId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return GitLabReleasesCatalogPolicy.ProviderId;
        }
    }

    public string DisplayName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return "GitLab Releases";
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
                | CatalogProviderCapabilities.FileList
                | CatalogProviderCapabilities.DirectDownload
                | CatalogProviderCapabilities.BrowserAssistedDownload
                | CatalogProviderCapabilities.Updates;
        }
    }

    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return GitLabReleasesCatalogPolicy.Compliance;
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
        var matchingSources = sources
            .Where(source => string.Equals(
                source.GameId,
                request.Game.Id,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var results = new List<CatalogMod>(Math.Min(limit, matchingSources.Length));
        foreach (var source in matchingSources)
        {
            ct.ThrowIfCancellationRequested();
            var result = await transport
                .GetLatestReleaseAsync(source.ProjectPath, ct)
                .ConfigureAwait(false);
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

        var result = await transport
            .GetLatestReleaseAsync(source.ProjectPath, ct)
            .ConfigureAwait(false);
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
            || !string.Equals(
                request.Mod.ProviderModId,
                request.File.ProviderModId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Mod.GameId, request.Game.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Unavailable,
                null,
                null,
                null,
                "The selected catalog file does not belong to this GitLab Releases provider item.");
        }

        var source = FindSource(request.Game.Id, request.Mod.ProviderModId);
        if (source is null)
        {
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Unavailable,
                null,
                null,
                null,
                "The GitLab project is not present in the curated catalog allowlist.");
        }

        var result = await transport
            .GetLatestReleaseAsync(source.ProjectPath, ct)
            .ConfigureAwait(false);
        var release = result.Release;
        var releasePage = release is null ? null : BuildReleasePageUri(source, release.TagName);
        var asset = release?.Assets.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id.ToString(CultureInfo.InvariantCulture),
                request.File.ProviderFileId,
                StringComparison.Ordinal));

        if (asset is null)
        {
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Unavailable,
                null,
                releasePage,
                null,
                "The release asset is no longer present in the latest curated GitLab release.");
        }

        if (asset.DirectAssetUri is not null)
        {
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Direct,
                asset.DirectAssetUri,
                releasePage,
                null,
                "GitLab release asset resolved from the current official API response.");
        }

        return new CatalogAcquisitionResolution(
            CatalogAcquisitionKind.Assisted,
            null,
            releasePage,
            null,
            "GitLab did not expose a same-host HTTPS direct asset URL; open the current release page to acquire it.");
    }

    public async Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var result = await transport
                .GetLatestReleaseAsync(sources[0].ProjectPath, ct)
                .ConfigureAwait(false);
            var state = result.RateLimit?.HourlyRemaining == 0
                ? CatalogProviderState.RateLimited
                : CatalogProviderState.Connected;
            var message = state == CatalogProviderState.RateLimited
                ? "GitLab REST API rate limit is exhausted."
                : "GitLab Releases API is reachable.";

            return new CatalogProviderHealth(
                ProviderId,
                state,
                message,
                result.RateLimit,
                DateTimeOffset.UtcNow);
        }
        catch (GitLabReleaseTransportException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.AuthenticationRequired,
                "GitLab rejected the configured credential.",
                ex.RateLimit,
                DateTimeOffset.UtcNow);
        }
        catch (GitLabReleaseTransportException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.RateLimited,
                "GitLab REST API is rate limited.",
                ex.RateLimit,
                DateTimeOffset.UtcNow);
        }
        catch (GitLabReleaseTransportException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Limited,
                "GitLab denied access to the curated project.",
                ex.RateLimit,
                DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException)
        {
            return new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Offline,
                "GitLab Releases API is unavailable.",
                null,
                DateTimeOffset.UtcNow);
        }
    }

    private GitLabReleaseCatalogSource? FindSource(string gameId, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(providerModId))
            return null;

        return sources.FirstOrDefault(source =>
            string.Equals(source.GameId, gameId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                source.ProviderModId,
                providerModId.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }

    private CatalogMod ToCatalogMod(
        GitLabReleaseCatalogSource source,
        GitLabReleaseSnapshot release)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var providerModId = source.ProviderModId;
        var files = release.Assets
            .Where(asset =>
                !string.Equals(asset.LinkType, "image", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(asset.LinkType, "runbook", StringComparison.OrdinalIgnoreCase))
            .Select(asset => new CatalogModFile(
                GitLabReleasesCatalogPolicy.ProviderId,
                providerModId,
                asset.Id.ToString(CultureInfo.InvariantCulture),
                asset.Name,
                asset.Name,
                CatalogFileCategory.Miscellaneous,
                release.TagName,
                null,
                $"GitLab release asset ({asset.LinkType}).",
                release.ReleasedAt ?? release.CreatedAt,
                false,
                false,
                Array.Empty<CatalogDependency>(),
                JsonSerializer.Serialize(new
                {
                    linkType = asset.LinkType,
                    direct = asset.DirectAssetUri is not null
                })))
            .ToArray();

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(GitLabReleasesCatalogPolicy.ProviderId, providerModId),
            GitLabReleasesCatalogPolicy.ProviderId,
            providerModId,
            source.GameId,
            source.ModName,
            release.Name,
            release.Description,
            release.Author,
            release.TagName,
            "GitLab Release",
            (source.Tags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            null,
            Array.Empty<CatalogImage>(),
            release.CreatedAt,
            release.ReleasedAt ?? release.CreatedAt,
            null,
            null,
            null,
            Array.Empty<CatalogDependency>(),
            BuildReleasePageUri(source, release.TagName).AbsoluteUri,
            files,
            null);
    }

    private Uri BuildReleasePageUri(GitLabReleaseCatalogSource source, string tagName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var encodedPath = string.Join(
            "/",
            source.ProjectPath
                .Trim()
                .Trim('/')
                .Split('/')
                .Select(Uri.EscapeDataString));
        var encodedTag = Uri.EscapeDataString(tagName);
        return new Uri(transport.WebBaseUri, $"{encodedPath}/-/releases/{encodedTag}");
    }

    private static bool MatchesQuery(
        CatalogMod mod,
        GitLabReleaseCatalogSource source,
        string? query)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = query.Trim();

        return MatchesCatalogText(mod.Name, needle)
            || MatchesCatalogText(mod.Summary, needle)
            || MatchesCatalogText(mod.Description, needle)
            || MatchesCatalogText(mod.Author, needle)
            || MatchesCatalogText(source.ProviderModId, needle)
            || mod.Tags.Any(tag => MatchesCatalogText(tag, needle));
    }

    private static bool MatchesCatalogText(string value, string needle)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return true;

        static string NormalizeSearchSeparators(string text)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            var chars = text
                .Select(character => character is '-' or '_' ? ' ' : character)
                .ToArray();
            return string.Join(
                ' ',
                new string(chars).Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return NormalizeSearchSeparators(value)
            .Contains(NormalizeSearchSeparators(needle), StringComparison.OrdinalIgnoreCase);
    }

    private static GitLabReleaseCatalogSource ValidateSource(GitLabReleaseCatalogSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.ModName);

        var projectPath = NormalizeProjectPath(source.ProjectPath);
        return source with
        {
            GameId = source.GameId.Trim(),
            GameDisplayName = source.GameDisplayName.Trim(),
            ProjectPath = projectPath,
            ModName = source.ModName.Trim(),
            Tags = source.Tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string NormalizeProjectPath(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
            throw new ArgumentException("GitLab project path must be 1..255 characters.", nameof(value));

        var normalized = value.Trim().Trim('/');
        if (normalized.Length == 0
            || normalized.Contains('\\')
            || normalized.Contains('\r')
            || normalized.Contains('\n'))
        {
            throw new ArgumentException("GitLab project path contains invalid characters.", nameof(value));
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment =>
            string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment.Length > 100))
        {
            throw new ArgumentException("GitLab project path contains an invalid segment.", nameof(value));
        }

        return normalized;
    }
}
