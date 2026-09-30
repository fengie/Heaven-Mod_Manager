using System.Net;

namespace MhwModManager.Core;

public sealed class NexusV3CatalogProvider : IModCatalogProvider
{
    private readonly NexusV3Transport transport;
    private readonly NexusV3Credential? credential;
    private CatalogProviderHealth health = new(
        "nexus",
        CatalogProviderState.Limited,
        "Nexus public discovery is available; credentials are required for mod details and file variants.",
        CheckedAt: DateTimeOffset.UtcNow);

    public NexusV3CatalogProvider(NexusV3Transport transport, NexusV3Credential? credential = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
        this.credential = credential;
    }

    public string ProviderId => NexusV3CatalogNormalizer.ProviderId;
    public string DisplayName => "Nexus Mods";

    public CatalogProviderCapabilities Capabilities =>
        CatalogProviderCapabilities.Browse |
        CatalogProviderCapabilities.Metadata |
        CatalogProviderCapabilities.Images |
        CatalogProviderCapabilities.FileList |
        CatalogProviderCapabilities.FileVariants |
        CatalogProviderCapabilities.BrowserAssistedDownload |
        CatalogProviderCapabilities.VersionHistory;

    public CatalogProviderCompliance Compliance => NexusV3CatalogPolicy.Compliance;

