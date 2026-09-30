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

public sealed record CatalogSyncState(
    string ProviderId,
    string ScopeKey,
    string? Cursor,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError);

public sealed record CatalogRateState(
    string ProviderId,
    string ScopeKey,
    CatalogRateLimit RateLimit);

public enum CatalogLinkEvidenceKind
{
    ExplicitCrossLink,
    CanonicalProjectUrl,
    TrustedPackageIdentifier,
    ExactArchiveSha256
}

public sealed record CatalogLink(
    string LeftCanonicalId,
    string RightCanonicalId,
    CatalogLinkEvidenceKind EvidenceKind,
    string EvidenceValue,
    DateTimeOffset ObservedAt);

public sealed record CatalogProvenance(
    string CanonicalId,
    string ProviderId,
    string SourceUrl,
    DateTimeOffset FetchedAt,
    string? ETag,
    DateTimeOffset? LastModified,
    string? SourceFingerprint);

