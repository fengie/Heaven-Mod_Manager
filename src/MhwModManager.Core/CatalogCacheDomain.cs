namespace MhwModManager.Core;

public sealed record CatalogCacheMetadata(
    DateTimeOffset FetchedAt,
    DateTimeOffset? ExpiresAt = null,
    string? ETag = null,
    DateTimeOffset? LastModified = null,
    string? SourceFingerprint = null);

public sealed record CachedCatalogMod(
    CatalogMod Mod,
    CatalogCacheMetadata Cache)
{
    public bool IsStale(DateTimeOffset now)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Cache.ExpiresAt is not null && Cache.ExpiresAt.Value <= now;
    }
}
