using System.Security.Cryptography;
using System.Text;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed record CatalogSyncOptions(
    TimeSpan FreshFor,
    bool HydrateFiles = true)
{
    public static CatalogSyncOptions Default { get; } = new(TimeSpan.FromMinutes(30));
}

public sealed record CatalogSyncResult(
    string ProviderId,
    string ScopeKey,
    int ItemCount,
    DateTimeOffset FetchedAt,
    CatalogProviderHealth? Health,
    string? NextCursor = null)
{
    public bool HasMore
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return !string.IsNullOrWhiteSpace(NextCursor);
        }
    }
}

public sealed class CatalogSyncService
{
    private readonly CatalogRepository repository;
    private readonly TimeProvider timeProvider;

    public CatalogSyncService(
        CatalogRepository repository,
        TimeProvider? timeProvider = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CatalogSyncResult> SyncAsync(
        IModCatalogProvider provider,
        CatalogBrowseRequest request,
        CatalogSyncOptions? options = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return await SyncPageAsync(
            provider,
            request,
            cursor: null,
            options,
            ct).ConfigureAwait(false);
    }

    public async Task<int> SyncAllAvailablePagesAsync(
        IModCatalogProvider provider,
        CatalogBrowseRequest request,
        CatalogSyncOptions? options = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        if (provider is not IPagedModCatalogProvider)
            throw new NotSupportedException(
                $"Catalog provider '{provider.ProviderId}' does not expose resumable browse pages.");

        options ??= CatalogSyncOptions.Default;
        ValidateOptions(options);
        ValidateProviderIdentity(provider);

        var scopeKey = BuildScopeKey(request);
        var previous = await repository.GetSyncStateAsync(
            provider.ProviderId,
            scopeKey,
            ct).ConfigureAwait(false);

        CatalogSyncResult current;
        if (previous is null || (string.IsNullOrWhiteSpace(previous.Cursor) && previous.LastSuccessAt is null))
        {
            current = await SyncAsync(provider, request, options, ct).ConfigureAwait(false);
        }
        else if (string.IsNullOrWhiteSpace(previous.Cursor))
        {
            return 0;
        }
        else
        {
            current = await SyncNextPageAsync(provider, request, options, ct).ConfigureAwait(false);
        }

        var total = current.ItemCount;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        while (current.HasMore)
        {
            var cursor = current.NextCursor!;
            if (!seenCursors.Add(cursor))
                throw new InvalidDataException(
                    $"Catalog provider '{provider.ProviderId}' repeated continuation cursor '{cursor}'.");

            if (current.ItemCount == 0)
                throw new InvalidDataException(
                    $"Catalog provider '{provider.ProviderId}' returned an empty page with a continuation cursor.");

            current = await SyncNextPageAsync(provider, request, options, ct).ConfigureAwait(false);
            total += current.ItemCount;
        }

        return total;
    }

    public async Task<CatalogSyncResult> SyncNextPageAsync(
        IModCatalogProvider provider,
        CatalogBrowseRequest request,
        CatalogSyncOptions? options = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        if (provider is not IPagedModCatalogProvider)
            throw new NotSupportedException(
                $"Catalog provider '{provider.ProviderId}' does not expose resumable browse pages.");

        options ??= CatalogSyncOptions.Default;
        ValidateOptions(options);
        ValidateProviderIdentity(provider);

        var scopeKey = BuildScopeKey(request);
        var previous = await repository.GetSyncStateAsync(
            provider.ProviderId,
            scopeKey,
            ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(previous?.Cursor))
        {
            var now = timeProvider.GetUtcNow();
            var health = await TryGetHealthAsync(provider, ct).ConfigureAwait(false);
            return new CatalogSyncResult(
                provider.ProviderId,
                scopeKey,
                0,
                now,
                health,
                NextCursor: null);
        }

        return await SyncPageAsync(
            provider,
            request,
            previous.Cursor,
            options,
            ct).ConfigureAwait(false);
    }

    private async Task<CatalogSyncResult> SyncPageAsync(
        IModCatalogProvider provider,
        CatalogBrowseRequest request,
        string? cursor,
        CatalogSyncOptions? options,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        options ??= CatalogSyncOptions.Default;
        ValidateOptions(options);
        ValidateProviderIdentity(provider);
        if (cursor is not null && provider is not IPagedModCatalogProvider)
            throw new NotSupportedException(
                $"Catalog provider '{provider.ProviderId}' does not expose resumable browse pages.");

        var attemptedAt = timeProvider.GetUtcNow();
        var scopeKey = BuildScopeKey(request);
        await repository.UpsertSourceAsync(
            provider.DisplayName,
            provider.Compliance,
            ct).ConfigureAwait(false);

        try
        {
            CatalogBrowsePage page;
            if (provider is IPagedModCatalogProvider paged)
            {
                page = await paged
                    .BrowsePageAsync(request, cursor, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                var discovered = await provider.SearchModsAsync(request, ct).ConfigureAwait(false);
                page = new CatalogBrowsePage(discovered);
            }

            var prepared = await PrepareBatchAsync(
                provider,
                request,
                page.Items,
                options,
                attemptedAt,
                ct).ConfigureAwait(false);

            var persistenceBatch = prepared
                .Select(cached => (
                    Cached: cached,
                    ReplaceFiles: options.HydrateFiles || cached.Mod.Files.Count > 0))
                .ToArray();
            await repository
                .UpsertBatchAsync(persistenceBatch, ct)
                .ConfigureAwait(false);

            var health = await TryGetHealthAsync(provider, ct).ConfigureAwait(false);
            await PersistRateStateAsync(provider.ProviderId, scopeKey, health, ct).ConfigureAwait(false);

            // Advance continuation only after every item in this page has been persisted.
            // A failed write therefore leaves the previous cursor available for a safe retry.
            await repository.UpsertSyncStateAsync(
                new CatalogSyncState(
                    provider.ProviderId,
                    scopeKey,
                    page.NextCursor,
                    LastSuccessAt: attemptedAt,
                    LastAttemptAt: attemptedAt,
                    CatalogSyncFailureKind.None),
                ct).ConfigureAwait(false);

            return new CatalogSyncResult(
                provider.ProviderId,
                scopeKey,
                prepared.Count,
                attemptedAt,
                health,
                page.NextCursor);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            var health = await TryGetHealthAsync(provider, CancellationToken.None).ConfigureAwait(false);
            await TryPersistFailureAsync(
                provider.ProviderId,
                scopeKey,
                attemptedAt,
                ClassifyFailure(ex, health),
                health).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IReadOnlyList<CachedCatalogMod>> PrepareBatchAsync(
        IModCatalogProvider provider,
        CatalogBrowseRequest request,
        IReadOnlyList<CatalogMod> discovered,
        CatalogSyncOptions options,
        DateTimeOffset fetchedAt,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(discovered);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CachedCatalogMod>(discovered.Count);

        foreach (var original in discovered)
        {
            ct.ThrowIfCancellationRequested();
            ValidateModIdentity(provider.ProviderId, request.Game.Id, original);

            var mod = original;
            if (options.HydrateFiles
                && provider.Capabilities.HasFlag(CatalogProviderCapabilities.FileList)
                && mod.Files.Count == 0)
            {
                var files = await provider.GetModFilesAsync(
                    request.Game,
                    mod.ProviderModId,
                    ct).ConfigureAwait(false);
                mod = mod with { Files = files };
            }

            ValidateModIdentity(provider.ProviderId, request.Game.Id, mod);
            ValidateFileIdentities(mod);

            if (!seen.Add(mod.CanonicalId))
                throw new InvalidDataException(
                    $"Catalog provider '{provider.ProviderId}' returned duplicate canonical id '{mod.CanonicalId}'.");

            result.Add(new CachedCatalogMod(
                mod,
                new CatalogCacheMetadata(
                    fetchedAt,
                    fetchedAt.Add(options.FreshFor),
                    SourceFingerprint: BuildFingerprint(mod))));
        }

        return result;
    }

    private static async Task<CatalogProviderHealth?> TryGetHealthAsync(
        IModCatalogProvider provider,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            return await provider.GetHealthAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task TryPersistFailureAsync(
        string providerId,
        string scopeKey,
        DateTimeOffset attemptedAt,
        CatalogSyncFailureKind failureKind,
        CatalogProviderHealth? health)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            await PersistRateStateAsync(
                providerId,
                scopeKey,
                health,
                CancellationToken.None).ConfigureAwait(false);
            var previous = await repository.GetSyncStateAsync(
                providerId,
                scopeKey,
                CancellationToken.None).ConfigureAwait(false);
            await repository.UpsertSyncStateAsync(
                new CatalogSyncState(
                    providerId,
                    scopeKey,
                    previous?.Cursor,
                    previous?.LastSuccessAt,
                    attemptedAt,
                    failureKind),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Failure telemetry must never replace the provider exception that caused the sync failure.
        }
    }

    private async Task PersistRateStateAsync(
        string providerId,
        string scopeKey,
        CatalogProviderHealth? health,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (health?.RateLimit is null) return;
        await repository.UpsertRateStateAsync(
            new CatalogRateState(providerId, scopeKey, health.RateLimit),
            ct).ConfigureAwait(false);
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

    private static void ValidateProviderIdentity(IModCatalogProvider provider)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.DisplayName);
        if (!string.Equals(
                provider.ProviderId,
                provider.Compliance.ProviderId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Catalog provider id does not match its compliance provider id.");
        }
    }

    private static void ValidateOptions(CatalogSyncOptions options)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (options.FreshFor < TimeSpan.Zero || options.FreshFor > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Catalog freshness must be between zero and seven days.");
    }

    private static void ValidateModIdentity(
        string providerId,
        string gameId,
        CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentException.ThrowIfNullOrWhiteSpace(mod.ProviderModId);

        var expectedCanonicalId = CatalogMod.BuildCanonicalId(providerId, mod.ProviderModId);
        if (!string.Equals(mod.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mod.GameId, gameId, StringComparison.Ordinal)
            || !string.Equals(mod.CanonicalId, expectedCanonicalId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Catalog provider '{providerId}' returned an item with mismatched provider/game identity.");
        }
    }

    private static void ValidateFileIdentities(CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in mod.Files)
        {
            if (!string.Equals(file.ProviderId, mod.ProviderId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(file.ProviderModId, mod.ProviderModId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(file.ProviderFileId))
            {
                throw new InvalidDataException(
                    "Catalog provider returned a file with mismatched parent identity.");
            }

            if (!ids.Add(file.ProviderFileId))
                throw new InvalidDataException(
                    $"Catalog provider returned duplicate file id '{file.ProviderFileId}'.");
        }
    }

    private static string BuildScopeKey(CatalogBrowseRequest request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var queryKey = string.IsNullOrWhiteSpace(request.Query)
            ? "all"
            : Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(request.Query.Trim().ToLowerInvariant())))
                .ToLowerInvariant()[..16];
        return $"{request.Game.Id}:{request.Mode.ToString().ToLowerInvariant()}:{queryKey}";
    }

    private static string BuildFingerprint(CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var payload = string.Join(
            "\n",
            mod.ProviderId,
            mod.ProviderModId,
            mod.GameId,
            mod.Version ?? string.Empty,
            mod.UpdatedAt?.ToUniversalTime().ToString("O") ?? string.Empty,
            mod.SourceUrl,
            string.Join(",", mod.Files.Select(file =>
                $"{file.ProviderFileId}:{file.Version}:{file.FileName}")));
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
