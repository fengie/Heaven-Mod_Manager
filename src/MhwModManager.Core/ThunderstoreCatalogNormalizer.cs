using System.Globalization;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record ThunderstoreAcquisitionFile(
    string ProviderModId,
    string ProviderFileId,
    Uri DownloadUri);

public static class ThunderstoreCatalogNormalizer
{
    public const string ProviderId = ThunderstoreCatalogPolicy.ProviderId;

    public static IReadOnlyList<CatalogMod> NormalizePackages(
        GameProfile game,
        string communityIdentifier,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        var community = ThunderstoreCatalogIdentity.NormalizeCommunityIdentifier(
            communityIdentifier,
            nameof(communityIdentifier));

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Thunderstore package-list response must be an array.");

        var result = new List<CatalogMod>(document.RootElement.GetArrayLength());
        foreach (var element in document.RootElement.EnumerateArray())
            result.Add(NormalizePackageElement(game, community, element));

        return result;
    }

    public static CatalogMod NormalizePackage(
        GameProfile game,
        string communityIdentifier,
        string expectedProviderModId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        var community = ThunderstoreCatalogIdentity.NormalizeCommunityIdentifier(
            communityIdentifier,
            nameof(communityIdentifier));

        if (!ThunderstoreCatalogIdentity.TryParseProviderModId(
                expectedProviderModId,
                out var providerCommunity,
                out _)
            || !providerCommunity.Equals(community, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Thunderstore provider mod id must match the expected community.",
                nameof(expectedProviderModId));
        }

        var mod = NormalizePackageElement(game, community, document.RootElement);
        if (!mod.ProviderModId.Equals(expectedProviderModId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Thunderstore package detail returned an unexpected package identity.");

        return mod;
    }

    public static ThunderstoreAcquisitionFile NormalizeAcquisition(
        string communityIdentifier,
        string expectedProviderModId,
        string expectedProviderFileId,
        JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(document);
        var community = ThunderstoreCatalogIdentity.NormalizeCommunityIdentifier(
            communityIdentifier,
            nameof(communityIdentifier));

        if (!ThunderstoreCatalogIdentity.TryParseProviderModId(
                expectedProviderModId,
                out var providerCommunity,
                out var expectedPackageUuid)
            || !providerCommunity.Equals(community, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Thunderstore provider mod id must match the expected community.",
                nameof(expectedProviderModId));
        }

        var expectedFileId = ThunderstoreCatalogIdentity.NormalizeUuid(
            expectedProviderFileId,
            nameof(expectedProviderFileId));
        var root = RequireObject(document.RootElement, "Thunderstore package");
        var actualPackageUuid = ReadRequiredUuid(root, "uuid4");
        if (!actualPackageUuid.Equals(expectedPackageUuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Thunderstore package detail returned an unexpected package UUID.");

        var owner = ReadRequiredString(root, "owner");
        var name = ReadRequiredString(root, "name");
        var versions = RequireArray(root, "versions");

        foreach (var version in versions.EnumerateArray())
        {
            var row = RequireObject(version, "Thunderstore package version");
            var fileId = ReadRequiredUuid(row, "uuid4");
            if (!fileId.Equals(expectedFileId, StringComparison.OrdinalIgnoreCase))
                continue;

            var versionNumber = NormalizeVersion(ReadRequiredString(row, "version_number"));
            var download = ValidateDownloadUri(
                ReadRequiredString(row, "download_url"),
                owner,
                name,
                versionNumber);

            return new ThunderstoreAcquisitionFile(
                ThunderstoreCatalogIdentity.BuildProviderModId(community, actualPackageUuid),
                fileId,
                download);
        }

        throw new InvalidDataException("Thunderstore package detail did not contain the requested package version.");
    }

    private static CatalogMod NormalizePackageElement(
        GameProfile game,
        string community,
        JsonElement element)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = RequireObject(element, "Thunderstore package");
        var name = ReadRequiredString(root, "name");
        var owner = ReadRequiredString(root, "owner");
        ValidatePackageToken(name, "package name");
        ValidatePackageToken(owner, "package owner");

        var fullName = ReadRequiredString(root, "full_name");
        var expectedFullName = $"{owner}-{name}";
        if (!fullName.Equals(expectedFullName, StringComparison.Ordinal))
            throw new InvalidDataException("Thunderstore package full_name does not match owner and package name.");

        var packageUuid = ReadRequiredUuid(root, "uuid4");
        var providerModId = ThunderstoreCatalogIdentity.BuildProviderModId(community, packageUuid);
        var sourceUrl = ReadRequiredString(root, "package_url");
        ValidatePackageUri(sourceUrl, community, owner, name);

        var categories = ReadStringArray(root, "categories");
        var versions = RequireArray(root, "versions");
        if (versions.GetArrayLength() == 0)
            throw new InvalidDataException("Thunderstore package contains no active versions.");

        var files = new List<CatalogModFile>(versions.GetArrayLength());
        string? latestDescription = null;
        string? latestVersion = null;
        string? thumbnail = null;
        IReadOnlyList<CatalogDependency> latestDependencies = Array.Empty<CatalogDependency>();
        long totalDownloads = 0;

        var index = 0;
        foreach (var version in versions.EnumerateArray())
        {
            var normalized = NormalizeVersionElement(
                community,
                providerModId,
                owner,
                name,
                version,
                recommended: index == 0);

            files.Add(normalized.File);
            totalDownloads = SaturatingAdd(totalDownloads, normalized.Downloads);

            if (index == 0)
            {
                latestDescription = normalized.Description;
                latestVersion = normalized.Version;
                thumbnail = normalized.IconUrl;
                latestDependencies = normalized.Dependencies;
            }

            index++;
        }

        var deprecated = ReadRequiredBoolean(root, "is_deprecated");
        var nsfw = ReadRequiredBoolean(root, "has_nsfw_content");
        var ratingScore = ReadNonNegativeInt64(root, "rating_score");

        var metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["community"] = community,
            ["package_uuid"] = packageUuid,
            ["is_deprecated"] = deprecated,
            ["has_nsfw_content"] = nsfw
        });

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(ProviderId, providerModId),
            ProviderId,
            providerModId,
            game.Id,
            name,
            latestDescription ?? string.Empty,
            latestDescription ?? string.Empty,
            owner,
            latestVersion,
            Category: categories.Count == 0 ? null : categories[0],
            Tags: categories,
            Thumbnail: thumbnail,
            Screenshots: thumbnail is null
                ? Array.Empty<CatalogImage>()
                : [new CatalogImage(thumbnail, IsThumbnail: true)],
            CreatedAt: ReadRequiredDate(root, "date_created"),
            UpdatedAt: ReadRequiredDate(root, "date_updated"),
            Downloads: totalDownloads,
            Endorsements: ratingScore,
            Rating: null,
            Dependencies: latestDependencies,
            SourceUrl: sourceUrl,
            Files: files,
            ProviderMetadata: metadata);
    }

