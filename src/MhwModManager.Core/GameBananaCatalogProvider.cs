using System.Net;

namespace MhwModManager.Core;

public sealed class GameBananaCatalogProvider :
    IModCatalogProvider,
    IInstalledCatalogOriginSnapshotProvider
{
    private const int MaxBrowseLimit = 100;
    private const int MaxBrowsePages = 10;

    private readonly GameBananaTransport transport;
    private CatalogProviderHealth health = new(
        GameBananaCatalogNormalizer.ProviderId,
        CatalogProviderState.Limited,
        "GameBanana has not been contacted yet.",
        CheckedAt: DateTimeOffset.UtcNow);

    public GameBananaCatalogProvider(GameBananaTransport transport)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
    }

    public string ProviderId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return GameBananaCatalogNormalizer.ProviderId;
        }
    }

    public string DisplayName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return "GameBanana";
        }
    }

    public CatalogProviderCapabilities Capabilities
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogProviderCapabilities.Browse |
                CatalogProviderCapabilities.Metadata |
                CatalogProviderCapabilities.Images |
                CatalogProviderCapabilities.FileList |
                CatalogProviderCapabilities.FileVariants |
                CatalogProviderCapabilities.BrowserAssistedDownload |
                CatalogProviderCapabilities.Updates;
        }
    }

    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return GameBananaCatalogPolicy.Compliance;
        }
    }

    public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CatalogGame>>(
        [
            new(
                "monster-hunter-world",
                "Monster Hunter: World",
                "9081")
        ]);
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrWhiteSpace(request.Query))
            throw new NotSupportedException("GameBanana full-text catalog search is not implemented by this adapter.");

        var includeUpdated = request.Mode switch
        {
            CatalogBrowseMode.Latest => false,
            CatalogBrowseMode.RecentlyUpdated => true,
            _ => throw new NotSupportedException(
                $"GameBanana browse mode '{request.Mode}' is not implemented by this adapter.")
        };

        var gameId = RequireGameId(request.Game);
        var limit = Math.Clamp(request.Limit, 1, MaxBrowseLimit);

        try
        {
            var result = new List<CatalogMod>(limit);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var page = 1; page <= MaxBrowsePages && result.Count < limit; page++)
            {
                using var listResponse = await transport
                    .GetNewModsAsync(gameId, page, includeUpdated, ct)
                    .ConfigureAwait(false);
                var ids = GameBananaCatalogNormalizer.NormalizeNewModIds(listResponse.Document);
                if (ids.Count == 0) break;

                foreach (var providerModId in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seen.Add(providerModId)) continue;

                    using var detailResponse = await transport
                        .GetModDataAsync(providerModId, ct)
                        .ConfigureAwait(false);
                    result.Add(GameBananaCatalogNormalizer.NormalizeMod(
                        request.Game,
                        providerModId,
                        detailResponse.Document));

                    if (result.Count >= limit) break;
                }
            }

            MarkConnected();
            return result;
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
        _ = RequireGameId(game);

        try
        {
            using var response = await transport.GetModDataAsync(providerModId, ct).ConfigureAwait(false);
            var mod = GameBananaCatalogNormalizer.NormalizeMod(game, providerModId, response.Document);
            MarkConnected();
            return mod;
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
        var mod = await GetModAsync(game, providerModId, ct).ConfigureAwait(false);
        return mod?.Files ?? Array.Empty<CatalogModFile>();
    }

    public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
        CatalogAcquisitionRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        _ = RequireGameId(request.Game);

        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "GameBanana acquisition requires GameBanana mod and file identities.",
                nameof(request));
        }

        if (!request.Mod.GameId.Equals(request.Game.Id, StringComparison.Ordinal)
            || !request.File.ProviderModId.Equals(request.Mod.ProviderModId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "GameBanana acquisition identities do not belong to the selected game/mod.",
                nameof(request));
        }

        if (!long.TryParse(
            request.File.ProviderFileId,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var fileId) || fileId <= 0)
        {
            throw new ArgumentException("GameBanana file id must be a positive integer.", nameof(request));
        }

        if (!long.TryParse(
            request.Mod.ProviderModId,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var modId) || modId <= 0)
        {
            throw new ArgumentException("GameBanana mod id must be a positive integer.", nameof(request));
        }

        var assisted = new Uri(
            $"https://gamebanana.com/mods/download/{modId.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        return Task.FromResult(new CatalogAcquisitionResolution(
            CatalogAcquisitionKind.Assisted,
            DownloadUri: null,
            AssistedUri: assisted,
            ExpiresAt: null,
            Message: "Open the GameBanana download page and use the provider-authorized download flow."));
    }

    public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(health);
    }

    private static int RequireGameId(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        if (game.GameBananaGameId is not > 0)
            throw new InvalidOperationException($"{game.DisplayName} does not have a GameBanana game ID configured.");
        return game.GameBananaGameId.Value;
    }

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to the GameBanana Core API.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return;

        if (ex is GameBananaTransportException transportException)
        {
            if (transportException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "GameBanana API rate limit reached.",
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

            health = new(
                ProviderId,
                CatalogProviderState.Offline,
                "GameBanana API request failed.",
                CheckedAt: DateTimeOffset.UtcNow);
            return;
        }

        health = ex switch
        {
            InvalidDataException => new(
                ProviderId,
                CatalogProviderState.Limited,
                "GameBanana API schema drift or identity mismatch was detected; normalization failed closed.",
                CheckedAt: DateTimeOffset.UtcNow),

            TaskCanceledException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "GameBanana request timed out.",
                CheckedAt: DateTimeOffset.UtcNow),

            HttpRequestException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "GameBanana is temporarily unreachable.",
                CheckedAt: DateTimeOffset.UtcNow),

            _ => health
        };
    }
}
