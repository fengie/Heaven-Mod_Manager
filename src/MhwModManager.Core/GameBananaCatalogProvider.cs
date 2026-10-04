using System.Net;

namespace MhwModManager.Core;

public sealed class GameBananaCatalogProvider :
    IModCatalogProvider,
    IPagedModCatalogProvider,
    IInstalledCatalogOriginSnapshotProvider
{
    private const int MaxBrowseLimit = 100;
    private const int MaxBrowsePagesPerRequest = 10;
    private const int MaxConcurrentDetailRequests = 4;

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
        var page = await BrowsePageAsync(request, cursor: null, ct).ConfigureAwait(false);
        return page.Items;
    }

    public async Task<CatalogBrowsePage> BrowsePageAsync(
        CatalogBrowseRequest request,
        string? cursor = null,
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
        var (page, offset) = ParseBrowseCursor(cursor);

        try
        {
            var result = new List<CatalogMod>(limit);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var fetchedPages = 0;
            string? nextCursor = null;
            var exhausted = false;

            while (fetchedPages < MaxBrowsePagesPerRequest && result.Count < limit)
            {
                ct.ThrowIfCancellationRequested();
                using var listResponse = await transport
                    .GetNewModsAsync(gameId, page, includeUpdated, ct)
                    .ConfigureAwait(false);
                fetchedPages++;

                var ids = GameBananaCatalogNormalizer.NormalizeNewModIds(listResponse.Document);
                if (ids.Count == 0)
                {
                    exhausted = true;
                    break;
                }

                if (offset > ids.Count)
                    throw new InvalidDataException(
                        "The saved GameBanana continuation no longer matches the provider page. Refresh providers to restart browsing.");

                var pageIds = new List<string>(Math.Min(ids.Count - offset, limit - result.Count));
                var index = offset;
                while (index < ids.Count && result.Count + pageIds.Count < limit)
                {
                    var providerModId = ids[index++];
                    if (seen.Add(providerModId))
                        pageIds.Add(providerModId);
                }

                if (pageIds.Count > 0)
                {
                    var hydrated = await HydrateModsAsync(
                        request.Game,
                        pageIds.ToArray(),
                        ct).ConfigureAwait(false);
                    result.AddRange(hydrated);
                }

                if (index < ids.Count)
                {
                    nextCursor = FormatBrowseCursor(page, index);
                    break;
                }

                page++;
                offset = 0;
                if (result.Count >= limit)
                {
                    nextCursor = FormatBrowseCursor(page, 0);
                    break;
                }
            }

            if (!exhausted && nextCursor is null && fetchedPages >= MaxBrowsePagesPerRequest)
                nextCursor = FormatBrowseCursor(page, 0);

            MarkConnected();
            return new CatalogBrowsePage(result, nextCursor);
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    private static (int Page, int Offset) ParseBrowseCursor(string? cursor)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(cursor))
            return (1, 0);

        var parts = cursor.Split(':', StringSplitOptions.None);
        if (parts.Length != 3
            || !string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var page)
            || page <= 0
            || !int.TryParse(
                parts[2],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset)
            || offset < 0)
        {
            throw new ArgumentException(
                "GameBanana browse continuation is invalid.",
                nameof(cursor));
        }

        return (page, offset);
    }

    private static string FormatBrowseCursor(int page, int offset)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"v1:{page}:{offset}");
    }

    private async Task<IReadOnlyList<CatalogMod>> HydrateModsAsync(
        GameProfile game,
        string[] providerModIds,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new List<CatalogMod>(providerModIds.Length);
        foreach (var batch in providerModIds.Chunk(MaxConcurrentDetailRequests))
        {
            ct.ThrowIfCancellationRequested();
            var tasks = batch
                .Select(providerModId => HydrateModAsync(game, providerModId, ct))
                .ToArray();
            var hydrated = await Task.WhenAll(tasks).ConfigureAwait(false);
            result.AddRange(hydrated);
        }

        return result;
    }

    private async Task<CatalogMod> HydrateModAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var detailResponse = await transport
            .GetModDataAsync(providerModId, ct)
            .ConfigureAwait(false);
        return GameBananaCatalogNormalizer.NormalizeMod(
            game,
            providerModId,
            detailResponse.Document);
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

    public async Task<InstalledCatalogOriginSnapshot> GetInstalledOriginSnapshotAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mod = await GetModAsync(game, providerModId, ct).ConfigureAwait(false);
        return new InstalledCatalogOriginSnapshot(
            mod,
            mod?.Files ?? Array.Empty<CatalogModFile>());
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
