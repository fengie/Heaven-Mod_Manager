using System.Net;

namespace MhwModManager.Core;

public sealed class NexusV3CatalogProvider : IModCatalogProvider
{
    private readonly NexusV3Transport transport;
    private readonly NexusV3Credential? credential;
    private CatalogProviderHealth health = new(
        NexusV3CatalogNormalizer.ProviderId,
        CatalogProviderState.Limited,
        "Nexus public trending discovery is available; authenticated details and files require credentials.",
        CheckedAt: DateTimeOffset.UtcNow);

    public NexusV3CatalogProvider(
        NexusV3Transport transport,
        NexusV3Credential? credential = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
        this.credential = credential;
    }

    public string ProviderId => NexusV3CatalogNormalizer.ProviderId;
    public string DisplayName => "Nexus Mods";

    public CatalogProviderCapabilities Capabilities =>
        CatalogProviderCapabilities.Search |
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
        return Task.FromResult<IReadOnlyList<CatalogGame>>(Array.Empty<CatalogGame>());
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);

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

            IEnumerable<CatalogMod> filtered = mods;
            var query = request.Query?.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(mod =>
                    mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || mod.Author.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || mod.Summary.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            MarkSearchSuccess();
            return filtered
                .Take(Math.Clamp(request.Limit, 1, 500))
                .ToArray();
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
        try
        {
            var normalized = await GetModDetailsCoreAsync(game, providerModId, ct)
                .ConfigureAwait(false);
            MarkConnected();
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
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var details = await GetModDetailsCoreAsync(game, providerModId, ct)
                .ConfigureAwait(false);
            var auth = RequireCredential();

            using var groupsResponse = await transport
                .GetModFilesAsync(details.GlobalModId, auth, ct: ct)
                .ConfigureAwait(false);
            var groupsDocument = groupsResponse.Document
                ?? throw new InvalidDataException("Nexus v3 mod-files response did not contain a JSON document.");
            var groups = NexusV3CatalogNormalizer.NormalizeModFileGroups(groupsDocument);

            var result = new List<CatalogModFile>();
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                using var versionsResponse = await transport
                    .GetModFileVersionsAsync(group.Id, auth, ct: ct)
                    .ConfigureAwait(false);
                var versionsDocument = versionsResponse.Document
                    ?? throw new InvalidDataException("Nexus v3 mod-file versions response did not contain a JSON document.");

                var versions = NexusV3CatalogNormalizer.NormalizeModFileVersions(
                    providerModId,
                    group.Id,
                    group.Name,
                    versionsDocument);
                result.AddRange(versions.Where(file => file.Category != CatalogFileCategory.Removed));
            }

            MarkConnected();
            return result
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
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.Mod.ProviderModId.Equals(request.File.ProviderModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Catalog acquisition request does not describe one Nexus mod/file identity.");
        }

        var domain = RequireGameDomain(request.Game);
        var assisted = new Uri(
            $"https://www.nexusmods.com/{Uri.EscapeDataString(domain)}/mods/{Uri.EscapeDataString(request.Mod.ProviderModId)}?tab=files");

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

    private async Task<NexusV3NormalizedMod> GetModDetailsCoreAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);

        var domain = RequireGameDomain(game);
        var auth = RequireCredential();
        using var response = await transport
            .GetModAsync(domain, providerModId, auth, ct: ct)
            .ConfigureAwait(false);
        var document = response.Document
            ?? throw new InvalidDataException("Nexus v3 mod-details response did not contain a JSON document.");

        var normalized = NexusV3CatalogNormalizer.NormalizeModDetails(
            game.Id,
            domain,
            document);
        if (!normalized.Mod.ProviderModId.Equals(providerModId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Nexus v3 returned a mod whose game-scoped id does not match the requested mod.");
        }

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

    private void MarkSearchSuccess()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            credential is null ? CatalogProviderState.Limited : CatalogProviderState.Connected,
            credential is null
                ? "Nexus public trending discovery is connected; credentials are required for details and file variants."
                : "Connected to Nexus Mods API v3.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to Nexus Mods API v3.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return;

        health = ex switch
        {
            NexusV3TransportException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                new(ProviderId, CatalogProviderState.AuthenticationRequired, "Nexus authentication was rejected.", CheckedAt: DateTimeOffset.UtcNow),

            NexusV3TransportException { StatusCode: HttpStatusCode.TooManyRequests } rateLimited =>
                new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "Nexus API rate limit reached.",
                    new CatalogRateLimit(
                        null,
                        null,
                        null,
                        null,
                        rateLimited.RetryAfter is null
                            ? null
                            : DateTimeOffset.UtcNow.Add(rateLimited.RetryAfter.Value),
                        DateTimeOffset.UtcNow),
                    DateTimeOffset.UtcNow),

            NexusV3TransportException =>
                new(ProviderId, CatalogProviderState.Limited, "Nexus request failed; cached catalog data should remain usable.", CheckedAt: DateTimeOffset.UtcNow),

            InvalidDataException =>
                new(ProviderId, CatalogProviderState.Limited, "Nexus API schema drift was detected; normalization failed closed.", CheckedAt: DateTimeOffset.UtcNow),

            TaskCanceledException =>
                new(ProviderId, CatalogProviderState.Offline, "Nexus request timed out.", CheckedAt: DateTimeOffset.UtcNow),

            HttpRequestException =>
                new(ProviderId, CatalogProviderState.Offline, "Nexus Mods is temporarily unreachable.", CheckedAt: DateTimeOffset.UtcNow),

            _ => health
        };
    }

    private static string RequireGameDomain(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
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
