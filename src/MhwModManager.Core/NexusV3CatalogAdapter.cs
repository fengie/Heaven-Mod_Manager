using System.Text.Json;

namespace MhwModManager.Core;

public sealed record NexusV3ModMetadata(
    string GlobalModId,
    string NexusGameId);

public sealed record NexusV3ModFileHeader(
    string Id,
    string Name,
    bool IsActive,
    DateTimeOffset? LastFileUploadedAt,
    int VersionsCount,
    int ArchivedCount,
    int RemovedCount);

public sealed record NexusV3FileMetadata(
    string PersistentFileId,
    string PersistentFileName,
    string GameScopedFileVersionId,
    bool IsPrimary);

public static class NexusV3CatalogNormalizer
{
    public const string ProviderId = "nexus";

    public static IReadOnlyList<CatalogMod> NormalizeTrending(
        JsonDocument document,
        GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(game);

        var domain = RequireGameDomain(game);
        var data = RequiredObject(document.RootElement, "data", "Nexus v3 trending response");
        var mods = RequiredArray(data, "mods", "Nexus v3 trending response");
        var normalized = new List<CatalogMod>();

        foreach (var item in mods.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw SchemaError("Nexus v3 trending mod entry must be an object.");

            var name = RequiredString(item, "name", "Nexus v3 trending mod entry");
            var sourceUrl = RequiredHttpsUri(item, "mod_page_url", "Nexus v3 trending mod entry");
            var providerModId = ParseProviderModId(sourceUrl, domain);
            var summary = OptionalString(item, "summary") ?? string.Empty;
            var author = OptionalString(item, "author") ?? string.Empty;
            var thumbnail = OptionalHttpsUri(item, "picture_url")?.AbsoluteUri;

            normalized.Add(new CatalogMod(
                CanonicalId: CatalogMod.BuildCanonicalId(ProviderId, providerModId),
                ProviderId: ProviderId,
                ProviderModId: providerModId,
                GameId: game.Id,
                Name: name,
                Summary: summary,
                Description: summary,
                Author: author,
                Version: null,
                Category: null,
                Tags: Array.Empty<string>(),
                Thumbnail: thumbnail,
                Screenshots: Array.Empty<CatalogImage>(),
                CreatedAt: null,
                UpdatedAt: null,
                Downloads: null,
                Endorsements: null,
                Rating: null,
                Dependencies: Array.Empty<CatalogDependency>(),
                SourceUrl: sourceUrl.AbsoluteUri,
                Files: Array.Empty<CatalogModFile>()));
        }

        return normalized;
    }

    public static CatalogMod NormalizeMod(
        JsonDocument document,
        GameProfile game,
        CatalogMod? seed = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(game);

        var data = RequiredObject(document.RootElement, "data", "Nexus v3 mod response");
        var globalModId = RequiredString(data, "id", "Nexus v3 mod response");
        var providerModId = RequiredString(data, "game_scoped_id", "Nexus v3 mod response");
        var nexusGameId = RequiredString(data, "game_id", "Nexus v3 mod response");
        var compatibleSeed = IsCompatibleSeed(seed, game, providerModId) ? seed : null;

        var name = OptionalString(data, "name")
            ?? compatibleSeed?.Name
            ?? "Nexus Mod " + providerModId;
        var summary = OptionalString(data, "summary")
            ?? compatibleSeed?.Summary
            ?? string.Empty;
        var description = OptionalString(data, "description")
            ?? compatibleSeed?.Description
            ?? summary;
        var author = OptionalString(data, "author")
            ?? compatibleSeed?.Author
            ?? string.Empty;
        var thumbnail = OptionalHttpsUri(data, "thumbnail_url")?.AbsoluteUri
            ?? compatibleSeed?.Thumbnail;
        var sourceUrl = BuildSourceUri(RequireGameDomain(game), providerModId).AbsoluteUri;
        var metadata = JsonSerializer.Serialize(new NexusV3ModMetadata(globalModId, nexusGameId));

        return new CatalogMod(
            CanonicalId: CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId: ProviderId,
            ProviderModId: providerModId,
            GameId: game.Id,
            Name: name,
            Summary: summary,
            Description: description,
            Author: author,
            Version: compatibleSeed?.Version,
            Category: compatibleSeed?.Category,
            Tags: compatibleSeed?.Tags ?? Array.Empty<string>(),
            Thumbnail: thumbnail,
            Screenshots: compatibleSeed?.Screenshots ?? Array.Empty<CatalogImage>(),
            CreatedAt: compatibleSeed?.CreatedAt,
            UpdatedAt: compatibleSeed?.UpdatedAt,
            Downloads: compatibleSeed?.Downloads,
            Endorsements: compatibleSeed?.Endorsements,
            Rating: compatibleSeed?.Rating,
            Dependencies: compatibleSeed?.Dependencies ?? Array.Empty<CatalogDependency>(),
            SourceUrl: sourceUrl,
            Files: compatibleSeed?.Files ?? Array.Empty<CatalogModFile>(),
            ProviderMetadata: metadata);
    }

