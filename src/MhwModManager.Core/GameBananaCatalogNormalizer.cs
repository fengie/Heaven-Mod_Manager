using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public static class GameBananaCatalogNormalizer
{
    public const string ProviderId = "gamebanana";

    private static readonly string[] FileNameProperties =
    [
        "_sFile",
        "file",
        "filename",
        "name"
    ];

    public static IReadOnlyList<string> NormalizeNewModIds(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GameBanana Core/List/New response must be an array.");

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2)
                throw new InvalidDataException("GameBanana Core/List/New entry must be a [type,id] pair.");

            var values = entry.EnumerateArray().ToArray();
            if (values[0].ValueKind != JsonValueKind.String
                || !string.Equals(values[0].GetString(), "Mod", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("GameBanana Core/List/New returned a non-Mod entry for a Mod-only request.");
            }

            var id = ReadPositiveId(values[1], "Core/List/New item id");
            if (seen.Add(id)) ids.Add(id);
        }

        return ids;
    }

    public static CatalogMod NormalizeMod(
        GameProfile game,
        string expectedProviderModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        var providerModId = NormalizePositiveId(expectedProviderModId, nameof(expectedProviderModId));

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GameBanana Core/Item/Data response must be an object when return_keys=1.");

        RejectApiError(root);

        var sourceUrl = ReadRequiredString(root, "Url().sProfileUrl()");
        ValidateGameBananaModUrl(sourceUrl, providerModId);

        var responseGameName = ReadRequiredString(root, "Game().name");
        if (!responseGameName.Equals(game.DisplayName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GameBanana mod detail belongs to a different game.");

        var name = ReadRequiredString(root, "name");
        var description = ReadOptionalString(root, "description") ?? string.Empty;
        var author = ReadOptionalString(root, "Owner().name") ?? "Unknown author";
        var category = ReadOptionalString(root, "Category().name")
            ?? ReadOptionalString(root, "RootCategory().name");
        var thumbnail = ReadOptionalString(root, "Preview().sStructuredDataFullsizeUrl()");
        if (thumbnail is not null)
            ValidateGameBananaAssetUrl(thumbnail, "preview");

        var files = NormalizeFiles(root, providerModId);
        var likes = ReadOptionalInt64(root, "likes");

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId,
            game.Id,
            name,
            description,
            description,
            author,
            Version: null,
            Category: category,
            Tags: Array.Empty<string>(),
            Thumbnail: thumbnail,
            Screenshots: thumbnail is null
                ? Array.Empty<CatalogImage>()
                : [new CatalogImage(thumbnail, IsThumbnail: true)],
            CreatedAt: ReadOptionalEpoch(root, "date"),
            UpdatedAt: ReadOptionalEpoch(root, "mdate"),
            Downloads: ReadOptionalInt64(root, "downloads"),
            Endorsements: likes,
            Rating: null,
            Dependencies: Array.Empty<CatalogDependency>(),
            SourceUrl: sourceUrl,
            Files: files,
            ProviderMetadata: null);
    }

    public static IReadOnlyList<CatalogModFile> NormalizeFiles(
        JsonElement root,
        string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalizedModId = NormalizePositiveId(providerModId, nameof(providerModId));
        if (!root.TryGetProperty("Files().aFiles()", out var files)
            || files.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CatalogModFile>();
        }

        if (files.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GameBanana Files().aFiles() must be an object keyed by file id.");

        var result = new List<CatalogModFile>();
        foreach (var property in files.EnumerateObject())
        {
            var fileId = NormalizePositiveId(property.Name, "GameBanana file id");
            if (property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"GameBanana file '{fileId}' must be an object.");

            var rowId = ReadRequiredString(property.Value, "_idRow");
            if (!NormalizePositiveId(rowId, "GameBanana file row id").Equals(fileId, StringComparison.Ordinal))
                throw new InvalidDataException($"GameBanana file '{fileId}' contains a conflicting row identity.");

            var fileName = ReadRequiredString(property.Value, "_sFile");
            var downloadUrl = ReadRequiredString(property.Value, "_sDownloadUrl");
            ValidateGameBananaFileUrl(downloadUrl, fileId);

            var archived = ReadOptionalBoolean(property.Value, "_bIsArchived") ?? false;
            var analysisState = ReadOptionalString(property.Value, "_sAnalysisState");
            var analysisResult = ReadOptionalString(property.Value, "_sAnalysisResult");
            var avState = ReadOptionalString(property.Value, "_sAvState");
            var avResult = ReadOptionalString(property.Value, "_sAvResult");
            ValidateAnalysisState(analysisState, analysisResult, avState, avResult, fileId);

            result.Add(new CatalogModFile(
                ProviderId,
                normalizedModId,
                fileId,
                fileName,
                fileName,
                archived ? CatalogFileCategory.Archived : CatalogFileCategory.Main,
                SizeBytes: ReadOptionalInt64(property.Value, "_nFilesize"),
                Description: ReadOptionalString(property.Value, "_sAnalysisResultVerbose"),
                UploadedAt: ReadOptionalEpoch(property.Value, "_tsDateAdded"),
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: null));
        }

        return result;
    }

    private static void ValidateAnalysisState(
        string? analysisState,
        string? analysisResult,
        string? avState,
        string? avResult,
        string fileId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (analysisState is not null
            && !analysisState.Equals("done", StringComparison.OrdinalIgnoreCase)
            && !analysisState.Equals("pending", StringComparison.OrdinalIgnoreCase)
            && !analysisState.Equals("processing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' returned an unknown analysis state.");
        }

        if (avState is not null
            && !avState.Equals("done", StringComparison.OrdinalIgnoreCase)
            && !avState.Equals("pending", StringComparison.OrdinalIgnoreCase)
            && !avState.Equals("processing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' returned an unknown antivirus state.");
        }

        _ = analysisResult;
        _ = avResult;
    }

    private static void ValidateGameBananaFileUrl(string value, string fileId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' download URL is not a trusted GameBanana HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !segments[0].Equals("dl", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals(fileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' download URL does not match its file identity.");
        }
    }

    private static void RejectApiError(JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty("error", out var error)) return;

        var message = error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : error.GetRawText();
        throw new InvalidDataException($"GameBanana API returned an error object: {message}");
    }

    private static void ValidateGameBananaModUrl(string value, string expectedProviderModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("GameBanana source URL is not a trusted GameBanana HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals(expectedProviderModId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("GameBanana source URL does not match the expected mod identity.");
        }
    }

    private static void ValidateGameBananaAssetUrl(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"GameBanana {context} URL is not a trusted GameBanana HTTPS URL.");
        }
    }

    private static string ReadPositiveId(JsonElement value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number > 0)
            return number.ToString(CultureInfo.InvariantCulture);
        if (value.ValueKind == JsonValueKind.String)
            return NormalizePositiveId(value.GetString() ?? string.Empty, context);
        throw new InvalidDataException($"GameBanana {context} must be a positive integer.");
    }

    private static string NormalizePositiveId(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number) || number <= 0)
        {
            throw new InvalidDataException($"GameBanana {context} must be a positive integer.");
        }
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"GameBanana response property '{propertyName}' is required.");
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
            throw new InvalidDataException($"GameBanana response property '{propertyName}' must be a string.");
        return value.GetString()?.Trim();
    }

    private static bool? ReadOptionalBoolean(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();

        throw new InvalidDataException($"GameBanana response property '{propertyName}' must be a boolean.");
    }

    private static long? ReadOptionalInt64(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
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

        throw new InvalidDataException($"GameBanana response property '{propertyName}' must be an integer.");
    }

    private static DateTimeOffset? ReadOptionalEpoch(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var seconds = ReadOptionalInt64(root, propertyName);
        if (seconds is null) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException(
                $"GameBanana response property '{propertyName}' is not a valid Unix timestamp.",
                ex);
        }
    }
}
using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public static class GameBananaCatalogNormalizer
{
    public const string ProviderId = "gamebanana";

    private static readonly string[] FileNameProperties =
    [
        "_sFile",
        "file",
        "filename",
        "name"
    ];

    public static IReadOnlyList<string> NormalizeNewModIds(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GameBanana Core/List/New response must be an array.");

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2)
                throw new InvalidDataException("GameBanana Core/List/New entry must be a [type,id] pair.");

            var values = entry.EnumerateArray().ToArray();
            if (values[0].ValueKind != JsonValueKind.String
                || !string.Equals(values[0].GetString(), "Mod", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("GameBanana Core/List/New returned a non-Mod entry for a Mod-only request.");
            }

            var id = ReadPositiveId(values[1], "Core/List/New item id");
            if (seen.Add(id)) ids.Add(id);
        }

        return ids;
    }

    public static CatalogMod NormalizeMod(
        GameProfile game,
        string expectedProviderModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        var providerModId = NormalizePositiveId(expectedProviderModId, nameof(expectedProviderModId));

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GameBanana Core/Item/Data response must be an object when return_keys=1.");

        RejectApiError(root);

        var sourceUrl = ReadRequiredString(root, "Url().sProfileUrl()");
        ValidateGameBananaModUrl(sourceUrl, providerModId);

        var responseGameName = ReadRequiredString(root, "Game().name");
        if (!responseGameName.Equals(game.DisplayName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GameBanana mod detail belongs to a different game.");

        var name = ReadRequiredString(root, "name");
        var description = ReadOptionalString(root, "description") ?? string.Empty;
        var author = ReadOptionalString(root, "Owner().name") ?? "Unknown author";
        var category = ReadOptionalString(root, "Category().name")
            ?? ReadOptionalString(root, "RootCategory().name");
        var thumbnail = ReadOptionalString(root, "Preview().sStructuredDataFullsizeUrl()");
        if (thumbnail is not null)
            ValidateGameBananaAssetUrl(thumbnail, "preview");

        var files = NormalizeFiles(root, providerModId);
        var likes = ReadOptionalInt64(root, "likes");

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId,
            game.Id,
            name,
            description,
            description,
            author,
            Version: null,
            Category: category,
            Tags: Array.Empty<string>(),
            Thumbnail: thumbnail,
            Screenshots: thumbnail is null
                ? Array.Empty<CatalogImage>()
                : [new CatalogImage(thumbnail, IsThumbnail: true)],
            CreatedAt: ReadOptionalEpoch(root, "date"),
            UpdatedAt: ReadOptionalEpoch(root, "mdate"),
            Downloads: ReadOptionalInt64(root, "downloads"),
            Endorsements: likes,
            Rating: null,
            Dependencies: Array.Empty<CatalogDependency>(),
            SourceUrl: sourceUrl,
            Files: files,
            ProviderMetadata: null);
    }

    public static IReadOnlyList<CatalogModFile> NormalizeFiles(
        JsonElement root,
        string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalizedModId = NormalizePositiveId(providerModId, nameof(providerModId));
        if (!root.TryGetProperty("Files().aFiles()", out var files)
            || files.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<CatalogModFile>();
        }

        if (files.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GameBanana Files().aFiles() must be an object keyed by file id.");

        var result = new List<CatalogModFile>();
        foreach (var property in files.EnumerateObject())
        {
            var fileId = NormalizePositiveId(property.Name, "GameBanana file id");
            if (property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"GameBanana file '{fileId}' must be an object.");

            var rowId = ReadRequiredString(property.Value, "_idRow");
            if (!NormalizePositiveId(rowId, "GameBanana file row id").Equals(fileId, StringComparison.Ordinal))
                throw new InvalidDataException($"GameBanana file '{fileId}' contains a conflicting row identity.");

            var fileName = ReadRequiredString(property.Value, "_sFile");
            var downloadUrl = ReadRequiredString(property.Value, "_sDownloadUrl");
            ValidateGameBananaFileUrl(downloadUrl, fileId);

            var archived = ReadOptionalBoolean(property.Value, "_bIsArchived") ?? false;
            var analysisState = ReadOptionalString(property.Value, "_sAnalysisState");
            var analysisResult = ReadOptionalString(property.Value, "_sAnalysisResult");
            var avState = ReadOptionalString(property.Value, "_sAvState");
            var avResult = ReadOptionalString(property.Value, "_sAvResult");
            ValidateAnalysisState(analysisState, analysisResult, avState, avResult, fileId);

            result.Add(new CatalogModFile(
                ProviderId,
                normalizedModId,
                fileId,
                fileName,
                fileName,
                archived ? CatalogFileCategory.Archived : CatalogFileCategory.Main,
                SizeBytes: ReadOptionalInt64(property.Value, "_nFilesize"),
                Description: ReadOptionalString(property.Value, "_sAnalysisResultVerbose"),
                UploadedAt: ReadOptionalEpoch(property.Value, "_tsDateAdded"),
                Dependencies: Array.Empty<CatalogDependency>(),
                ProviderMetadata: null));
        }

        return result;
    }

    private static void ValidateAnalysisState(
        string? analysisState,
        string? analysisResult,
        string? avState,
        string? avResult,
        string fileId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (analysisState is not null
            && !analysisState.Equals("done", StringComparison.OrdinalIgnoreCase)
            && !analysisState.Equals("pending", StringComparison.OrdinalIgnoreCase)
            && !analysisState.Equals("processing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' returned an unknown analysis state.");
        }

        if (avState is not null
            && !avState.Equals("done", StringComparison.OrdinalIgnoreCase)
            && !avState.Equals("pending", StringComparison.OrdinalIgnoreCase)
            && !avState.Equals("processing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' returned an unknown antivirus state.");
        }

        _ = analysisResult;
        _ = avResult;
    }

    private static void ValidateGameBananaFileUrl(string value, string fileId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' download URL is not a trusted GameBanana HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !segments[0].Equals("dl", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals(fileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"GameBanana file '{fileId}' download URL does not match its file identity.");
        }
    }

    private static void RejectApiError(JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty("error", out var error)) return;

        var message = error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : error.GetRawText();
        throw new InvalidDataException($"GameBanana API returned an error object: {message}");
    }

    private static void ValidateGameBananaModUrl(string value, string expectedProviderModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("GameBanana source URL is not a trusted GameBanana HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals(expectedProviderModId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("GameBanana source URL does not match the expected mod identity.");
        }
    }

    private static void ValidateGameBananaAssetUrl(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !(uri.Host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".gamebanana.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"GameBanana {context} URL is not a trusted GameBanana HTTPS URL.");
        }
    }

    private static string ReadPositiveId(JsonElement value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number > 0)
            return number.ToString(CultureInfo.InvariantCulture);
        if (value.ValueKind == JsonValueKind.String)
            return NormalizePositiveId(value.GetString() ?? string.Empty, context);
        throw new InvalidDataException($"GameBanana {context} must be a positive integer.");
    }

    private static string NormalizePositiveId(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number) || number <= 0)
        {
            throw new InvalidDataException($"GameBanana {context} must be a positive integer.");
        }
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"GameBanana response property '{propertyName}' is required.");
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
            throw new InvalidDataException($"GameBanana response property '{propertyName}' must be a string.");
        return value.GetString()?.Trim();
    }

    private static bool? ReadOptionalBoolean(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();

        throw new InvalidDataException($"GameBanana response property '{propertyName}' must be a boolean.");
    }

    private static long? ReadOptionalInt64(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
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

        throw new InvalidDataException($"GameBanana response property '{propertyName}' must be an integer.");
    }

    private static DateTimeOffset? ReadOptionalEpoch(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var seconds = ReadOptionalInt64(root, propertyName);
        if (seconds is null) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException(
                $"GameBanana response property '{propertyName}' is not a valid Unix timestamp.",
                ex);
        }
    }
}
