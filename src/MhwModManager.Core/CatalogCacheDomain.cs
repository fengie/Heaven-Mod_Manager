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

public sealed record CatalogSearchRequest(
    string GameId,
    string? Query = null,
    IReadOnlyList<string>? ProviderIds = null,
    int Limit = 100,
    bool IncludeStale = true);

public sealed record CatalogSyncState(
    string ProviderId,
    string ScopeKey,
    string? Cursor,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    string? ETag = null,
    DateTimeOffset? LastModified = null);

public sealed record CatalogProvenance(
    string CanonicalId,
    string ProviderId,
    string ProviderModId,
    string SourceUrl,
    DateTimeOffset ObservedAt,
    string? SourceFingerprint = null);

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