    public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CatalogGame>>([]);
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"game={request.Game.Id}; query={request.Query ?? "<empty>"}");

        if (!string.IsNullOrWhiteSpace(request.Query))
            throw new NotSupportedException("Nexus API v3 full-catalog search is not implemented yet.");

        if (request.Mode != CatalogBrowseMode.Trending)
            throw new NotSupportedException($"Nexus API v3 browse mode '{request.Mode}' is not implemented yet.");

        var domain = RequireGameDomain(request.Game);
        try
        {
            using var response = await transport
                .GetTrendingModsAsync(domain, ct: ct)
                .ConfigureAwait(false);
            var document = response.Document
                ?? throw new InvalidDataException("Nexus v3 trending response did not contain a JSON document.");

            var mods = NexusV3CatalogNormalizer.NormalizeTrendingMods(
                request.Game.Id,
                domain,
                document);

            MarkConnected(publicOnly: credential is null);
            return mods.Take(Math.Clamp(request.Limit, 1, 500)).ToArray();
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
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        try
        {
            var normalized = await GetModDetailsAsync(game, providerModId, ct).ConfigureAwait(false);
            MarkConnected(publicOnly: false);
            return normalized.Mod;
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
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        try
        {
            var normalized = await GetModDetailsAsync(game, providerModId, ct).ConfigureAwait(false);
            var auth = RequireCredential();

            using var groupsResponse = await transport
                .GetModFilesAsync(normalized.GlobalModId, auth, ct: ct)
                .ConfigureAwait(false);
            var groupsDocument = groupsResponse.Document
                ?? throw new InvalidDataException("Nexus v3 mod-files response did not contain a JSON document.");
            var groups = NexusV3CatalogNormalizer.NormalizeModFiles(
                providerModId,
                groupsDocument);

            var files = new List<CatalogModFile>();
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                using var versionsResponse = await transport
                    .GetModFileVersionsAsync(group.ProviderFileId, auth, ct: ct)
                    .ConfigureAwait(false);
                var versionsDocument = versionsResponse.Document
                    ?? throw new InvalidDataException("Nexus v3 mod-file versions response did not contain a JSON document.");

                files.AddRange(NexusV3CatalogNormalizer.NormalizeModFileVersions(
                    providerModId,
                    group.ProviderFileId,
                    group.Name,
                    versionsDocument));
            }

            MarkConnected(publicOnly: false);
            return files
                .Where(file => file.Category != CatalogFileCategory.Removed)
                .OrderBy(file => CategoryOrder(file.Category))
                .ThenByDescending(file => file.UploadedAt)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
        CatalogAcquisitionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"game={request.Game.Id}; mod={request.Mod.ProviderModId}; file={request.File.ProviderFileId}");
        ct.ThrowIfCancellationRequested();

        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase) ||
            !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Nexus acquisition requires Nexus mod and file identities.",
                nameof(request));
        }

        if (!request.Mod.GameId.Equals(request.Game.Id, StringComparison.Ordinal) ||
            !request.File.ProviderModId.Equals(request.Mod.ProviderModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Nexus acquisition identities do not belong to the selected game/mod.",
                nameof(request));
        }

        var domain = RequireGameDomain(request.Game);
        var encodedDomain = Uri.EscapeDataString(domain);
        var modId = Uri.EscapeDataString(request.Mod.ProviderModId);
        var assisted = new Uri($"https://www.nexusmods.com/{encodedDomain}/mods/{modId}?tab=files");

        return Task.FromResult(new CatalogAcquisitionResolution(
            CatalogAcquisitionKind.Assisted,
            DownloadUri: null,
            AssistedUri: assisted,
            ExpiresAt: null,
            Message: "Open the Nexus Mods file page and use the provider-authorized download flow. Direct v3 acquisition is not enabled yet."));
    }

    public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(health);
    }

    private async Task<NexusV3NormalizedModDetails> GetModDetailsAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        var domain = RequireGameDomain(game);
        var auth = RequireCredential();

        using var response = await transport
            .GetModAsync(domain, providerModId, auth, ct: ct)
            .ConfigureAwait(false);
        var document = response.Document
            ?? throw new InvalidDataException("Nexus v3 mod-details response did not contain a JSON document.");
        var normalized = NexusV3CatalogNormalizer.NormalizeModDetailsWithIdentity(
            game.Id,
            domain,
            document);

        if (!normalized.Mod.ProviderModId.Equals(providerModId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Nexus v3 mod details returned a different game-scoped mod identity.");

        return normalized;
    }

    private NexusV3Credential RequireCredential()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (credential is not null)
            return credential;

        health = new(
            ProviderId,
            CatalogProviderState.AuthenticationRequired,
            "Nexus credentials are required for mod details and file variants.",
            CheckedAt: DateTimeOffset.UtcNow);
        throw new InvalidOperationException("Nexus credentials are required for this catalog operation.");
    }

    private void MarkConnected(bool publicOnly)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"publicOnly={publicOnly}");
        health = new(
            ProviderId,
            publicOnly ? CatalogProviderState.Limited : CatalogProviderState.Connected,
            publicOnly
                ? "Nexus public trending discovery is connected; credentials are required for mod details and file variants."
                : "Connected to Nexus Mods API v3.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return;

        if (ex is NexusV3TransportException transportException)
        {
            if (transportException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.AuthenticationRequired,
                    "Nexus authentication was rejected.",
                    CheckedAt: DateTimeOffset.UtcNow);
                return;
            }

            if (transportException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                health = new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "Nexus API rate limit reached.",
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
                CatalogProviderState.Limited,
                "Nexus request failed; cached catalog data should remain usable.",
                CheckedAt: DateTimeOffset.UtcNow);
            return;
        }

        health = ex switch
        {
            InvalidDataException => new(
                ProviderId,
                CatalogProviderState.Limited,
                "Nexus API schema drift was detected; normalization failed closed.",
                CheckedAt: DateTimeOffset.UtcNow),

            TaskCanceledException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "Nexus request timed out.",
                CheckedAt: DateTimeOffset.UtcNow),

            HttpRequestException => new(
                ProviderId,
                CatalogProviderState.Offline,
                "Nexus Mods is temporarily unreachable.",
                CheckedAt: DateTimeOffset.UtcNow),

            _ => health
        };
    }

    private static string RequireGameDomain(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.NexusGameDomain))
            throw new InvalidOperationException($"{game.DisplayName} does not have a Nexus Mods domain configured.");
        return game.NexusGameDomain.Trim().Trim('/');
    }

    private static int CategoryOrder(CatalogFileCategory category)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return category switch
        {
            CatalogFileCategory.Main => 0,
            CatalogFileCategory.Optional => 1,
            CatalogFileCategory.Update => 2,
            CatalogFileCategory.Miscellaneous => 3,
            CatalogFileCategory.OldVersion => 4,
            CatalogFileCategory.Archived => 5,
            CatalogFileCategory.Removed => 6,
            _ => 7
        };
    }
}