    public static IReadOnlyList<NexusV3ModFileHeader> NormalizeModFileHeaders(
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);

        var data = RequiredObject(document.RootElement, "data", "Nexus v3 mod-files response");
        var modFiles = RequiredArray(data, "mod_files", "Nexus v3 mod-files response");
        var normalized = new List<NexusV3ModFileHeader>();

        foreach (var item in modFiles.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw SchemaError("Nexus v3 mod-file entry must be an object.");

            normalized.Add(new NexusV3ModFileHeader(
                Id: RequiredString(item, "id", "Nexus v3 mod-file entry"),
                Name: RequiredString(item, "name", "Nexus v3 mod-file entry"),
                IsActive: RequiredBoolean(item, "is_active", "Nexus v3 mod-file entry"),
                LastFileUploadedAt: RequiredNullableDateTimeOffset(
                    item,
                    "last_file_uploaded_at",
                    "Nexus v3 mod-file entry"),
                VersionsCount: RequiredInt32(item, "versions_count", "Nexus v3 mod-file entry"),
                ArchivedCount: RequiredInt32(item, "archived_count", "Nexus v3 mod-file entry"),
                RemovedCount: RequiredInt32(item, "removed_count", "Nexus v3 mod-file entry")));
        }

        return normalized;
    }

    public static IReadOnlyList<CatalogModFile> NormalizeModFileVersions(
        JsonDocument document,
        string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);

        var data = RequiredObject(document.RootElement, "data", "Nexus v3 mod-file versions response");
        var versions = RequiredArray(data, "versions", "Nexus v3 mod-file versions response");
        var normalized = new List<CatalogModFile>();

        foreach (var item in versions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw SchemaError("Nexus v3 mod-file version entry must be an object.");

            var versionId = RequiredString(item, "id", "Nexus v3 mod-file version entry");
            var file = RequiredObject(item, "file", "Nexus v3 mod-file version entry");
            var persistentFileId = RequiredString(file, "id", "Nexus v3 mod-file version file");
            var persistentFileName = RequiredString(file, "name", "Nexus v3 mod-file version file");
            var gameScopedVersionId = RequiredString(
                item,
                "game_scoped_id",
                "Nexus v3 mod-file version entry");
            var name = RequiredString(item, "name", "Nexus v3 mod-file version entry");
            var version = RequiredString(item, "version", "Nexus v3 mod-file version entry");
            var category = ParseFileCategory(
                RequiredString(item, "category", "Nexus v3 mod-file version entry"));
            var uploadedAt = RequiredDateTimeOffset(
                item,
                "uploaded_at",
                "Nexus v3 mod-file version entry");
            var isPrimary = OptionalBoolean(item, "is_primary") ?? false;
            var metadata = JsonSerializer.Serialize(new NexusV3FileMetadata(
                persistentFileId,
                persistentFileName,
                gameScopedVersionId,
                isPrimary));

            normalized.Add(new CatalogModFile(
                ProviderId: ProviderId,
                ProviderModId: providerModId.Trim(),
                ProviderFileId: versionId,
                Name: name,
                FileName: name,
                Category: category,
                Version: version,
                SizeBytes: null,
                Description: persistentFileName,
                UploadedAt: uploadedAt,
                Required: false,
                Recommended: isPrimary,
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: metadata));
        }

        return normalized;
    }

    public static string GetGlobalModId(CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(mod);

        if (!string.Equals(mod.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Catalog mod is not a Nexus mod.", nameof(mod));
        if (string.IsNullOrWhiteSpace(mod.ProviderMetadata))
            throw new InvalidDataException("Nexus catalog mod is missing global mod identity metadata.");

        try
        {
            var metadata = JsonSerializer.Deserialize<NexusV3ModMetadata>(mod.ProviderMetadata);
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.GlobalModId))
                throw new InvalidDataException("Nexus catalog mod has invalid global mod identity metadata.");

            return metadata.GlobalModId;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Nexus catalog mod has malformed provider metadata.", ex);
        }
    }

    public static string RequireGameDomain(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);

        if (string.IsNullOrWhiteSpace(game.NexusGameDomain))
            throw new NotSupportedException("The selected game does not define a Nexus Mods domain.");

        return game.NexusGameDomain.Trim().Trim('/');
    }

    private static bool IsCompatibleSeed(
        CatalogMod? seed,
        GameProfile game,
        string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return seed is not null
            && string.Equals(seed.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(seed.ProviderModId, providerModId, StringComparison.Ordinal)
            && string.Equals(seed.GameId, game.Id, StringComparison.Ordinal);
    }

    private static Uri BuildSourceUri(string gameDomain, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var domain = Uri.EscapeDataString(gameDomain);
        var modId = Uri.EscapeDataString(providerModId);
        return new Uri(
            "https://www.nexusmods.com/" + domain + "/mods/" + modId,
            UriKind.Absolute);
    }

    private static string ParseProviderModId(Uri sourceUri, string expectedGameDomain)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!IsNexusHost(sourceUri.Host))
            throw SchemaError("Nexus v3 trending mod_page_url has an unexpected host.");

        var segments = sourceUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length != 3
            || !string.Equals(segments[0], expectedGameDomain, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "mods", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[2]))
        {
            throw SchemaError("Nexus v3 trending mod_page_url does not match the expected game/mod path.");
        }

        return Uri.UnescapeDataString(segments[2]);
    }

    private static bool IsNexusHost(string host)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Equals(host, "nexusmods.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "www.nexusmods.com", StringComparison.OrdinalIgnoreCase);
    }

    private static CatalogFileCategory ParseFileCategory(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value switch
        {
            "main" => CatalogFileCategory.Main,
            "update" => CatalogFileCategory.Update,
            "optional" => CatalogFileCategory.Optional,
            "old_version" => CatalogFileCategory.OldVersion,
            "miscellaneous" => CatalogFileCategory.Miscellaneous,
            "removed" => CatalogFileCategory.Removed,
            "archived" => CatalogFileCategory.Archived,
            "unknown" => CatalogFileCategory.Unknown,
            _ => throw SchemaError("Nexus v3 returned an unrecognized mod-file category.")
        };
    }

    private static JsonElement RequiredObject(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind != JsonValueKind.Object)
            throw SchemaError(context + " property '" + propertyName + "' must be an object.");
        return value;
    }

    private static JsonElement RequiredArray(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind != JsonValueKind.Array)
            throw SchemaError(context + " property '" + propertyName + "' must be an array.");
        return value;
    }

    private static JsonElement RequiredProperty(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var value))
        {
            throw SchemaError(context + " is missing required property '" + propertyName + "'.");
        }

        return value;
    }

    private static string RequiredString(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind != JsonValueKind.String)
            throw SchemaError(context + " property '" + propertyName + "' must be a string.");

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw SchemaError(context + " property '" + propertyName + "' must not be empty.");

        return text;
    }

    private static string? OptionalString(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
            throw SchemaError("Optional Nexus v3 property '" + propertyName + "' must be a string or null.");

        return value.GetString();
    }

    private static bool RequiredBoolean(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw SchemaError(context + " property '" + propertyName + "' must be a boolean.");

        return value.GetBoolean();
    }

    private static bool? OptionalBoolean(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw SchemaError("Optional Nexus v3 property '" + propertyName + "' must be a boolean or null.");

        return value.GetBoolean();
    }

    private static int RequiredInt32(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result < 0)
            throw SchemaError(context + " property '" + propertyName + "' must be a non-negative integer.");

        return result;
    }

    private static DateTimeOffset RequiredDateTimeOffset(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(
                value.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var result))
        {
            throw SchemaError(context + " property '" + propertyName + "' must be an ISO-8601 date-time.");
        }

        return result;
    }

    private static DateTimeOffset? RequiredNullableDateTimeOffset(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredProperty(parent, propertyName, context);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(
                value.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var result))
        {
            throw SchemaError(context + " property '" + propertyName + "' must be an ISO-8601 date-time or null.");
        }

        return result;
    }

    private static Uri RequiredHttpsUri(
        JsonElement parent,
        string propertyName,
        string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = RequiredString(parent, propertyName, context);
        return ParseHttpsUri(value, context + " property '" + propertyName + "'");
    }

    private static Uri? OptionalHttpsUri(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = OptionalString(parent, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParseHttpsUri(value, "Optional Nexus v3 property '" + propertyName + "'");
    }

    private static Uri ParseHttpsUri(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw SchemaError(context + " must be an absolute HTTPS URI.");
        }

        return uri;
    }

    private static InvalidDataException SchemaError(string message)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new InvalidDataException(message);
    }
}

