using System.Net;

namespace MhwModManager.Core;

public sealed record ModIoCatalogGameSource(
    string GameId,
    string GameDisplayName,
    int ProviderGameId);

public sealed class ModIoCatalogProvider : IModCatalogProvider
{
    private readonly ModIoTransport transport;
    private readonly ModIoCatalogGameSource[] sources;
    private CatalogProviderHealth health = new(
        ModIoCatalogPolicy.ProviderId,
        CatalogProviderState.Limited,
        "mod.io has not been contacted yet.",
        CheckedAt: DateTimeOffset.UtcNow);

    public ModIoCatalogProvider(
        ModIoTransport transport,
        IEnumerable<ModIoCatalogGameSource> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sources);

        this.transport = transport;
        this.sources = sources.Select(ValidateSource).ToArray();
        if (this.sources.Length == 0)
            throw new ArgumentException("At least one mod.io game mapping is required.", nameof(sources));

        var duplicates = this.sources
            .GroupBy(source => source.GameId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException(
                $"mod.io game mappings contain duplicate local game ids: {string.Join(", ", duplicates)}.",
                nameof(sources));
    }

    public string ProviderId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ModIoCatalogPolicy.ProviderId;
        }
    }

    public string DisplayName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return "mod.io";
        }
    }

    public CatalogProviderCapabilities Capabilities
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogProviderCapabilities.Search |
                CatalogProviderCapabilities.Browse |
                CatalogProviderCapabilities.Metadata |
                CatalogProviderCapabilities.Images |
                CatalogProviderCapabilities.FileList |
                CatalogProviderCapabilities.FileVariants |
                CatalogProviderCapabilities.DirectDownload |
                CatalogProviderCapabilities.Updates |
                CatalogProviderCapabilities.Ratings;
        }
    }

    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ModIoCatalogPolicy.Compliance;
        }
    }

    public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<CatalogGame> games = sources
            .Select(source => new CatalogGame(
                source.GameId,
                source.GameDisplayName,
                source.ProviderGameId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
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
        var source = RequireSource(request.Game);
        var limit = Math.Clamp(request.Limit, 1, 100);
        var sort = request.Mode switch
        {
            CatalogBrowseMode.Trending => "-downloads_today",
            CatalogBrowseMode.RecentlyUpdated => "-date_updated",
            CatalogBrowseMode.Latest => "-date_live",
            CatalogBrowseMode.Popular => "-downloads_total",
            _ => throw new NotSupportedException($"Unsupported mod.io browse mode '{request.Mode}'.")
        };

        try
        {
            using var response = await transport.GetModsAsync(
                source.ProviderGameId,
                request.Query,
                sort,
                0,
                limit,
                ct).ConfigureAwait(false);
            var mods = ModIoCatalogNormalizer.NormalizeMods(
                request.Game,
                source.ProviderGameId,
                response.Document);
            MarkConnected();
            return mods.Take(limit).ToArray();
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public async Task<CatalogMod?> GetModAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        var source = RequireSource(game);

        try
        {
            using var response = await transport.GetModAsync(
                source.ProviderGameId,
                providerModId,
                ct).ConfigureAwait(false);
            var mod = ModIoCatalogNormalizer.NormalizeMod(
                game,
                source.ProviderGameId,
                providerModId,
                response.Document);
            MarkConnected();
            return mod;
        }
        catch (ModIoTransportException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            MarkConnected();
            return null;
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        var source = RequireSource(game);

        try
        {
            using var response = await transport.GetModFilesAsync(
                source.ProviderGameId,
                providerModId,
                ct: ct).ConfigureAwait(false);
            var files = ModIoCatalogNormalizer.NormalizeModFiles(providerModId, response.Document);
            MarkConnected();
            return files;
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public async Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
        CatalogAcquisitionRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        var source = RequireSource(request.Game);

        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.Mod.GameId.Equals(request.Game.Id, StringComparison.Ordinal)
            || !request.File.ProviderModId.Equals(request.Mod.ProviderModId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "mod.io acquisition identities do not belong to the selected provider/game/mod.",
                nameof(request));
        }

        try
        {
            using var response = await transport.GetModFileAsync(
                source.ProviderGameId,
                request.Mod.ProviderModId,
                request.File.ProviderFileId,
                ct).ConfigureAwait(false);
            var acquisition = ModIoCatalogNormalizer.NormalizeAcquisitionFile(
                request.Mod.ProviderModId,
                request.File.ProviderFileId,
                response.Document);

            MarkConnected();

            if (acquisition.DownloadUri is null)
            {
                return new CatalogAcquisitionResolution(
                    CatalogAcquisitionKind.Unavailable,
                    DownloadUri: null,
                    AssistedUri: null,
                    ExpiresAt: null,
                    Message: acquisition.UnavailableReason ?? "mod.io did not provide an approved download.");
            }

            if (acquisition.ExpiresAt is not null && acquisition.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return new CatalogAcquisitionResolution(
                    CatalogAcquisitionKind.Unavailable,
                    DownloadUri: null,
                    AssistedUri: null,
                    ExpiresAt: acquisition.ExpiresAt,
                    Message: "mod.io returned an already-expired download URL; refresh before downloading.");
            }

            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Direct,
                acquisition.DownloadUri,
                AssistedUri: null,
                ExpiresAt: acquisition.ExpiresAt,
                Message: "Use the fresh provider-authorized mod.io download URL.");
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(health);
    }

    private ModIoCatalogGameSource RequireSource(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        return sources.FirstOrDefault(source =>
                   source.GameId.Equals(game.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"{game.DisplayName} does not have a mod.io game mapping configured.");
    }

    private static ModIoCatalogGameSource ValidateSource(ModIoCatalogGameSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);
        if (!GameProfile.IsCanonicalId(source.GameId))
            throw new ArgumentException("mod.io local game ids must be canonical game ids.", nameof(source));
        if (string.IsNullOrWhiteSpace(source.GameDisplayName))
            throw new ArgumentException("mod.io game display name is required.", nameof(source));
        if (source.ProviderGameId <= 0)
            throw new ArgumentException("mod.io provider game id must be positive.", nameof(source));

        return source with
        {
            GameId = source.GameId.Trim(),
            GameDisplayName = source.GameDisplayName.Trim()
        };
    }

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to the mod.io REST API.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return;

        if (ex is ModIoTransportException transportException)
        {
            if (transportException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "mod.io API rate limit reached.",
                    new CatalogRateLimit(
                        null,
                        null,
                        null,
                        null,
                        transportException.RetryAfter is null
                            ? null
                            : DateTimeOffset.UtcNow.Add(transportException.RetryAfter.Value),
                        DateTimeOffset.UtcNow),
                    DateTimeOffset.UtcNow);
                return;
            }

            if (transportException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.AuthenticationRequired,
                    "mod.io rejected the configured API credentials.",
                    CheckedAt: DateTimeOffset.UtcNow);
                return;
            }

            health = new(
                ProviderId,
                CatalogProviderState.Offline,
                "mod.io API request failed.",
                CheckedAt: DateTimeOffset.UtcNow);
            return;
        }

        health = ex switch
        {
            InvalidDataException => new(
                ProviderId,
                CatalogProviderState.Limited,
                "mod.io API schema drift or identity mismatch was detected; normalization failed closed.",
                CheckedAt: DateTimeOffset.UtcNow),

            TaskCanceledException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "mod.io request timed out.",
                CheckedAt: DateTimeOffset.UtcNow),

            HttpRequestException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "mod.io is temporarily unreachable.",
                CheckedAt: DateTimeOffset.UtcNow),

            _ => health
        };
    }
}