    private static NormalizedVersion NormalizeVersionElement(
        string community,
        string providerModId,
        string owner,
        string packageName,
        JsonElement element,
        bool recommended)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = RequireObject(element, "Thunderstore package version");

        var versionName = ReadRequiredString(root, "name");
        if (!versionName.Equals(packageName, StringComparison.Ordinal))
            throw new InvalidDataException("Thunderstore package version name does not match its package.");

        if (!ReadRequiredBoolean(root, "is_active"))
            throw new InvalidDataException("Thunderstore v1 package API returned an inactive version.");

        var version = NormalizeVersion(ReadRequiredString(root, "version_number"));
        var fullName = ReadRequiredString(root, "full_name");
        var expectedFullName = $"{owner}-{packageName}-{version}";
        if (!fullName.Equals(expectedFullName, StringComparison.Ordinal))
            throw new InvalidDataException("Thunderstore package version full_name does not match its identity.");

        var providerFileId = ReadRequiredUuid(root, "uuid4");
        var description = ReadOptionalString(root, "description") ?? string.Empty;
        var icon = ReadRequiredString(root, "icon");
        ValidateAssetUri(icon);

        _ = ValidateDownloadUri(
            ReadRequiredString(root, "download_url"),
            owner,
            packageName,
            version);

        var dependencies = ReadDependencies(root, community);
        var downloads = ReadNonNegativeInt64(root, "downloads");
        var fileSize = ReadNonNegativeInt64(root, "file_size");

