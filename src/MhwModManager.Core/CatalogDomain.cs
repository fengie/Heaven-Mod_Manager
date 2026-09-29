namespace MhwModManager.Core;

[Flags]
public enum CatalogProviderCapabilities
{
    None = 0,
    Search = 1 << 0,
    Browse = 1 << 1,
    Categories = 1 << 2,
    Metadata = 1 << 3,
    Images = 1 << 4,
    FileList = 1 << 5,
    FileVariants = 1 << 6,
    Dependencies = 1 << 7,
    DirectDownload = 1 << 8,
    AuthenticatedDownload = 1 << 9,
    BrowserAssistedDownload = 1 << 10,
    Updates = 1 << 11,
    Ratings = 1 << 12,
    Comments = 1 << 13,
    VersionHistory = 1 << 14
}

public enum CatalogFileCategory
{
    Unknown,
    Main,
    Optional,
    Update,
    Miscellaneous,
    OldVersion,
    Archived,
    Removed
}

public enum CatalogProviderState
{
    Connected,
    AuthenticationRequired,
    RateLimited,
    Limited,
    Disabled,
    Offline
}

public enum CatalogBrowseMode
{
    Trending,
    RecentlyUpdated,
    Latest,
    Popular
}

public enum CatalogAcquisitionKind
{
    Direct,
    AuthenticatedDirect,
    Assisted,
    Unavailable
}

public sealed record CatalogImage(string Url, string? Caption = null, bool IsThumbnail = false);

public sealed record CatalogDependency(
    string Name,
    string? ProviderId = null,
    string? ProviderModId = null,
    string? Url = null,
    bool Required = true);

public sealed record CatalogGame(
    string Id,
    string DisplayName,
    string? ProviderGameId = null,
    bool Installed = false);

public sealed record CatalogModFile(
    string ProviderId,
    string ProviderModId,
    string ProviderFileId,
    string Name,
    string FileName,
    CatalogFileCategory Category,
    string? Version = null,
    long? SizeBytes = null,
    string? Description = null,
    DateTimeOffset? UploadedAt = null,
    bool Required = false,
    bool Recommended = false,
    IReadOnlyList<CatalogDependency>? Dependencies = null,
    string? ProviderMetadata = null);

public sealed record CatalogMod(
    string CanonicalId,
    string ProviderId,
    string ProviderModId,
    string GameId,
    string Name,
    string Summary,
    string Description,
    string Author,
    string? Version,
    string? Category,
    IReadOnlyList<string> Tags,
    string? Thumbnail,
    IReadOnlyList<CatalogImage> Screenshots,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    long? Downloads,
    long? Endorsements,
    double? Rating,
    IReadOnlyList<CatalogDependency> Dependencies,
    string SourceUrl,
    IReadOnlyList<CatalogModFile> Files,
    string? ProviderMetadata = null)
{
    public static string BuildCanonicalId(string providerId, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);
        return $"{providerId.Trim().ToLowerInvariant()}:{providerModId.Trim().ToLowerInvariant()}";
    }
}

public sealed record CatalogBrowseRequest(
    GameProfile Game,
    string? Query = null,
    CatalogBrowseMode Mode = CatalogBrowseMode.Trending,
    int Limit = 100);

public sealed record CatalogRateLimit(
    int? HourlyLimit,
    int? HourlyRemaining,
    int? DailyLimit,
    int? DailyRemaining,
    DateTimeOffset? RetryAfter = null,
    DateTimeOffset? ObservedAt = null);

public sealed record CatalogProviderHealth(
    string ProviderId,
    CatalogProviderState State,
    string Message,
    CatalogRateLimit? RateLimit = null,
    DateTimeOffset? CheckedAt = null);

public sealed record CatalogAcquisitionRequest(
    GameProfile Game,
    CatalogMod Mod,
    CatalogModFile File);

public sealed record CatalogAcquisitionResolution(
    CatalogAcquisitionKind Kind,
    Uri? DownloadUri,
    Uri? AssistedUri,
    DateTimeOffset? ExpiresAt,
    string Message);

public sealed record InstalledCatalogOrigin(
    string ModId,
    string ProviderId,
    string ProviderModId,
    string ProviderFileId,
    string? InstalledVersion,
    DateTimeOffset DownloadedAt,
    string SourceUrl,
    string ArchiveSha256,
    string? ProviderMetadata = null);

public interface IModCatalogProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    CatalogProviderCapabilities Capabilities { get; }
    CatalogProviderCompliance Compliance { get; }

    Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogMod>> SearchModsAsync(CatalogBrowseRequest request, CancellationToken ct = default);
    Task<CatalogMod?> GetModAsync(GameProfile game, string providerModId, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(GameProfile game, string providerModId, CancellationToken ct = default);
    Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(CatalogAcquisitionRequest request, CancellationToken ct = default);
    Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default);
}
