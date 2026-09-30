using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed class NexusV3CatalogProvider : IModCatalogProvider
{
    private readonly NexusV3Transport transport;
    private readonly NexusV3Credential? credential;
    private CatalogProviderHealth health = new(
        "nexus",
        CatalogProviderState.Limited,
        "Nexus public discovery is available; authenticated mod details and files require credentials.",
        CheckedAt: DateTimeOffset.UtcNow);

    public NexusV3CatalogProvider(NexusV3Transport transport, NexusV3Credential? credential = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
        this.credential = credential;
    }

    public string ProviderId => "nexus";
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
        return Task.FromResult<IReadOnlyList<CatalogGame>>([]);
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"game={request.Game.Id}; query={request.Query ?? "<empty>"}");

        var domain = RequireGameDomain(request.Game);
        try
        {
            using var response = await transport.GetTrendingModsAsync(domain, ct: ct).ConfigureAwait(false);
            var mods = ParseTrending(response, request.Game);
            var query = request.Query?.Trim();

            IEnumerable<CatalogMod> filtered = mods;
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(mod =>
                    mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    mod.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    mod.Summary.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var result = filtered
                .Take(Math.Clamp(request.Limit, 1, 500))
                .ToArray();

            health = new(
                ProviderId,
                credential is null ? CatalogProviderState.Limited : CatalogProviderState.Connected,
                credential is null
                    ? "Nexus public trending discovery is connected; credentials are required for mod details and file variants."
                    : "Connected to Nexus Mods API v3.",
                CheckedAt: DateTimeOffset.UtcNow);
            return result;
        }
        catch (Exception ex) when (TrackFailure(ex, ct))
        {
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
            var (mod, _) = await GetModDetailsCoreAsync(game, providerModId, ct).ConfigureAwait(false);
            MarkConnected();
            return mod;
        }
        catch (Exception ex) when (TrackFailure(ex, ct))
        {
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
            var (_, globalModId) = await GetModDetailsCoreAsync(game, providerModId, ct).ConfigureAwait(false);
            var auth = RequireCredential();

            using var response = await transport
                .GetModFilesAsync(globalModId, auth, ct: ct)
                .ConfigureAwait(false);

            var data = RequireDataObject(response, "mod files");
            if (!data.TryGetProperty("mod_files", out var groups) || groups.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Nexus Mods API v3 mod-files payload is missing the mod_files array.");

            var files = new List<CatalogModFile>();
            foreach (var group in groups.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (group.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Nexus Mods API v3 mod_files contains a non-object entry.");

                var modFileId = RequireString(group, "id", "mod file");
                var modFileName = RequireString(group, "name", "mod file");

                using var versionsResponse = await transport
                    .GetModFileVersionsAsync(modFileId, auth, ct: ct)
                    .ConfigureAwait(false);

                var versionData = RequireDataObject(versionsResponse, "mod file versions");
                if (!versionData.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Nexus Mods API v3 mod-file versions payload is missing the versions array.");

                foreach (var version in versions.EnumerateArray())
                {
                    if (version.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Nexus Mods API v3 versions contains a non-object entry.");

                    var normalized = ParseModFileVersion(providerModId, modFileId, modFileName, version);
                    if (normalized.Category != CatalogFileCategory.Removed)
                        files.Add(normalized);
                }
            }

            MarkConnected();
            return files
                .OrderBy(file => CategoryOrder(file.Category))
                .ThenByDescending(file => file.UploadedAt)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (TrackFailure(ex, ct))
        {
            throw;
        }
    }

    public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
        CatalogAcquisitionRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod(
            $"game={request.Game.Id}; mod={request.Mod.ProviderModId}; file={request.File.ProviderFileId}");
        ct.ThrowIfCancellationRequested();

        var domain = RequireGameDomain(request.Game);
        var modId = Uri.EscapeDataString(request.Mod.ProviderModId);
        var assisted = new Uri($"https://www.nexusmods.com/{domain}/mods/{modId}?tab=files");

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

    private async Task<(CatalogMod Mod, string GlobalModId)> GetModDetailsCoreAsync(
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

        var data = RequireDataObject(response, "mod details");
        var globalId = RequireString(data, "id", "mod details");
        var gameScopedId = RequireString(data, "game_scoped_id", "mod details");
        var name = RequireString(data, "name", "mod details");

        if (!string.Equals(gameScopedId, providerModId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Nexus Mods API v3 returned a mod whose game_scoped_id does not match the requested mod.");

        var summary = GetString(data, "summary") ?? string.Empty;
        var description = GetString(data, "description") ?? summary;
        var author = GetString(data, "author")
            ?? GetNestedString(data, "uploader", "name")
            ?? GetString(data, "uploaded_by")
            ?? "Unknown author";
        var thumbnail = GetString(data, "thumbnail_url") ?? GetString(data, "picture_url");
        var screenshots = string.IsNullOrWhiteSpace(thumbnail)
            ? Array.Empty<CatalogImage>()
            : [new CatalogImage(thumbnail, IsThumbnail: true)];

        var sourceUrl = $"https://www.nexusmods.com/{domain}/mods/{Uri.EscapeDataString(gameScopedId)}";
        var metadata = JsonSerializer.Serialize(new
        {
            nexus_global_mod_id = globalId,
            nexus_game_scoped_id = gameScopedId
        });

        return (
            new CatalogMod(
                CatalogMod.BuildCanonicalId(ProviderId, gameScopedId),
                ProviderId,
                gameScopedId,
                game.Id,
                name,
                summary,
                description,
                author,
                GetString(data, "version"),
                GetString(data, "category_name"),
                [],
                thumbnail,
                screenshots,
                GetTimestamp(data, "created_at") ?? GetTimestamp(data, "created_timestamp"),
                GetTimestamp(data, "updated_at") ?? GetTimestamp(data, "updated_timestamp"),
                GetLong(data, "downloads") ?? GetLong(data, "download_count"),
                GetLong(data, "endorsements") ?? GetLong(data, "endorsement_count"),
                GetDouble(data, "rating"),
                [],
                sourceUrl,
                [],
                metadata),
            globalId);
    }

    private IReadOnlyList<CatalogMod> ParseTrending(
        NexusV3TransportResponse response,
        GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        var data = RequireDataObject(response, "trending");
        if (!data.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Nexus Mods API v3 trending payload is missing the mods array.");

        var domain = RequireGameDomain(game);
        var result = new List<CatalogMod>();
        foreach (var item in mods.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Nexus Mods API v3 trending mods contains a non-object entry.");

            var name = RequireString(item, "name", "trending mod");
            var sourceUrl = RequireString(item, "mod_page_url", "trending mod");
            var gameScopedId = ExtractGameScopedId(sourceUrl, domain);
            var author = GetString(item, "author") ?? "Unknown author";
            var summary = GetString(item, "summary") ?? string.Empty;
            var picture = GetString(item, "picture_url");
            var screenshots = string.IsNullOrWhiteSpace(picture)
                ? Array.Empty<CatalogImage>()
                : [new CatalogImage(picture, IsThumbnail: true)];

            result.Add(new CatalogMod(
                CatalogMod.BuildCanonicalId(ProviderId, gameScopedId),
                ProviderId,
                gameScopedId,
                game.Id,
                name,
                summary,
                summary,
                author,
                Version: null,
                Category: null,
                Tags: [],
                Thumbnail: picture,
                Screenshots: screenshots,
                CreatedAt: null,
                UpdatedAt: null,
                Downloads: null,
                Endorsements: null,
                Rating: null,
                Dependencies: [],
                SourceUrl: sourceUrl,
                Files: [],
                ProviderMetadata: null));
        }

        return result;
    }

    private static CatalogModFile ParseModFileVersion(
        string providerModId,
        string modFileId,
        string modFileName,
        JsonElement version)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mod={providerModId}; modFile={modFileId}");
        var versionId = RequireString(version, "id", "mod file version");
        var name = RequireString(version, "name", "mod file version");
        var versionText = RequireString(version, "version", "mod file version");
        var categoryText = RequireString(version, "category", "mod file version");
        var uploadedAt = RequireTimestamp(version, "uploaded_at", "mod file version");

        var metadata = JsonSerializer.Serialize(new
        {
            nexus_mod_file_id = modFileId,
            nexus_mod_file_name = modFileName,
            nexus_game_scoped_file_id = GetString(version, "game_scoped_id")
        });

        return new CatalogModFile(
            ProviderId: "nexus",
            ProviderModId: providerModId,
            ProviderFileId: versionId,
            Name: name,
            FileName: name,
            Category: ParseCategory(categoryText),
            Version: versionText,
            SizeBytes: GetLong(version, "size")
                ?? MultiplyKilobytes(GetLong(version, "size_kb")),
            Description: GetString(version, "description"),
            UploadedAt: uploadedAt,
            Required: false,
            Recommended: GetBool(version, "is_primary"),
            Dependencies: [],
            ProviderMetadata: metadata);
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

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to Nexus Mods API v3.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private bool TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
            return false;

        health = ex switch
        {
            NexusV3TransportException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                new(ProviderId, CatalogProviderState.AuthenticationRequired, "Nexus authentication was rejected.", CheckedAt: DateTimeOffset.UtcNow),

            NexusV3TransportException { StatusCode: HttpStatusCode.TooManyRequests } rateLimited =>
                new(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "Nexus API rate limit reached.",
                    new CatalogRateLimit(null, null, null, null,
                        rateLimited.RetryAfter is null ? null : DateTimeOffset.UtcNow.Add(rateLimited.RetryAfter.Value),
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

        return false;
    }

    private static JsonElement RequireDataObject(NexusV3TransportResponse response, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"context={context}");
        if (response.Document is null)
            throw new InvalidDataException($"Nexus Mods API v3 {context} response did not contain a JSON document.");

        var root = response.Document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Nexus Mods API v3 {context} response has an invalid data object.");
        }

        return data;
    }

    private static string RequireGameDomain(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.NexusGameDomain))
            throw new InvalidOperationException($"{game.DisplayName} does not have a Nexus Mods domain configured.");
        return game.NexusGameDomain.Trim().Trim('/');
    }

    private static string ExtractGameScopedId(string sourceUrl, string domain)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.EndsWith("nexusmods.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Nexus trending mod_page_url is not a valid Nexus HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 3 ||
            !segments[0].Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("mods", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(segments[2]))
        {
            throw new InvalidDataException("Nexus trending mod_page_url does not contain the expected game-scoped mod identity.");
        }

        return Uri.UnescapeDataString(segments[2]);
    }

    private static string RequireString(JsonElement element, string property, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}; context={context}");
        var value = GetString(element, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Nexus Mods API v3 {context} is missing required '{property}'.");
        return value;
    }

    private static string? GetString(JsonElement element, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}");
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? GetNestedString(JsonElement element, string objectProperty, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"object={objectProperty}; property={property}");
        if (!element.TryGetProperty(objectProperty, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return null;
        return GetString(nested, property);
    }

    private static long? GetLong(JsonElement element, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}");
        if (!element.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? GetDouble(JsonElement element, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}");
        if (!element.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool GetBool(JsonElement element, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}");
        if (!element.TryGetProperty(property, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.True)
            return true;
        if (value.ValueKind == JsonValueKind.False)
            return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number != 0;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static DateTimeOffset? GetTimestamp(JsonElement element, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}");
        var text = GetString(element, property);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset RequireTimestamp(JsonElement element, string property, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={property}; context={context}");
        return GetTimestamp(element, property)
            ?? throw new InvalidDataException($"Nexus Mods API v3 {context} has invalid required '{property}'.");
    }

    private static long? MultiplyKilobytes(long? kilobytes)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (kilobytes is null)
            return null;
        try
        {
            return checked(kilobytes.Value * 1024L);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("Nexus Mods API v3 file size overflowed the supported range.", ex);
        }
    }

    public static CatalogFileCategory ParseCategory(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value?.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant() switch
        {
            "main" => CatalogFileCategory.Main,
            "update" => CatalogFileCategory.Update,
            "optional" => CatalogFileCategory.Optional,
            "old_version" => CatalogFileCategory.OldVersion,
            "miscellaneous" or "misc" => CatalogFileCategory.Miscellaneous,
            "removed" => CatalogFileCategory.Removed,
            "archived" => CatalogFileCategory.Archived,
            _ => CatalogFileCategory.Unknown
        };
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