        var metadata = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["full_version_name"] = fullName,
            ["community"] = community
        });

        var file = new CatalogModFile(
            ProviderId,
            providerModId,
            providerFileId,
            fullName,
            fullName + ".zip",
            recommended ? CatalogFileCategory.Main : CatalogFileCategory.OldVersion,
            Version: version,
            SizeBytes: fileSize,
            Description: description,
            UploadedAt: ReadRequiredDate(root, "date_created"),
            Required: false,
            Recommended: recommended,
            Dependencies: dependencies,
            ProviderMetadata: metadata);

        return new NormalizedVersion(
            file,
            description,
            version,
            icon,
            dependencies,
            downloads);
    }

    private static List<CatalogDependency> ReadDependencies(
        JsonElement root,
        string community)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var entries = RequireArray(root, "dependencies");
        var result = new List<CatalogDependency>(entries.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Thunderstore dependency entries must be strings.");

            var value = entry.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Thunderstore dependency entries must be non-empty strings.");

            var parts = value.Split('-', StringSplitOptions.None);
            if (parts.Length != 3)
                throw new InvalidDataException("Thunderstore dependency identity must use namespace-name-version form.");

            ValidatePackageToken(parts[0], "dependency namespace");
            ValidatePackageToken(parts[1], "dependency package name");
            _ = NormalizeVersion(parts[2]);

            var dependencyName = $"{parts[0]}-{parts[1]}";
            if (!seen.Add(dependencyName))
                continue;

            result.Add(new CatalogDependency(
                dependencyName,
                ProviderId: ProviderId,
                ProviderModId: null,
                Url: $"https://thunderstore.io/c/{community}/p/{parts[0]}/{parts[1]}/",
                Required: true));
        }

        return result;
    }

    private static List<string> ReadStringArray(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var entries = RequireArray(root, propertyName);
        var result = new List<string>(entries.GetArrayLength());

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Thunderstore field '{propertyName}' entries must be strings.");

            var value = entry.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Thunderstore field '{propertyName}' entries must be non-empty.");

            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
        }

        return result;
    }

    private static JsonElement RequireObject(JsonElement element, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context} must be an object.");
        return element;
    }

    private static JsonElement RequireArray(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be an array.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be a non-empty string.");
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
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be a string when present.");

        return value.GetString()?.Trim();
    }

    private static bool ReadRequiredBoolean(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static long ReadNonNegativeInt64(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result)
            || result < 0)
        {
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be a non-negative integer.");
        }

        return result;
    }

    private static string ReadRequiredUuid(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadRequiredString(root, propertyName);
        try
        {
            return ThunderstoreCatalogIdentity.NormalizeUuid(value, propertyName);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be a UUID.", ex);
        }
    }

    private static DateTimeOffset ReadRequiredDate(JsonElement root, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadRequiredString(root, propertyName);
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new InvalidDataException($"Thunderstore field '{propertyName}' must be an ISO-8601 timestamp.");
        }

        return parsed;
    }

    private static string NormalizeVersion(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Trim().Split('.', StringSplitOptions.None);
        if (parts.Length != 3
            || parts.Any(part =>
                part.Length == 0
                || !part.All(character => character is >= '0' and <= '9')))
        {
            throw new InvalidDataException("Thunderstore version_number must use numeric major.minor.patch form.");
        }

        return string.Join(".", parts);
    }

    private static void ValidatePackageToken(string value, string context)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character =>
                !((character is >= 'a' and <= 'z')
                  || (character is >= 'A' and <= 'Z')
                  || (character is >= '0' and <= '9')
                  || character == '_')))
        {
            throw new InvalidDataException(
                $"Thunderstore {context} contains characters outside the published package-name contract.");
        }
    }

    private static void ValidatePackageUri(
        string value,
        string community,
        string owner,
        string packageName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !ThunderstoreCatalogIdentity.IsTrustedHost(uri.Host))
        {
            throw new InvalidDataException("Thunderstore package URL is not a trusted Thunderstore HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var currentRoute = segments.Length == 5
            && segments[0].Equals("c", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals(community, StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("p", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals(owner, StringComparison.Ordinal)
            && segments[4].Equals(packageName, StringComparison.Ordinal);

        var legacyRoute = segments.Length == 3
            && segments[0].Equals("package", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals(owner, StringComparison.Ordinal)
            && segments[2].Equals(packageName, StringComparison.Ordinal);

        if (!currentRoute && !legacyRoute)
            throw new InvalidDataException("Thunderstore package URL does not match its package identity.");
    }

    private static Uri ValidateDownloadUri(
        string value,
        string owner,
        string packageName,
        string version)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !ThunderstoreCatalogIdentity.IsTrustedHost(uri.Host)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("Thunderstore download URL is not a trusted direct HTTPS URL.");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (segments.Length != 5
            || !segments[0].Equals("package", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("download", StringComparison.OrdinalIgnoreCase)
            || !segments[2].Equals(owner, StringComparison.Ordinal)
            || !segments[3].Equals(packageName, StringComparison.Ordinal)
            || !segments[4].Equals(version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Thunderstore download URL does not match its package/version identity.");
        }

        return uri;
    }

    private static void ValidateAssetUri(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !ThunderstoreCatalogIdentity.IsTrustedHost(uri.Host))
        {
            throw new InvalidDataException("Thunderstore asset URL is not a trusted Thunderstore HTTPS URL.");
        }
    }

    private static long SaturatingAdd(long current, long value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (long.MaxValue - current < value)
            return long.MaxValue;
        return current + value;
    }

    private sealed record NormalizedVersion(
        CatalogModFile File,
        string Description,
        string Version,
        string IconUrl,
        IReadOnlyList<CatalogDependency> Dependencies,
        long Downloads);
}