public sealed class NexusV3CatalogAdapter
{
    private readonly NexusV3Transport transport;
    private readonly NexusV3Credential credential;

    public NexusV3CatalogAdapter(
        NexusV3Transport transport,
        NexusV3Credential credential)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(credential);
        this.transport = transport;
        this.credential = credential;
    }

    public async Task<IReadOnlyList<CatalogMod>> GetTrendingAsync(
        GameProfile game,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var domain = NexusV3CatalogNormalizer.RequireGameDomain(game);
        using var response = await transport.GetTrendingModsAsync(domain, ct: ct).ConfigureAwait(false);
        var document = RequireDocument(response, "trending mods");
        return NexusV3CatalogNormalizer.NormalizeTrending(document, game);
    }

    public async Task<CatalogMod> GetModAsync(
        GameProfile game,
        string providerModId,
        CatalogMod? seed = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);

        var domain = NexusV3CatalogNormalizer.RequireGameDomain(game);
        using var response = await transport
            .GetModAsync(domain, providerModId, credential, ct: ct)
            .ConfigureAwait(false);
        var document = RequireDocument(response, "mod details");
        return NexusV3CatalogNormalizer.NormalizeMod(document, game, seed);
    }

    public async Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mod = await GetModAsync(game, providerModId, ct: ct).ConfigureAwait(false);
        return await GetModFilesAsync(game, mod, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
        GameProfile game,
        CatalogMod mod,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);

        if (!string.Equals(mod.GameId, game.Id, StringComparison.Ordinal))
            throw new ArgumentException("Catalog mod does not belong to the selected game.", nameof(mod));

        CatalogMod hydrated = mod;
        if (string.IsNullOrWhiteSpace(mod.ProviderMetadata))
            hydrated = await GetModAsync(game, mod.ProviderModId, mod, ct).ConfigureAwait(false);

        var globalModId = NexusV3CatalogNormalizer.GetGlobalModId(hydrated);
        using var headersResponse = await transport
            .GetModFilesAsync(globalModId, credential, ct: ct)
            .ConfigureAwait(false);
        var headersDocument = RequireDocument(headersResponse, "mod files");
        var headers = NexusV3CatalogNormalizer.NormalizeModFileHeaders(headersDocument);
        var files = new List<CatalogModFile>();

        foreach (var header in headers)
        {
            ct.ThrowIfCancellationRequested();
            using var versionsResponse = await transport
                .GetModFileVersionsAsync(header.Id, credential, ct: ct)
                .ConfigureAwait(false);
            var versionsDocument = RequireDocument(versionsResponse, "mod-file versions");
            files.AddRange(NexusV3CatalogNormalizer.NormalizeModFileVersions(
                versionsDocument,
                hydrated.ProviderModId));
        }

        return files;
    }

    private static JsonDocument RequireDocument(
        NexusV3TransportResponse response,
        string operation)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return response.Document
            ?? throw new InvalidDataException("Nexus v3 returned no JSON document for " + operation + ".");
    }
}
