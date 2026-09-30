using System.Net;

namespace MhwModManager.Core;

public sealed record ModIoCatalogGameSource(
    string GameId,
    string GameDisplayName,
    int ModIoGameId);

public sealed class ModIoCatalogProvider : IModCatalogProvider
{
    private readonly ModIoTransport transport;
    private readonly ModIoCatalogGameSource[] games;
    private CatalogProviderHealth health = new(
        ModIoCatalogPolicy.ProviderId,
        CatalogProviderState.Limited,
        "mod.io has not been contacted yet.",
        CheckedAt: DateTimeOffset.UtcNow);

    public ModIoCatalogProvider(
        ModIoTransport transport,
        IEnumerable<ModIoCatalogGameSource> games)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(games);
        this.transport = transport;
        this.games = games.Select(ValidateGame).ToArray();
        if (this.games.Length == 0)
            throw new ArgumentException("At least one mod.io game mapping is required.", nameof(games));
        if (this.games.GroupBy(x => x.GameId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
            || this.games.GroupBy(x => x.ModIoGameId).Any(g => g.Count() > 1))
            throw new ArgumentException("mod.io game mappings must be unique.", nameof(games));
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
            return CatalogProviderCapabilities.Search
                | CatalogProviderCapabilities.Browse
                | CatalogProviderCapabilities.Metadata
                | CatalogProviderCapabilities.Images
                | CatalogProviderCapabilities.FileList
                | CatalogProviderCapabilities.FileVariants
                | CatalogProviderCapabilities.DirectDownload
                | CatalogProviderCapabilities.Updates;
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
        IReadOnlyList<CatalogGame> result = games
            .Select(x => new CatalogGame(
                x.GameId,
                x.GameDisplayName,
                x.ModIoGameId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        var source = FindGame(request.Game.Id)
            ?? throw new InvalidOperationException($"{request.Game.DisplayName} has no mod.io mapping.");
        var limit = Math.Clamp(request.Limit, 1, 100);
        var sort = request.Mode switch
        {
            CatalogBrowseMode.Latest => "-date_live",
            CatalogBrowseMode.RecentlyUpdated => "-date_updated",
            CatalogBrowseMode.Popular => "-downloads_total",
            CatalogBrowseMode.Trending => "-downloads_total",
            _ => "-date_updated"
        };

        try
        {
            using var response = await transport.GetModsAsync(
                source.ModIoGameId,
                0,
                limit,
                sort,
                request.Query,
                ct).ConfigureAwait(false);
            var mods = ModIoCatalogNormalizer.NormalizeMods(
                request.Game,
                source.ModIoGameId,
                response.Document);
            MarkConnected();
            return mods;
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
        var source = RequireSource(game, providerModId, out var rawModId);

        try
        {
            using var fileResponse = await transport
                .GetModFilesAsync(source.ModIoGameId, rawModId, ct)
                .ConfigureAwait(false);
            var files = ModIoCatalogNormalizer.NormalizeFiles(providerModId, fileResponse.Document);

            using var modResponse = await transport
                .GetModAsync(source.ModIoGameId, rawModId, ct)
                .ConfigureAwait(false);
            var mod = ModIoCatalogNormalizer.NormalizeMod(
                game,
                source.ModIoGameId,
                modResponse.Document,
                files);
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
        var source = RequireSource(game, providerModId, out var rawModId);

        try
        {
            using var response = await transport
                .GetModFilesAsync(source.ModIoGameId, rawModId, ct)
                .ConfigureAwait(false);
            var files = ModIoCatalogNormalizer.NormalizeFiles(providerModId, response.Document);
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
        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.Mod.GameId.Equals(request.Game.Id, StringComparison.Ordinal)
            || !request.File.ProviderModId.Equals(request.Mod.ProviderModId, StringComparison.Ordinal))
        {
            throw new ArgumentException("mod.io acquisition identities do not belong together.", nameof(request));
        }

        var source = RequireSource(request.Game, request.Mod.ProviderModId, out var rawModId);
        try
        {
            using var response = await transport
                .GetModFileAsync(
                    source.ModIoGameId,
                    rawModId,
                    request.File.ProviderFileId,
                    ct)
                .ConfigureAwait(false);
            var resolved = ModIoCatalogNormalizer.NormalizeDownload(
                rawModId,
                request.File.ProviderFileId,
                response.Document);

            if (resolved.VirusPositive != 0)
            {
                return new CatalogAcquisitionResolution(
                    CatalogAcquisitionKind.Unavailable,
                    null,
                    new Uri(request.Mod.SourceUrl),
                    null,
                    "mod.io reports this file as potentially harmful or containing a threat.");
            }

            if (resolved.VirusStatus != 1)
            {
                return new CatalogAcquisitionResolution(
                    CatalogAcquisitionKind.Unavailable,
                    null,
                    new Uri(request.Mod.SourceUrl),
                    null,
                    "mod.io has not completed a clean virus scan for this file.");
            }

            MarkConnected();
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Direct,
                resolved.DownloadUri,
                new Uri(request.Mod.SourceUrl),
                resolved.ExpiresAt,
                "Resolved a fresh provider-authorized mod.io download URL. The URL is ephemeral and must not be cached.");
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

    private ModIoCatalogGameSource RequireSource(
        GameProfile game,
        string providerModId,
        out string rawModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = FindGame(game.Id)
            ?? throw new InvalidOperationException($"{game.DisplayName} has no mod.io mapping.");
        if (!ModIoCatalogNormalizer.TryParseProviderModId(providerModId, out var providerGameId, out rawModId)
            || providerGameId != source.ModIoGameId)
            throw new ArgumentException("mod.io provider identity does not match the selected game.", nameof(providerModId));
        return source;
    }

    private ModIoCatalogGameSource? FindGame(string gameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return games.FirstOrDefault(x => x.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase));
    }

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to the official mod.io REST API.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return;

        if (ex is ModIoTransportException transportException)
        {
            if (transportException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.AuthenticationRequired,
                    "mod.io rejected the configured API key.",
                    CheckedAt: DateTimeOffset.UtcNow);
                return;
            }

            if (transportException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "mod.io API rate limit reached.",
                    new CatalogRateLimit(
                        null, null, null, null,
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

    private static ModIoCatalogGameSource ValidateGame(ModIoCatalogGameSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameDisplayName);
        if (source.ModIoGameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(source), "mod.io game id must be positive.");
        return source with
        {
            GameId = source.GameId.Trim(),
            GameDisplayName = source.GameDisplayName.Trim()
        };
    }
}
