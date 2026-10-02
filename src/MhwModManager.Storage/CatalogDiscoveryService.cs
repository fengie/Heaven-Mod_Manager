using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed record CatalogProviderSyncOutcome(
    string ProviderId,
    string DisplayName,
    bool Succeeded,
    int ItemCount,
    CatalogSyncFailureKind FailureKind,
    CatalogProviderHealth? Health);

public sealed record CatalogDiscoveryResult(
    IReadOnlyList<CatalogProviderSyncOutcome> Providers)
{
    public int SuccessfulProviders
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Providers.Count(provider => provider.Succeeded);
        }
    }

    public int FailedProviders
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Providers.Count - SuccessfulProviders;
        }
    }

    public int ItemCount
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Providers.Sum(provider => provider.ItemCount);
        }
    }
}

public sealed class CatalogDiscoveryService
{
    private readonly CatalogSyncService syncService;

    public CatalogDiscoveryService(CatalogSyncService syncService)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(syncService);
        this.syncService = syncService;
    }

    public Task<CatalogDiscoveryResult> SearchAsync(
        IEnumerable<IModCatalogProvider> providers,
        GameProfile game,
        string query,
        int limit,
        CatalogSyncOptions? options = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        var providerList = providers.ToArray();
        ValidateProviders(providerList);
        var searchable = providerList
            .Where(provider => provider.Capabilities.HasFlag(CatalogProviderCapabilities.Search))
            .ToArray();
        return RefreshAsync(
            searchable,
            new CatalogBrowseRequest(
                game,
                Query: query.Trim(),
                Limit: limit),
            options,
            ct);
    }

    public async Task<CatalogDiscoveryResult> RefreshAsync(
        IEnumerable<IModCatalogProvider> providers,
        CatalogBrowseRequest request,
        CatalogSyncOptions? options = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(request);

        var providerList = providers.ToArray();
        ValidateProviders(providerList);

        var outcomes = new List<CatalogProviderSyncOutcome>(providerList.Length);
        foreach (var provider in providerList.OrderBy(
                     provider => provider.ProviderId,
                     StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await syncService.SyncAsync(
                    provider,
                    request,
                    options,
                    ct).ConfigureAwait(false);
                outcomes.Add(new CatalogProviderSyncOutcome(
                    provider.ProviderId,
                    provider.DisplayName,
                    Succeeded: true,
                    result.ItemCount,
                    CatalogSyncFailureKind.None,
                    result.Health));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var health = await TryGetHealthAsync(provider).ConfigureAwait(false);
                outcomes.Add(new CatalogProviderSyncOutcome(
                    provider.ProviderId,
                    provider.DisplayName,
                    Succeeded: false,
                    ItemCount: 0,
                    ClassifyFailure(ex, health),
                    health));
            }
        }

        return new CatalogDiscoveryResult(outcomes);
    }

    private static void ValidateProviders(IReadOnlyList<IModCatalogProvider> providers)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (provider is null)
                throw new ArgumentException("Catalog provider collection cannot contain null entries.", nameof(providers));
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.ProviderId);
            if (!ids.Add(provider.ProviderId))
                throw new ArgumentException(
                    $"Catalog provider collection contains duplicate provider id '{provider.ProviderId}'.",
                    nameof(providers));
        }
    }

    private static async Task<CatalogProviderHealth?> TryGetHealthAsync(
        IModCatalogProvider provider)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            return await provider.GetHealthAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static CatalogSyncFailureKind ClassifyFailure(
        Exception ex,
        CatalogProviderHealth? health)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is TaskCanceledException) return CatalogSyncFailureKind.Timeout;
        if (ex is InvalidDataException) return CatalogSyncFailureKind.SchemaDrift;
        if (health is not null)
        {
            return health.State switch
            {
                CatalogProviderState.AuthenticationRequired => CatalogSyncFailureKind.AuthenticationRequired,
                CatalogProviderState.RateLimited => CatalogSyncFailureKind.RateLimited,
                CatalogProviderState.Offline => CatalogSyncFailureKind.Offline,
                _ => CatalogSyncFailureKind.ProviderError
            };
        }

        return ex is HttpRequestException
            ? CatalogSyncFailureKind.Offline
            : CatalogSyncFailureKind.ProviderError;
    }
}
