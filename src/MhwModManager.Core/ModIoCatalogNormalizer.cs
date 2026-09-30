using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record ModIoResolvedDownload(
    Uri DownloadUri,
    DateTimeOffset? ExpiresAt,
    int VirusStatus,
    int VirusPositive);

public static class ModIoCatalogNormalizer
{
    public const string ProviderId = "modio";

    public static IReadOnlyList<CatalogMod> NormalizeMods(
        GameProfile game,
        int expectedGameId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        if (expectedGameId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedGameId));

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("mod.io list response must be an object.");

        var data = RequireArray(root, "data");
        var result = new List<CatalogMod>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
            result.Add(NormalizeModObject(game, expectedGameId, item, Array.Empty<CatalogModFile>()));
        return result;
    }

    public static CatalogMod NormalizeMod(
        GameProfile game,
        int expectedGameId,
        JsonDocument document,
        IReadOnlyList<CatalogModFile> files)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        return NormalizeModObject(game, expectedGameId, document.RootElement, files);
    }

    public static IReadOnlyList<CatalogModFile> NormalizeFiles(
        string providerModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        var modId = NormalizePositiveId(providerModId, nameof(providerModId));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("mod.io file-list response must be an object.");
        var data = RequireArray(root, "data");
        return data.EnumerateArray().Select(item => NormalizeFile(modId, item)).ToArray();
    }

    public static ModIoResolvedDownload NormalizeDownload(
        string expectedModId,
        string expectedFileId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        var modId = NormalizePositiveId(expectedModId, nameof(expectedModId));
        var fileId = NormalizePositiveId(expectedFileId, nameof(expectedFileId));
        var root = document.RootElement;
        RequireObject(root, "modfile");

        if (ReadPositiveId(root, "id") != fileId)
            throw new InvalidDataException("mod.io modfile id does not match the requested file.");
        if (ReadPositiveId(root, "mod_id") != modId)
            throw new InvalidDataException("mod.io modfile belongs to a different mod.");

        var virusStatus = ReadRequiredInt(root, "virus_status");
        var virusPositive = ReadRequiredInt(root, "virus_positive");
        var download = RequireObjectProperty(root, "download");
        var url = ReadRequiredString(download, "binary_url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("api.mod.io", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".modapi.io", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".modcdn.io", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("mod.io download URL is not a trusted HTTPS mod.io URL.");
        }

        var expires = ReadOptionalEpoch(download, "date_expires");
        return new ModIoResolvedDownload(uri, expires, virusStatus, virusPositive);
    }

    private static CatalogMod NormalizeModObject(
        GameProfile game,
        int expectedGameId,
        JsonElement item,
        IReadOnlyList<CatalogModFile> files)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        RequireObject(item, "mod");

        var providerModId = ReadPositiveId(item, "id");
        var gameId = ReadRequiredInt(item, "game_id");
        if (gameId != expectedGameId)
            throw new InvalidDataException("mod.io mod belongs to a different game.");

        var status = ReadRequiredInt(item, "status");
        var visible = ReadRequiredInt(item, "visible");
        if (status != 1 || visible != 1)
            throw new InvalidDataException("mod.io returned a mod that is not accepted and public.");

        var name = ReadRequiredString(item, "name");
        var summary = ReadOptionalString(item, "summary") ?? string.Empty;
        var description = ReadOptionalString(item, "description_plaintext")
            ?? ReadOptionalString(item, "description")
            ?? summary;
        var author = "Unknown author";
        if (item.TryGetProperty("submitted_by", out var submittedBy)
            && submittedBy.ValueKind == JsonValueKind.Object)
            author = ReadOptionalString(submittedBy, "username") ?? author;

        var sourceUrl = ReadRequiredString(item, "profile_url");
        ValidateProfileUrl(sourceUrl);

        string? thumbnail = null;
        if (item.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object
            && media.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
        {
            thumbnail = ReadOptionalString(logo, "thumb_320x180")
                ?? ReadOptionalString(logo, "thumb_640x360")
                ?? ReadOptionalString(logo, "original");
            if (thumbnail is not null) ValidateAssetUrl(thumbnail);
        }

        var tags = Array.Empty<string>();
        if (item.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
        {
            tags = tagArray.EnumerateArray()
                .Select(tag => tag.ValueKind == JsonValueKind.Object ? ReadOptionalString(tag, "name") : null)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        long? downloads = null;
        if (item.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
            downloads = ReadOptionalInt64(stats, "downloads_total");

        var version = files
            .OrderByDescending(file => file.UploadedAt)
            .Select(file => file.Version)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, $"{expectedGameId}:{providerModId}"),
            ProviderId,
            $"{expectedGameId}:{providerModId}",
            game.Id,
            name,
            summary,
            description,
            author,
            version,
            Category: null,
            Tags: tags,
            Thumbnail: thumbnail,
            Screenshots: thumbnail is null ? Array.Empty<CatalogImage>() : [new CatalogImage(thumbnail, IsThumbnail: true)],
            CreatedAt: ReadOptionalEpoch(item, "date_added"),
            UpdatedAt: ReadOptionalEpoch(item, "date_updated"),
            Downloads: downloads,
            Endorsements: null,
            Rating: null,
            Dependencies: Array.Empty<CatalogDependency>(),
            SourceUrl: sourceUrl,
            Files: files,
            ProviderMetadata: null);
    }

    private static CatalogModFile NormalizeFile(string expectedModId, JsonElement item)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RequireObject(item, "modfile");
        var fileId = ReadPositiveId(item, "id");
        var modId = ReadPositiveId(item, "mod_id");
        if (modId != expectedModId)
            throw new InvalidDataException("mod.io modfile belongs to a different mod.");

        var virusPositive = ReadRequiredInt(item, "virus_positive");
        if (virusPositive == 1)
            throw new InvalidDataException("mod.io marked a modfile as containing a threat.");

        var filename = ReadRequiredString(item, "filename");
        return new CatalogModFile(
            ProviderId,
            expectedModId,
            fileId,
            string.IsNullOrWhiteSpace(ReadOptionalString(item, "version")) ? filename : ReadOptionalString(item, "version")!,
            filename,
            CatalogFileCategory.Main,
            Version: ReadOptionalString(item, "version"),
            SizeBytes: ReadOptionalInt64(item, "filesize"),
            Description: ReadOptionalString(item, "changelog"),
            UploadedAt: ReadOptionalEpoch(item, "date_added"),
            Dependencies: Array.Empty<CatalogDependency>(),
            ProviderMetadata: null);
    }

    public static bool TryParseProviderModId(string providerModId, out int gameId, out string modId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        gameId = 0;
        modId = string.Empty;
        if (string.IsNullOrWhiteSpace(providerModId)) return false;
        var split = providerModId.IndexOf(':');
        if (split <= 0 || split == providerModId.Length - 1) return false;
        if (!int.TryParse(providerModId[..split], NumberStyles.None, CultureInfo.InvariantCulture, out gameId)
            || gameId <= 0)
            return false;
        try
        {
            modId = NormalizePositiveId(providerModId[(split + 1)..], nameof(providerModId));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateProfileUrl(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("mod.io", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".mod.io", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("mod.io profile URL is not a trusted HTTPS mod.io URL.");
    }

    private static void ValidateAssetUrl(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.EndsWith(".modcdn.io", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("mod.io", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("mod.io asset URL is not a trusted HTTPS mod.io URL.");
    }

    private static JsonElement RequireArray(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"mod.io response property '{property}' must be an array.");
        return value;
    }

    private static JsonElement RequireObject(JsonElement root, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"mod.io {context} must be an object.");
        return root;
    }

    private static JsonElement RequireObjectProperty(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"mod.io response property '{property}' must be an object.");
        return value;
    }

    private static string ReadPositiveId(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadRequiredInt64(root, property);
        if (value <= 0) throw new InvalidDataException($"mod.io response property '{property}' must be positive.");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizePositiveId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("mod.io ID must be a positive integer.", parameterName);
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static string ReadRequiredString(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(root, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"mod.io response property '{property}' is required.");
        return value;
    }

    private static string? ReadOptionalString(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"mod.io response property '{property}' must be a string.");
        return value.GetString()?.Trim();
    }

    private static int ReadRequiredInt(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadRequiredInt64(root, property);
        if (value is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException($"mod.io response property '{property}' is outside Int32 range.");
        return (int)value;
    }

    private static long ReadRequiredInt64(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var number))
            throw new InvalidDataException($"mod.io response property '{property}' must be an integer.");
        return number;
    }

    private static long? ReadOptionalInt64(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            throw new InvalidDataException($"mod.io response property '{property}' must be an integer.");
        return number;
    }

    private static DateTimeOffset? ReadOptionalEpoch(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var seconds = ReadOptionalInt64(root, property);
        if (seconds is null || seconds == 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds.Value); }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"mod.io response property '{property}' is not a valid Unix timestamp.", ex);
        }
    }
}
