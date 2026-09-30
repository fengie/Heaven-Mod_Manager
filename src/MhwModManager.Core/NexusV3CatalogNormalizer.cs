using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record NexusV3NormalizedModDetails(CatalogMod Mod, string GlobalModId);

public static class NexusV3CatalogNormalizer
{
    public const string ProviderId = "nexus";

    public static IReadOnlyList<CatalogMod> NormalizeTrendingMods(
        string gameId,
        string gameDomain,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDomain);
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var mods = RequireArray(data, "mods");
        var results = new List<CatalogMod>(mods.GetArrayLength());

        foreach (var item in mods.EnumerateArray())
        {
            _ = ReadRequiredString(item, "name");
            var sourceUrl = ReadRequiredString(item, "mod_page_url");
            var providerModId = ReadProviderModId(item, sourceUrl, gameDomain);
            results.Add(NormalizeMod(
                item,
                gameId,
                gameDomain,
                providerModId,
                sourceUrl,
                files: Array.Empty<CatalogModFile>()));
        }

        return results;
    }

    public static CatalogMod NormalizeModDetails(
        string gameId,
        string gameDomain,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return NormalizeModDetailsWithIdentity(gameId, gameDomain, document).Mod;
    }

    public static NexusV3NormalizedModDetails NormalizeModDetailsWithIdentity(
        string gameId,
        string gameDomain,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDomain);
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var providerModId = ReadRequiredString(data, "game_scoped_id");
        var globalModId = ReadRequiredString(data, "id");
        var nexusGameId = ReadRequiredString(data, "game_id");
        var sourceUrl = BuildSourceUrl(gameDomain, providerModId);
        var mod = NormalizeMod(
            data,
            gameId,
            gameDomain,
            providerModId,
            sourceUrl,
            files: Array.Empty<CatalogModFile>(),
            requiredGameId: nexusGameId);

        return new NexusV3NormalizedModDetails(mod, globalModId);
    }

    public static IReadOnlyList<CatalogModFile> NormalizeModFiles(
        string providerModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var files = RequireArray(data, "mod_files");
        var results = new List<CatalogModFile>(files.GetArrayLength());

        foreach (var item in files.EnumerateArray())
        {
            var fileId = ReadRequiredString(item, "id");
            var name = ReadRequiredString(item, "name");
            var fileName = ReadOptionalString(item, "file_name") ?? name;
            var category = ParseCategory(
                ReadOptionalString(item, "category_name")
                ?? ReadOptionalString(item, "category"));
            var uploadedAt = ReadOptionalTimestamp(item, "last_file_uploaded_at")
                ?? ReadOptionalTimestamp(item, "uploaded_at");

            results.Add(new CatalogModFile(
                ProviderId,
                providerModId,
                fileId,
                name,
                fileName,
                category,
                Version: ReadOptionalString(item, "version"),
                SizeBytes: ReadOptionalInt64(item, "size_bytes") ?? ReadOptionalInt64(item, "size"),
                Description: ReadOptionalString(item, "description"),
                UploadedAt: uploadedAt,
                Required: ReadOptionalBoolean(item, "required") ?? false,
                Recommended: ReadOptionalBoolean(item, "recommended") ?? false,
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: BuildFileMetadata(item)));
        }

        return results;
    }

    public static IReadOnlyList<CatalogModFile> NormalizeModFileVersions(
        string providerModId,
        string modFileId,
        string modFileName,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modFileName);
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var versions = RequireArray(data, "versions");
        var results = new List<CatalogModFile>(versions.GetArrayLength());

        foreach (var item in versions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Nexus v3 file-version payload contains a non-object version.");

            var versionId = ReadRequiredString(item, "id");
            var nestedFile = RequireObject(item, "file");
            var nestedFileId = ReadRequiredString(nestedFile, "id");
            _ = ReadRequiredString(nestedFile, "name");
            if (!nestedFileId.Equals(modFileId, StringComparison.Ordinal))
                throw new InvalidDataException("Nexus v3 file-version payload references an unexpected persistent mod-file id.");

            var position = ReadRequiredString(item, "position");
            if (!decimal.TryParse(
                position,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _))
            {
                throw new InvalidDataException("Nexus v3 file-version property 'position' is not a valid decimal.");
            }

            var gameScopedFileId = ReadRequiredString(item, "game_scoped_id");
            var name = ReadRequiredString(item, "name");
            var version = ReadRequiredString(item, "version");
            var category = ParseVersionCategory(ReadRequiredString(item, "category"));
            var uploadedAt = ReadOptionalTimestamp(item, "uploaded_at")
                ?? throw new InvalidDataException("Nexus v3 file-version property 'uploaded_at' is required.");

            var metadata = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["mod_file_id"] = modFileId.Trim(),
                ["mod_file_name"] = modFileName.Trim(),
                ["game_scoped_file_id"] = gameScopedFileId
            });

            results.Add(new CatalogModFile(
                ProviderId,
                providerModId.Trim(),
                versionId,
                name,
                name,
                category,
                Version: version,
                SizeBytes: ReadOptionalInt64(item, "size_bytes") ?? ReadOptionalInt64(item, "size"),
                Description: ReadOptionalString(item, "description"),
                UploadedAt: uploadedAt,
                Required: false,
                Recommended: ReadOptionalBoolean(item, "is_primary") ?? false,
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: metadata));
        }

        return results;
    }

    private static CatalogMod NormalizeMod(
        JsonElement item,
        string gameId,
        string gameDomain,
        string providerModId,
        string sourceUrl,
        IReadOnlyList<CatalogModFile> files,
        string? requiredGameId = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = ReadOptionalString(item, "name") ?? $"Nexus Mod {providerModId}";
        var summary = ReadOptionalString(item, "summary") ?? string.Empty;
        var description = ReadOptionalString(item, "description") ?? summary;
        var author = ReadOptionalString(item, "author") ?? "Unknown";
        var thumbnail = ReadOptionalString(item, "thumbnail_url")
            ?? ReadOptionalString(item, "picture_url");

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId,
            gameId,
            name,
            summary,
            description,
            author,
            ReadOptionalString(item, "version"),
            ReadOptionalString(item, "category"),
            ReadStringArray(item, "tags"),
            thumbnail,
            Array.Empty<CatalogImage>(),
            ReadOptionalTimestamp(item, "created_at"),
            ReadOptionalTimestamp(item, "updated_at"),
            ReadOptionalInt64(item, "downloads"),
            ReadOptionalInt64(item, "endorsements"),
            ReadOptionalDouble(item, "rating"),
            Array.Empty<CatalogDependency>(),
            sourceUrl,
            files,
            BuildModMetadata(item, gameDomain, requiredGameId));
    }

    private static string ReadProviderModId(JsonElement item, string sourceUrl, string gameDomain)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Nexus v3 mod entry does not contain a trusted Nexus source URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3
            || !segments[0].Equals(gameDomain.Trim(), StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("mods", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[2]))
        {
            throw new InvalidDataException("Nexus v3 mod entry source URL does not match the requested game domain.");
        }

        var urlId = Uri.UnescapeDataString(segments[2]);
        var explicitId = ReadOptionalString(item, "game_scoped_id");
        if (!string.IsNullOrWhiteSpace(explicitId)
            && !explicitId.Equals(urlId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Nexus v3 mod entry contains conflicting game-scoped identities.");
        }

        return explicitId ?? urlId;
    }

    private static string BuildSourceUrl(string gameDomain, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (gameDomain.Contains('/') || gameDomain.Contains('\\'))
            throw new ArgumentException("Game domain must be one path segment.", nameof(gameDomain));

        return $"https://www.nexusmods.com/{Uri.EscapeDataString(gameDomain.Trim())}/mods/{Uri.EscapeDataString(providerModId.Trim())}";
    }

    private static string? BuildModMetadata(
        JsonElement item,
        string gameDomain,
        string? requiredGameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var globalId = ReadOptionalString(item, "id");
        if (globalId is null)
            return null;

        var metadata = new Dictionary<string, string>
        {
            ["game_domain"] = gameDomain,
            ["global_mod_id"] = globalId
        };
        if (!string.IsNullOrWhiteSpace(requiredGameId))
            metadata["game_id"] = requiredGameId;

        return JsonSerializer.Serialize(metadata);
    }

    private static string? BuildFileMetadata(JsonElement item)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var safe = new Dictionary<string, object?>();

        AddIfPresent(safe, "is_active", ReadOptionalBoolean(item, "is_active"));
        AddIfPresent(safe, "versions_count", ReadOptionalInt64(item, "versions_count"));
        AddIfPresent(safe, "archived_count", ReadOptionalInt64(item, "archived_count"));
        AddIfPresent(safe, "removed_count", ReadOptionalInt64(item, "removed_count"));

        return safe.Count == 0 ? null : JsonSerializer.Serialize(safe);
    }

    private static void AddIfPresent(
        Dictionary<string, object?> target,
        string key,
        object? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value is not null)
            target[key] = value;
    }

    private static CatalogFileCategory ParseVersionCategory(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value.Trim().ToLowerInvariant() switch
        {
            "main" => CatalogFileCategory.Main,
            "update" => CatalogFileCategory.Update,
            "optional" => CatalogFileCategory.Optional,
            "old_version" => CatalogFileCategory.OldVersion,
            "miscellaneous" => CatalogFileCategory.Miscellaneous,
            "removed" => CatalogFileCategory.Removed,
            "archived" => CatalogFileCategory.Archived,
            "unknown" => CatalogFileCategory.Unknown,
            _ => throw new InvalidDataException(
                $"Nexus v3 returned unrecognized file-version category '{value}'.")
        };
    }

    private static CatalogFileCategory ParseCategory(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return CatalogFileCategory.Unknown;

        return value.Trim()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToLowerInvariant() switch
        {
            "main" or "main_file" or "main_files" => CatalogFileCategory.Main,
            "optional" or "optional_file" or "optional_files" => CatalogFileCategory.Optional,
            "update" or "updates" => CatalogFileCategory.Update,
            "misc" or "miscellaneous" => CatalogFileCategory.Miscellaneous,
            "old" or "old_version" or "old_versions" => CatalogFileCategory.OldVersion,
            "archived" => CatalogFileCategory.Archived,
            "removed" => CatalogFileCategory.Removed,
            _ => CatalogFileCategory.Unknown
        };
    }

    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be an object.");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be an array.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(parent, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' is required.");
        return value;
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be a string.");

        return value.GetString();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return Array.Empty<string>();

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be an array.");

        var results = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must contain only strings.");

            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text);
        }

        return results;
    }

    private static DateTimeOffset? ReadOptionalTimestamp(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(parent, propertyName);
        if (value is null)
            return null;

        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' is not a valid timestamp.");
        }

        return parsed;
    }

    private static long? ReadOptionalInt64(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be an integer.");
    }

    private static double? ReadOptionalDouble(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be numeric.");
    }

    private static bool? ReadOptionalBoolean(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be boolean.")
        };
    }
}
