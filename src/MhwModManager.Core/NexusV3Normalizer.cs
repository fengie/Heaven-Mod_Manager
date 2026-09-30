using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record NexusV3NormalizedMod(CatalogMod Mod, string? GlobalModId);

public static class NexusV3Normalizer
{
    public const string ProviderId = "nexus";

    public static IReadOnlyList<CatalogMod> NormalizeTrending(JsonDocument document, string gameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var data = GetDataObject(document);
        if (!data.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Nexus Mods API v3 trending payload is missing the required data.mods array.");

        var result = new List<CatalogMod>(mods.GetArrayLength());
        foreach (var element in mods.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Nexus Mods API v3 trending payload contains a non-object mod entry.");

            var sourceUrl = GetRequiredString(element, "mod_page_url", "trending mod");
            var providerModId = GetOptionalString(element, "game_scoped_id")
                ?? ExtractGameScopedId(sourceUrl);

            result.Add(CreateCatalogMod(element, gameId, providerModId, sourceUrl, files: null));
        }

        return result;
    }

    public static NexusV3NormalizedMod NormalizeMod(
        JsonDocument document,
        string gameId,
        IReadOnlyList<CatalogModFile>? files = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var data = GetDataObject(document);
        var providerModId = GetRequiredString(data, "game_scoped_id", "mod detail");
        var globalModId = GetOptionalString(data, "id");
        var sourceUrl = GetOptionalString(data, "mod_page_url")
            ?? BuildSourceUrl(gameId, providerModId);

        return new NexusV3NormalizedMod(
            CreateCatalogMod(data, gameId, providerModId, sourceUrl, files),
            globalModId);
    }

    public static IReadOnlyList<CatalogModFile> NormalizeFiles(
        JsonDocument document,
        string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModId);

        var data = GetDataObject(document);
        if (!data.TryGetProperty("mod_files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Nexus Mods API v3 file payload is missing the required data.mod_files array.");

        var result = new List<CatalogModFile>(files.GetArrayLength());
        foreach (var element in files.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Nexus Mods API v3 file payload contains a non-object file entry.");

            var providerFileId = GetRequiredString(element, "id", "mod file");
            var name = GetRequiredString(element, "name", "mod file");
            var fileName = GetOptionalString(element, "file_name") ?? name;
            var category = ParseFileCategory(
                GetOptionalString(element, "category")
                ?? GetOptionalString(element, "category_name"));

            result.Add(new CatalogModFile(
                ProviderId,
                providerModId.Trim(),
                providerFileId,
                name,
                fileName,
                category,
                Version: GetOptionalString(element, "version"),
                SizeBytes: GetOptionalInt64(element, "size_in_bytes") ?? GetOptionalInt64(element, "size"),
                Description: GetOptionalString(element, "description"),
                UploadedAt: GetOptionalDateTimeOffset(element, "last_file_uploaded_at")
                    ?? GetOptionalDateTimeOffset(element, "uploaded_at"),
                Required: GetOptionalBoolean(element, "required") ?? false,
                Recommended: GetOptionalBoolean(element, "recommended") ?? false,
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: null));
        }

        return result;
    }

    private static CatalogMod CreateCatalogMod(
        JsonElement element,
        string gameId,
        string providerModId,
        string sourceUrl,
        IReadOnlyList<CatalogModFile>? files)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = GetRequiredString(element, "name", "mod");
        var summary = GetOptionalString(element, "summary") ?? string.Empty;
        var description = GetOptionalString(element, "description") ?? summary;
        var author = GetOptionalString(element, "author") ?? string.Empty;
        var thumbnail = GetOptionalString(element, "thumbnail_url")
            ?? GetOptionalString(element, "picture_url");

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId.Trim(),
            gameId.Trim(),
            name,
            summary,
            description,
            author,
            GetOptionalString(element, "version"),
            GetOptionalString(element, "category_name") ?? GetOptionalString(element, "category"),
            GetStringArray(element, "tags"),
            thumbnail,
            GetImages(element),
            GetOptionalDateTimeOffset(element, "created_at"),
            GetOptionalDateTimeOffset(element, "updated_at"),
            GetOptionalInt64(element, "downloads"),
            GetOptionalInt64(element, "endorsements"),
            GetOptionalDouble(element, "rating"),
            Array.Empty<CatalogDependency>(),
            sourceUrl,
            files ?? Array.Empty<CatalogModFile>(),
            ProviderMetadata: null);
    }

    private static JsonElement GetDataObject(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Nexus Mods API v3 payload is missing the required data object.");
        }

        return data;
    }

    private static string GetRequiredString(JsonElement element, string propertyName, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = GetOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Nexus Mods API v3 {context} is missing required string '{propertyName}'.");
        return value;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()?.Trim();

        if (value.ValueKind == JsonValueKind.Number)
            return value.GetRawText();

        throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' has an unexpected JSON type.");
    }

    private static long? GetOptionalInt64(JsonElement element, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' is not a valid integer.");
    }

    private static double? GetOptionalDouble(JsonElement element, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' is not a valid number.");
    }

    private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();

        throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' is not a valid boolean.");
    }

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = GetOptionalString(element, propertyName);
        if (value is null)
            return null;

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' is not a valid timestamp.");
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' is not an array.");

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Nexus Mods API v3 property '{propertyName}' contains a non-string item.");

            var text = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }

        return result;
    }

    private static IReadOnlyList<CatalogImage> GetImages(JsonElement element)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!element.TryGetProperty("images", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CatalogImage>();
        }

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Nexus Mods API v3 property 'images' is not an array.");

        var result = new List<CatalogImage>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var url = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(url))
                    result.Add(new CatalogImage(url));
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Nexus Mods API v3 images array contains an unsupported item.");

            var imageUrl = GetRequiredString(item, "url", "image");
            result.Add(new CatalogImage(
                imageUrl,
                GetOptionalString(item, "caption"),
                GetOptionalBoolean(item, "is_thumbnail") ?? false));
        }

        return result;
    }

    private static CatalogFileCategory ParseFileCategory(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value))
            return CatalogFileCategory.Unknown;

        return value.Trim().ToLowerInvariant() switch
        {
            "main" or "main file" => CatalogFileCategory.Main,
            "optional" or "optional file" => CatalogFileCategory.Optional,
            "update" or "update file" => CatalogFileCategory.Update,
            "miscellaneous" or "misc" => CatalogFileCategory.Miscellaneous,
            "old version" or "old_version" => CatalogFileCategory.OldVersion,
            "archived" => CatalogFileCategory.Archived,
            "removed" => CatalogFileCategory.Removed,
            _ => CatalogFileCategory.Unknown
        };
    }

    private static string ExtractGameScopedId(string sourceUrl)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Nexus Mods API v3 mod_page_url is not a valid Nexus HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index + 1 < segments.Length; index++)
        {
            if (segments[index].Equals("mods", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(segments[index + 1]))
            {
                return Uri.UnescapeDataString(segments[index + 1]);
            }
        }

        throw new InvalidDataException("Nexus Mods API v3 mod_page_url does not contain a game-scoped mod id.");
    }

    private static string BuildSourceUrl(string gameId, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return $"https://www.nexusmods.com/{Uri.EscapeDataString(gameId.Trim())}/mods/{Uri.EscapeDataString(providerModId.Trim())}";
    }
}
