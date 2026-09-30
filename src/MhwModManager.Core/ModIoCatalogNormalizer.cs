using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record ModIoAcquisitionFile(
    string ProviderModId,
    string ProviderFileId,
    Uri? DownloadUri,
    DateTimeOffset? ExpiresAt,
    string? UnavailableReason);

public static class ModIoCatalogNormalizer
{
    public const string ProviderId = ModIoCatalogPolicy.ProviderId;

    public static IReadOnlyList<CatalogMod> NormalizeMods(
        GameProfile game,
        int expectedGameId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGameId);

        var root = RequireObjectValue(document.RootElement, "mod.io mod list response");
        var data = RequireArray(root, "data");
        var result = new List<CatalogMod>(data.GetArrayLength());

        foreach (var item in data.EnumerateArray())
            result.Add(NormalizeModElement(game, expectedGameId, item));

        return result;
    }

    public static CatalogMod NormalizeMod(
        GameProfile game,
        int expectedGameId,
        string expectedProviderModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGameId);
        if (!TryParseProviderModId(expectedProviderModId, out var providerGameId, out var rawModId)
            || providerGameId != expectedGameId)
        {
            throw new ArgumentException(
                "mod.io provider mod id must match the expected game and mod identity.",
                nameof(expectedProviderModId));
        }

        var providerModId = BuildProviderModId(providerGameId, rawModId);
        var mod = NormalizeModElement(game, expectedGameId, document.RootElement);
        if (!mod.ProviderModId.Equals(providerModId, StringComparison.Ordinal))
            throw new InvalidDataException("mod.io mod detail returned an unexpected mod identity.");
        return mod;
    }

    public static IReadOnlyList<CatalogModFile> NormalizeModFiles(
        string expectedProviderModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        if (!TryParseProviderModId(expectedProviderModId, out var providerGameId, out var rawModId))
            throw new ArgumentException("mod.io provider mod id must include game and mod ids.", nameof(expectedProviderModId));
        var providerModId = BuildProviderModId(providerGameId, rawModId);
        var root = RequireObjectValue(document.RootElement, "mod.io modfile list response");
        var data = RequireArray(root, "data");

        var result = new List<CatalogModFile>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
            result.Add(NormalizeFileElement(providerModId, rawModId, item, recommended: false));

        return result;
    }

    public static ModIoAcquisitionFile NormalizeAcquisitionFile(
        string expectedProviderModId,
        string expectedProviderFileId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        if (!TryParseProviderModId(expectedProviderModId, out var providerGameId, out var rawModId))
            throw new ArgumentException("mod.io provider mod id must include game and mod ids.", nameof(expectedProviderModId));
        var providerModId = BuildProviderModId(providerGameId, rawModId);
        var providerFileId = NormalizePositiveId(expectedProviderFileId, nameof(expectedProviderFileId));
        var root = RequireObjectValue(document.RootElement, "mod.io modfile response");

        var actualFileId = ReadRequiredPositiveId(root, "id");
        var actualModId = ReadRequiredPositiveId(root, "mod_id");
        if (!actualFileId.Equals(providerFileId, StringComparison.Ordinal)
            || !actualModId.Equals(rawModId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("mod.io modfile response returned an unexpected identity.");
        }

        var virusStatus = ReadRequiredInt64(root, "virus_status");
        var virusPositive = ReadRequiredInt64(root, "virus_positive");

        if (virusStatus != 1)
        {
            return new ModIoAcquisitionFile(
                providerModId,
                providerFileId,
                null,
                null,
                "mod.io has not reported a completed clean virus scan for this file.");
        }

        if (virusPositive != 0)
        {
            return new ModIoAcquisitionFile(
                providerModId,
                providerFileId,
                null,
                null,
                "mod.io reported this file as unsafe or potentially harmful.");
        }

        var download = RequireObject(root, "download");
        var binaryUrl = ReadRequiredString(download, "binary_url");
        var downloadUri = ValidateDownloadUri(binaryUrl);
        var expiresAt = ReadOptionalEpoch(download, "date_expires");

        return new ModIoAcquisitionFile(
            providerModId,
            providerFileId,
            downloadUri,
            expiresAt,
            null);
    }

    private static CatalogMod NormalizeModElement(
        GameProfile game,
        int expectedGameId,
        JsonElement element)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = RequireObjectValue(element, "mod.io mod");

        var rawModId = ReadRequiredPositiveId(root, "id");
        var gameId = ReadRequiredInt64(root, "game_id");
        if (gameId != expectedGameId)
            throw new InvalidDataException("mod.io mod belongs to a different game.");

        var providerModId = BuildProviderModId(expectedGameId, rawModId);

        var status = ReadRequiredInt64(root, "status");
        var visible = ReadRequiredInt64(root, "visible");
        if (status != 1 || visible != 1)
            throw new InvalidDataException("mod.io returned a mod that is not accepted and public.");

        var name = ReadRequiredString(root, "name");
        var summary = ReadOptionalString(root, "summary") ?? string.Empty;
        var description = ReadOptionalString(root, "description_plaintext") ?? summary;
        var sourceUrl = ReadRequiredString(root, "profile_url");
        ValidateProfileUri(sourceUrl);

        var author = "Unknown author";
        if (root.TryGetProperty("submitted_by", out var submittedBy)
            && submittedBy.ValueKind == JsonValueKind.Object)
        {
            author = ReadOptionalString(submittedBy, "username") ?? author;
        }

        var tags = ReadTags(root);
        var screenshots = ReadScreenshots(root);
        var thumbnail = ReadThumbnail(root);

        CatalogModFile[] files = [];
        string? version = null;
        if (root.TryGetProperty("modfile", out var modfile)
            && modfile.ValueKind == JsonValueKind.Object
            && modfile.TryGetProperty("id", out _))
        {
            var normalized = NormalizeFileElement(providerModId, rawModId, modfile, recommended: true);
            files = [normalized];
            version = normalized.Version;
        }

        long? downloads = null;
        double? rating = null;
        if (root.TryGetProperty("stats", out var stats)
            && stats.ValueKind == JsonValueKind.Object)
        {
            downloads = ReadOptionalInt64(stats, "downloads_total");
            rating = ReadOptionalDouble(stats, "ratings_weighted_aggregate");
        }

        var metadata = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["modio_game_id"] = expectedGameId.ToString(CultureInfo.InvariantCulture)
        });

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId,
            game.Id,
            name,
            summary,
            description,
            author,
            version,
            Category: null,
            Tags: tags,
            Thumbnail: thumbnail,
            Screenshots: screenshots,
            CreatedAt: ReadOptionalEpoch(root, "date_added"),
            UpdatedAt: ReadOptionalEpoch(root, "date_updated"),
            Downloads: downloads,
            Endorsements: null,
            Rating: rating,
            Dependencies: Array.Empty<CatalogDependency>(),
            SourceUrl: sourceUrl,
            Files: files,
            ProviderMetadata: metadata);
    }

    private static CatalogModFile NormalizeFileElement(
        string expectedProviderModId,
        string expectedRawModId,
        JsonElement element,
        bool recommended)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = RequireObjectValue(element, "mod.io modfile");
        var providerFileId = ReadRequiredPositiveId(root, "id");
        var rawModId = ReadRequiredPositiveId(root, "mod_id");

        if (!rawModId.Equals(expectedRawModId, StringComparison.Ordinal))
            throw new InvalidDataException("mod.io modfile belongs to a different mod.");

        var fileName = ReadRequiredString(root, "filename");
        var version = ReadOptionalString(root, "version");
        var virusStatus = ReadRequiredInt64(root, "virus_status");
        var virusPositive = ReadRequiredInt64(root, "virus_positive");

        string? md5 = null;
        if (root.TryGetProperty("filehash", out var filehash)
            && filehash.ValueKind == JsonValueKind.Object)
        {
            md5 = ReadOptionalString(filehash, "md5");
        }

        var metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["virus_status"] = virusStatus,
            ["virus_positive"] = virusPositive,
            ["md5"] = md5
        });

        return new CatalogModFile(
            ProviderId,
            expectedProviderModId,
            providerFileId,
            fileName,
            fileName,
            CatalogFileCategory.Main,
            Version: version,
            SizeBytes: ReadOptionalInt64(root, "filesize"),
            Description: ReadOptionalString(root, "changelog"),
            UploadedAt: ReadOptionalEpoch(root, "date_added"),
            Required: false,
            Recommended: recommended,
            Dependencies: Array.Empty<CatalogDependency>(),
            ProviderMetadata: metadata);
    }

    private static IReadOnlyList<string> ReadTags(JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty("tags", out var tags)
            || tags.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<string>();
        }

        if (tags.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("mod.io tags must be an array.");

        var result = new List<string>();
        foreach (var item in tags.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("mod.io tag entries must be objects.");
            var name = ReadRequiredString(item, "name");
            if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
                result.Add(name);
        }

        return result;
    }

    private static IReadOnlyList<CatalogImage> ReadScreenshots(JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty("media", out var media)
            || media.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CatalogImage>();
        }

        if (media.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("mod.io media must be an object.");

        if (!media.TryGetProperty("images", out var images)
            || images.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CatalogImage>();
        }

        if (images.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("mod.io media images must be an array.");

        var result = new List<CatalogImage>();
        foreach (var item in images.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("mod.io media image entries must be objects.");

            var url = ReadRequiredString(item, "original");
            ValidateAssetUri(url);
            result.Add(new CatalogImage(url));
        }

        return result;
    }

    private static string? ReadThumbnail(JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty("logo", out var logo)
            || logo.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (logo.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("mod.io logo must be an object.");

        var url = ReadOptionalString(logo, "thumb_320x180")
            ?? ReadOptionalString(logo, "original");
        if (url is not null)
            ValidateAssetUri(url);
        return url;
    }

    private static JsonElement RequireObjectValue(JsonElement element, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context} must be an object.");
        return element;
    }

    private static JsonElement RequireObject(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"mod.io field '{propertyName}' must be an object.");
        return value;
    }

    private static JsonElement RequireArray(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"mod.io field '{propertyName}' must be an array.");
        return value;
    }

    private static string ReadRequiredPositiveId(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadRequiredInt64(root, propertyName);
        if (value <= 0)
            throw new InvalidDataException($"mod.io field '{propertyName}' must be a positive integer.");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static long ReadRequiredInt64(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw new InvalidDataException($"mod.io field '{propertyName}' must be an integer.");
        }

        return result;
    }

    private static long? ReadOptionalInt64(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
            throw new InvalidDataException($"mod.io field '{propertyName}' must be an integer when present.");
        return result;
    }

    private static double? ReadOptionalDouble(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
            throw new InvalidDataException($"mod.io field '{propertyName}' must be numeric when present.");
        return result;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"mod.io field '{propertyName}' must be a non-empty string.");
        return value;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"mod.io field '{propertyName}' must be a string when present.");
        return value.GetString();
    }

    private static DateTimeOffset? ReadOptionalEpoch(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalInt64(root, propertyName);
        if (value is null or <= 0) return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value.Value);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"mod.io field '{propertyName}' contains an invalid Unix timestamp.", ex);
        }
    }

    public static bool TryParseProviderModId(
        string providerModId,
        out int gameId,
        out string rawModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        gameId = 0;
        rawModId = string.Empty;
        if (string.IsNullOrWhiteSpace(providerModId))
            return false;

        var split = providerModId.IndexOf(':', StringComparison.Ordinal);
        if (split <= 0 || split == providerModId.Length - 1
            || providerModId.IndexOf(':', split + 1) >= 0)
        {
            return false;
        }

        if (!int.TryParse(
            providerModId[..split],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out gameId)
            || gameId <= 0)
        {
            gameId = 0;
            return false;
        }

        try
        {
            rawModId = NormalizePositiveId(providerModId[(split + 1)..], nameof(providerModId));
            return true;
        }
        catch (ArgumentException)
        {
            gameId = 0;
            rawModId = string.Empty;
            return false;
        }
    }

    private static string BuildProviderModId(int gameId, string rawModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        return $"{gameId.ToString(CultureInfo.InvariantCulture)}:{NormalizePositiveId(rawModId, nameof(rawModId))}";
    }

    private static string NormalizePositiveId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var id) || id <= 0)
        {
            throw new ArgumentException("mod.io IDs must be positive integers.", parameterName);
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static void ValidateProfileUri(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var uri = RequireHttpsUri(value, "mod.io profile URL");
        if (!uri.IdnHost.Equals("mod.io", StringComparison.OrdinalIgnoreCase)
            && !uri.IdnHost.EndsWith(".mod.io", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("mod.io profile URL points outside mod.io.");
        }
    }

    private static void ValidateAssetUri(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var uri = RequireHttpsUri(value, "mod.io asset URL");
        if (!IsAllowedHost(uri.IdnHost, "mod.io")
            && !IsAllowedHost(uri.IdnHost, "modcdn.io"))
        {
            throw new InvalidDataException("mod.io asset URL points outside approved mod.io CDN hosts.");
        }
    }

    private static Uri ValidateDownloadUri(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var uri = RequireHttpsUri(value, "mod.io download URL");
        if (!IsAllowedHost(uri.IdnHost, "mod.io")
            && !IsAllowedHost(uri.IdnHost, "modcdn.io")
            && !IsAllowedHost(uri.IdnHost, "modapi.io"))
        {
            throw new InvalidDataException("mod.io download URL points outside approved mod.io hosts.");
        }

        return uri;
    }

    private static Uri RequireHttpsUri(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException($"{context} must be an absolute HTTPS URL without user information.");
        }

        return uri;
    }

    private static bool IsAllowedHost(string host, string suffix)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
    }
}
