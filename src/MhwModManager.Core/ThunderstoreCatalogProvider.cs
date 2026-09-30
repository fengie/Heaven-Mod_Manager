using System.Net;

namespace MhwModManager.Core;

public sealed record ThunderstoreCatalogGameSource(
    string GameId,
    string GameDisplayName,
    string CommunityIdentifier);

public sealed class ThunderstoreCatalogProvider : IModCatalogProvider
{
    private readonly ThunderstoreTransport transport;
    private readonly ThunderstoreCatalogGameSource[] sources;
    private CatalogProviderHealth health;

    public ThunderstoreCatalogProvider(
        ThunderstoreTransport transport,
        IEnumerable<ThunderstoreCatalogGameSource> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sources);

        this.transport = transport;
        this.sources = sources.Select(ValidateSource).ToArray();
        if (this.sources.Length == 0)
            throw new ArgumentException("At least one Thunderstore community mapping is required.", nameof(sources));

        var duplicates = this.sources
            .GroupBy(source => source.GameId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new ArgumentException(
                $"Thunderstore mappings contain duplicate local game ids: {string.Join(", ", duplicates)}.",
                nameof(sources));
        }

        health = Compliance.Enabled
            ? new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Limited,
                "Thunderstore has not been contacted yet.",
                CheckedAt: DateTimeOffset.UtcNow)
            : new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Disabled,
                ThunderstoreCatalogPolicy.PendingTermsReason,
                CheckedAt: DateTimeOffset.UtcNow);
    }

    public string ProviderId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ThunderstoreCatalogPolicy.ProviderId;
        }
    }

    public string DisplayName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return "Thunderstore";
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
                CatalogProviderCapabilities.Dependencies |
                CatalogProviderCapabilities.DirectDownload |
                CatalogProviderCapabilities.Updates |
                CatalogProviderCapabilities.Ratings |
                CatalogProviderCapabilities.VersionHistory;
        }
    }

    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return ThunderstoreCatalogPolicy.Compliance;
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
                source.CommunityIdentifier))
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
        EnsureComplianceReady();

        var source = RequireSource(request.Game);
        var limit = Math.Clamp(request.Limit, 1, 100);

        try
        {
            using var response = await transport.GetPackagesAsync(
                source.CommunityIdentifier,
                ct: ct).ConfigureAwait(false);
            var document = response.Document
                ?? throw new InvalidDataException("Thunderstore unexpectedly returned an empty package-list response.");
            var packages = ThunderstoreCatalogNormalizer.NormalizePackages(
                request.Game,
                source.CommunityIdentifier,
                document);

            IEnumerable<CatalogMod> filtered = packages;
            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                var query = request.Query.Trim();
                filtered = filtered.Where(mod =>
                    mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || mod.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || mod.Author.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || mod.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            filtered = request.Mode switch
            {
                CatalogBrowseMode.Trending => filtered
                    .OrderByDescending(mod => mod.Downloads ?? 0)
                    .ThenByDescending(mod => mod.UpdatedAt),
                CatalogBrowseMode.Popular => filtered
                    .OrderByDescending(mod => mod.Downloads ?? 0)
                    .ThenByDescending(mod => mod.Endorsements ?? 0),
                CatalogBrowseMode.RecentlyUpdated => filtered
                    .OrderByDescending(mod => mod.UpdatedAt)
                    .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase),
                CatalogBrowseMode.Latest => filtered
                    .OrderByDescending(mod => mod.CreatedAt)
                    .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase),
                _ => throw new NotSupportedException($"Unsupported Thunderstore browse mode '{request.Mode}'.")
            };

            MarkConnected();
            return filtered.Take(limit).ToArray();
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
        EnsureComplianceReady();
        var source = RequireSource(game, providerModId, out var packageUuid);

        try
        {
            using var response = await transport.GetPackageAsync(
                source.CommunityIdentifier,
                packageUuid,
                ct).ConfigureAwait(false);
            var document = response.Document
                ?? throw new InvalidDataException("Thunderstore unexpectedly returned an empty package-detail response.");
            var mod = ThunderstoreCatalogNormalizer.NormalizePackage(
                game,
                source.CommunityIdentifier,
                providerModId,
                document);
            MarkConnected();
            return mod;
        }
        catch (ThunderstoreTransportException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
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
        EnsureComplianceReady();
        var source = RequireSource(game, providerModId, out var packageUuid);

        try
        {
            using var response = await transport.GetPackageAsync(
                source.CommunityIdentifier,
                packageUuid,
                ct).ConfigureAwait(false);
            var document = response.Document
                ?? throw new InvalidDataException("Thunderstore unexpectedly returned an empty package-detail response.");
            var mod = ThunderstoreCatalogNormalizer.NormalizePackage(
                game,
                source.CommunityIdentifier,
                providerModId,
                document);
            MarkConnected();
            return mod.Files;
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
        EnsureComplianceReady();

        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.Mod.GameId.Equals(request.Game.Id, StringComparison.Ordinal)
            || !request.File.ProviderModId.Equals(request.Mod.ProviderModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Thunderstore acquisition identities do not belong to the selected provider/game/mod.",
                nameof(request));
        }

        var source = RequireSource(
            request.Game,
            request.Mod.ProviderModId,
            out var packageUuid);

        try
        {
            using var response = await transport.GetPackageAsync(
                source.CommunityIdentifier,
                packageUuid,
                ct).ConfigureAwait(false);
            var document = response.Document
                ?? throw new InvalidDataException("Thunderstore unexpectedly returned an empty package-detail response.");
            var acquisition = ThunderstoreCatalogNormalizer.NormalizeAcquisition(
                source.CommunityIdentifier,
                request.Mod.ProviderModId,
                request.File.ProviderFileId,
                document);

            MarkConnected();
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Direct,
                acquisition.DownloadUri,
                AssistedUri: null,
                ExpiresAt: null,
                Message: "Use the current provider-authorized Thunderstore package download URL.");
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

    private void EnsureComplianceReady()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        CatalogProviderComplianceValidator.EnsureUsable(
            Compliance,
            DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private ThunderstoreCatalogGameSource RequireSource(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        return sources.FirstOrDefault(source =>
                   source.GameId.Equals(game.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"{game.DisplayName} does not have a Thunderstore community mapping configured.");
    }

    private ThunderstoreCatalogGameSource RequireSource(
        GameProfile game,
        string providerModId,
        out string packageUuid)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var source = RequireSource(game);
        if (!ThunderstoreCatalogIdentity.TryParseProviderModId(
                providerModId,
                out var community,
                out packageUuid)
            || !community.Equals(source.CommunityIdentifier, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Thunderstore provider identity does not match the selected game/community.",
                nameof(providerModId));
        }

        return source;
    }

    private static ThunderstoreCatalogGameSource ValidateSource(ThunderstoreCatalogGameSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);
        if (!GameProfile.IsCanonicalId(source.GameId))
            throw new ArgumentException("Thunderstore local game ids must be canonical game ids.", nameof(source));
        if (string.IsNullOrWhiteSpace(source.GameDisplayName))
            throw new ArgumentException("Thunderstore game display name is required.", nameof(source));

        return source with
        {
            GameDisplayName = source.GameDisplayName.Trim(),
            CommunityIdentifier = ThunderstoreCatalogIdentity.NormalizeCommunityIdentifier(
                source.CommunityIdentifier,
                nameof(source.CommunityIdentifier))
        };
    }

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to the Thunderstore public package API.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return;

        if (ex is ThunderstoreTransportException transportException)
        {
            if (transportException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "Thunderstore API rate limit reached.",
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
                "Thunderstore API request failed.",
                CheckedAt: DateTimeOffset.UtcNow);
            return;
        }

        health = ex switch
        {
            InvalidDataException => new(
                ProviderId,
                CatalogProviderState.Limited,
                "Thunderstore API schema drift or identity mismatch was detected; normalization failed closed.",
                CheckedAt: DateTimeOffset.UtcNow),

            TaskCanceledException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "Thunderstore request timed out.",
                CheckedAt: DateTimeOffset.UtcNow),

            HttpRequestException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "Thunderstore is temporarily unreachable.",
                CheckedAt: DateTimeOffset.UtcNow),

            _ => health
        };
    }
}
