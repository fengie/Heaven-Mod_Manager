using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record NexusV3NormalizedMod(CatalogMod Mod, string GlobalModId);
public sealed record NexusV3ModFileGroup(string Id, string Name);

public static class NexusV3CatalogNormalizer
{
    public const string ProviderId = "nexus";

    public static IReadOnlyList<CatalogMod> NormalizeTrendingMods(
        string gameId,
        string gameDomain,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateGameIdentity(gameId, gameDomain);
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var mods = RequireArray(data, "mods");
        var result = new List<CatalogMod>(mods.GetArrayLength());

        foreach (var item in mods.EnumerateArray())
        {
            RequireObjectValue(item, "trending mod");
            var sourceUrl = ReadRequiredString(item, "mod_page_url");
            var providerModId = ExtractProviderModId(sourceUrl, gameDomain);
            result.Add(BuildMod(
                item,
                gameId,
                gameDomain,
                providerModId,
                sourceUrl,
                providerMetadata: null));
        }

        return result;
    }

    public static NexusV3NormalizedMod NormalizeModDetails(
        string gameId,
        string gameDomain,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ValidateGameIdentity(gameId, gameDomain);
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var globalModId = ReadRequiredString(data, "id");
        var providerModId = ReadRequiredString(data, "game_scoped_id");
        var sourceUrl = BuildSourceUrl(gameDomain, providerModId);
        var metadata = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["game_domain"] = gameDomain,
            ["global_mod_id"] = globalModId
        });

        return new NexusV3NormalizedMod(
            BuildMod(data, gameId, gameDomain, providerModId, sourceUrl, metadata),
            globalModId);
    }

    public static IReadOnlyList<NexusV3ModFileGroup> NormalizeModFileGroups(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);

        var data = RequireObject(document.RootElement, "data");
        var groups = RequireArray(data, "mod_files");
        var result = new List<NexusV3ModFileGroup>(groups.GetArrayLength());

        foreach (var item in groups.EnumerateArray())
        {
            RequireObjectValue(item, "mod file");
            result.Add(new NexusV3ModFileGroup(
                ReadRequiredString(item, "id"),
                ReadRequiredString(item, "name")));
        }

        return result;
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
        var result = new List<CatalogModFile>(versions.GetArrayLength());

        foreach (var item in versions.EnumerateArray())
        {
            RequireObjectValue(item, "mod file version");
            var versionId = ReadRequiredString(item, "id");
            var name = ReadRequiredString(item, "name");
            var category = ParseFileCategory(ReadRequiredString(item, "category"));
            var uploadedAt = ReadRequiredTimestamp(item, "uploaded_at");

            var metadata = new Dictionary<string, string>
            {
                ["mod_file_group_id"] = modFileId,
                ["mod_file_group_name"] = modFileName
            };
            var gameScopedFileId = ReadOptionalString(item, "game_scoped_id");
            if (!string.IsNullOrWhiteSpace(gameScopedFileId))
                metadata["game_scoped_file_id"] = gameScopedFileId;

            result.Add(new CatalogModFile(
                ProviderId,
                providerModId,
                versionId,
                name,
                ReadOptionalString(item, "file_name") ?? name,
                category,
                Version: ReadRequiredString(item, "version"),
                SizeBytes: ReadOptionalInt64(item, "size") ?? MultiplyKilobytes(ReadOptionalInt64(item, "size_kb")),
                Description: ReadOptionalString(item, "description"),
                UploadedAt: uploadedAt,
                Required: ReadOptionalBoolean(item, "required") ?? false,
                Recommended: ReadOptionalBoolean(item, "is_primary") ?? false,
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: JsonSerializer.Serialize(metadata)));
        }

        return result;
    }

    public static CatalogFileCategory ParseFileCategory(string? value)
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
            "update" or "updates" or "update_file" or "update_files" => CatalogFileCategory.Update,
            "misc" or "miscellaneous" => CatalogFileCategory.Miscellaneous,
            "old" or "old_version" or "old_versions" => CatalogFileCategory.OldVersion,
            "archived" => CatalogFileCategory.Archived,
            "removed" => CatalogFileCategory.Removed,
            _ => CatalogFileCategory.Unknown
        };
    }

    private static CatalogMod BuildMod(
        JsonElement item,
        string gameId,
        string gameDomain,
        string providerModId,
        string sourceUrl,
        string? providerMetadata)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var summary = ReadOptionalString(item, "summary") ?? string.Empty;
        var thumbnail = ReadOptionalString(item, "thumbnail_url")
            ?? ReadOptionalString(item, "picture_url");
        var screenshots = string.IsNullOrWhiteSpace(thumbnail)
            ? Array.Empty<CatalogImage>()
            : [new CatalogImage(thumbnail, IsThumbnail: true)];

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId,
            gameId,
            ReadRequiredString(item, "name"),
            summary,
            ReadOptionalString(item, "description") ?? summary,
            ReadOptionalString(item, "author")
                ?? ReadNestedOptionalString(item, "uploader", "name")
                ?? ReadOptionalString(item, "uploaded_by")
                ?? "Unknown author",
            ReadOptionalString(item, "version"),
            ReadOptionalString(item, "category_name") ?? ReadOptionalString(item, "category"),
            ReadStringArray(item, "tags"),
            thumbnail,
            screenshots,
            ReadOptionalTimestamp(item, "created_at") ?? ReadOptionalTimestamp(item, "created_timestamp"),
            ReadOptionalTimestamp(item, "updated_at") ?? ReadOptionalTimestamp(item, "updated_timestamp"),
            ReadOptionalInt64(item, "downloads") ?? ReadOptionalInt64(item, "download_count"),
            ReadOptionalInt64(item, "endorsements") ?? ReadOptionalInt64(item, "endorsement_count"),
            ReadOptionalDouble(item, "rating"),
            Array.Empty<CatalogDependency>(),
            sourceUrl,
            Array.Empty<CatalogModFile>(),
            providerMetadata);
    }

    private static void ValidateGameIdentity(string gameId, string gameDomain)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDomain);
        if (gameDomain.Contains('/') || gameDomain.Contains('\\'))
            throw new ArgumentException("Game domain must be one path segment.", nameof(gameDomain));
    }

    private static string ExtractProviderModId(string sourceUrl, string gameDomain)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Nexus v3 mod_page_url is not a trusted Nexus HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 3
            || !segments[0].Equals(gameDomain, StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("mods", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[2]))
        {
            throw new InvalidDataException("Nexus v3 mod_page_url does not contain the expected game-scoped mod identity.");
        }

        return Uri.UnescapeDataString(segments[2]);
    }

    private static string BuildSourceUrl(string gameDomain, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return $"https://www.nexusmods.com/{Uri.EscapeDataString(gameDomain.Trim())}/mods/{Uri.EscapeDataString(providerModId.Trim())}";
    }

    private static void RequireObjectValue(JsonElement value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Nexus v3 {context} entry must be an object.");
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
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be a string.");
        return value.GetString();
    }

    private static string? ReadNestedOptionalString(
        JsonElement parent,
        string objectProperty,
        string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(objectProperty, out var nested)
            || nested.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (nested.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Nexus v3 response property '{objectProperty}' must be an object.");
        return ReadOptionalString(nested, propertyName);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be an array.");

        var result = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must contain only strings.");
            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }

        return result;
    }

    private static DateTimeOffset? ReadOptionalTimestamp(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(parent, propertyName);
        if (value is null)
            return null;
        return ParseTimestamp(value, propertyName);
    }

    private static DateTimeOffset ReadRequiredTimestamp(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadRequiredString(parent, propertyName);
        return ParseTimestamp(value, propertyName);
    }

    private static DateTimeOffset ParseTimestamp(string value, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
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
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be an integer.");
    }

    private static double? ReadOptionalDouble(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be numeric.");
    }

    private static bool? ReadOptionalBoolean(JsonElement parent, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"Nexus v3 response property '{propertyName}' must be boolean.")
        };
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
            throw new InvalidDataException("Nexus v3 file size exceeded the supported range.", ex);
        }
    }
}
