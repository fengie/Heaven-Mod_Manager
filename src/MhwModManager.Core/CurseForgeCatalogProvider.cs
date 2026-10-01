using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MhwModManager.Core;

public sealed record CurseForgeCatalogGameSource(
    string GameId,
    string GameDisplayName,
    int CurseForgeGameId);

public sealed class CurseForgeCatalogProvider : IModCatalogProvider
{
    private readonly CurseForgeTransport transport;
    private readonly CurseForgeCatalogGameSource[] sources;
    private CatalogProviderHealth health;

    public CurseForgeCatalogProvider(
        CurseForgeTransport transport,
        IEnumerable<CurseForgeCatalogGameSource> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sources);
        this.transport = transport;
        this.sources = sources.Select(ValidateSource).ToArray();
        if (this.sources.Length == 0)
            throw new ArgumentException("At least one CurseForge game mapping is required.", nameof(sources));
        if (this.sources.GroupBy(x => x.GameId, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new ArgumentException("CurseForge game mappings contain duplicate local game ids.", nameof(sources));

        health = new CatalogProviderHealth(
            ProviderId,
            CatalogProviderState.AuthenticationRequired,
            "CurseForge is configured but has not been contacted yet.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    public string ProviderId
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CurseForgeCatalogPolicy.ProviderId;
        }
    }

    public string DisplayName
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return "CurseForge";
        }
    }

    public CatalogProviderCapabilities Capabilities
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CatalogProviderCapabilities.Search
                | CatalogProviderCapabilities.Browse
                | CatalogProviderCapabilities.Metadata
                | CatalogProviderCapabilities.Images
                | CatalogProviderCapabilities.FileList
                | CatalogProviderCapabilities.FileVariants
                | CatalogProviderCapabilities.Dependencies
                | CatalogProviderCapabilities.AuthenticatedDownload
                | CatalogProviderCapabilities.Updates
                | CatalogProviderCapabilities.Ratings
                | CatalogProviderCapabilities.VersionHistory;
        }
    }

    public CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return CurseForgeCatalogPolicy.Compliance;
        }
    }

    public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<CatalogGame> games = sources
            .Select(x => new CatalogGame(
                x.GameId,
                x.GameDisplayName,
                x.CurseForgeGameId.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(games);
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        CatalogProviderComplianceValidator.EnsureUsable(Compliance, DateOnly.FromDateTime(DateTime.UtcNow));
        var source = RequireSource(request.Game);
        var wanted = Math.Clamp(request.Limit, 1, 100);
        var sortField = request.Mode switch
        {
            CatalogBrowseMode.Trending => 2,
            CatalogBrowseMode.Popular => 6,
            CatalogBrowseMode.RecentlyUpdated => 3,
            CatalogBrowseMode.Latest => 11,
            _ => throw new NotSupportedException($"CurseForge browse mode '{request.Mode}' is unsupported.")
        };

        try
        {
            var result = new List<CatalogMod>(wanted);
            for (var index = 0; result.Count < wanted && index < 100; index += 50)
            {
                var pageSize = Math.Min(50, wanted - result.Count);
                using var response = await transport.SearchModsAsync(
                    source.CurseForgeGameId,
                    request.Query,
                    sortField,
                    index,
                    pageSize,
                    ct).ConfigureAwait(false);
                var page = NormalizeModList(response.Document, request.Game, source.CurseForgeGameId);
                result.AddRange(page.Take(wanted - result.Count));
                if (page.Count < pageSize) break;
            }

            MarkConnected();
            return result;
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public async Task<CatalogMod?> GetModAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        var source = RequireSource(game);
        var modId = ParsePositiveId(providerModId, nameof(providerModId));
        try
        {
            using var response = await transport.GetModAsync(modId, ct).ConfigureAwait(false);
            var root = RequireDataObject(response.Document);
            var mod = NormalizeMod(root, game, source.CurseForgeGameId);
            MarkConnected();
            return mod;
        }
        catch (CurseForgeTransportException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            MarkConnected();
            return null;
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(game);
        _ = RequireSource(game);
        var modId = ParsePositiveId(providerModId, nameof(providerModId));

        try
        {
            using var response = await transport.GetModFilesAsync(modId, ct).ConfigureAwait(false);
            var files = NormalizeFiles(
                RequireDataArray(response.Document),
                modId.ToString(CultureInfo.InvariantCulture));
            MarkConnected();
            return files;
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public async Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
        CatalogAcquisitionRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(request);
        _ = RequireSource(request.Game);
        if (!request.Mod.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.File.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase)
            || !request.Mod.GameId.Equals(request.Game.Id, StringComparison.Ordinal)
            || !request.File.ProviderModId.Equals(request.Mod.ProviderModId, StringComparison.Ordinal))
        {
            throw new ArgumentException("CurseForge acquisition identities do not match the selected game/mod.", nameof(request));
        }

        var modId = ParsePositiveId(request.Mod.ProviderModId, nameof(request));
        var fileId = ParsePositiveId(request.File.ProviderFileId, nameof(request));

        try
        {
            using var response = await transport.GetDownloadUrlAsync(modId, fileId, ct).ConfigureAwait(false);
            var url = ReadRequiredString(RequireDataValue(response.Document), "download URL");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var download)
                || !download.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(download.UserInfo))
            {
                throw new InvalidDataException("CurseForge download URL must be credential-free absolute HTTPS.");
            }

            MarkConnected();
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.AuthenticatedDirect,
                download,
                null,
                null,
                "Use the provider-authorized CurseForge download URL immediately; it is not persisted in catalog provenance.");
        }
        catch (CurseForgeTransportException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            TrackFailure(ex, ct);
            return new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Assisted,
                null,
                new Uri(request.Mod.SourceUrl),
                null,
                "CurseForge did not authorize direct download for this file. Open the official provider page instead.");
        }
        catch (Exception ex)
        {
            TrackFailure(ex, ct);
            throw;
        }
    }

    public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(health);
    }

    private CurseForgeCatalogGameSource RequireSource(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var matches = sources.Where(x => x.GameId.Equals(game.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"{game.DisplayName} does not have a CurseForge game mapping configured.");
        return matches[0];
    }

    private static CurseForgeCatalogGameSource ValidateSource(CurseForgeCatalogGameSource source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.GameDisplayName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.CurseForgeGameId);
        return source with
        {
            GameId = source.GameId.Trim(),
            GameDisplayName = source.GameDisplayName.Trim()
        };
    }

    private static long ParsePositiveId(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("CurseForge ID must be a positive integer.", parameterName);
        return id;
    }

    private static IReadOnlyList<CatalogMod> NormalizeModList(
        JsonDocument document,
        GameProfile game,
        int expectedGameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var array = RequireDataArray(document);
        return array.EnumerateArray()
            .Select(item => NormalizeMod(item, game, expectedGameId))
            .ToArray();
    }

    private static CatalogMod NormalizeMod(JsonElement root, GameProfile game, int expectedGameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("CurseForge mod payload must be an object.");

        var modId = ReadRequiredInt64(root, "id");
        var gameId = ReadRequiredInt64(root, "gameId");
        if (gameId != expectedGameId)
            throw new InvalidDataException("CurseForge returned a mod for a different game.");

        var providerModId = modId.ToString(CultureInfo.InvariantCulture);
        var name = ReadRequiredString(root, "name");
        var summary = ReadOptionalString(root, "summary") ?? string.Empty;
        var authors = ReadNameArray(root, "authors");
        var categories = ReadNameArray(root, "categories");
        var files = root.TryGetProperty("latestFiles", out var latestFiles) && latestFiles.ValueKind == JsonValueKind.Array
            ? NormalizeFiles(latestFiles, providerModId)
            : Array.Empty<CatalogModFile>();
        var sourceUrl = ReadOptionalString(root, "links", "websiteUrl")
            ?? $"https://www.curseforge.com/projects/{providerModId}";

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
            || !source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CurseForge source URL must be absolute HTTPS.");

        var logo = ReadOptionalString(root, "logo", "thumbnailUrl");
        var screenshots = ReadImageArray(root, "screenshots");
        var tags = categories.Concat(ReadStringArray(root, "gameVersionLatestFiles", "gameVersion")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new CatalogMod(
            CatalogMod.BuildCanonicalId(CurseForgeCatalogPolicy.ProviderId, providerModId),
            CurseForgeCatalogPolicy.ProviderId,
            providerModId,
            game.Id,
            name,
            summary,
            summary,
            authors.Length == 0 ? "Unknown author" : string.Join(", ", authors),
            files.FirstOrDefault()?.Version,
            categories.FirstOrDefault(),
            tags,
            logo,
            screenshots,
            ReadOptionalDate(root, "dateCreated"),
            ReadOptionalDate(root, "dateModified"),
            ReadOptionalInt64(root, "downloadCount"),
            null,
            ReadOptionalDouble(root, "rating"),
            files.SelectMany(x => x.Dependencies ?? []).DistinctBy(x => (x.ProviderId, x.ProviderModId)).ToArray(),
            source.AbsoluteUri,
            files);
    }

    private static IReadOnlyList<CatalogModFile> NormalizeFiles(JsonElement array, string providerModId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CurseForge files payload must be an array.");

        var result = new List<CatalogModFile>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("CurseForge file payload must contain objects.");
            var id = ReadRequiredInt64(item, "id").ToString(CultureInfo.InvariantCulture);
            if (!ids.Add(id))
                throw new InvalidDataException($"CurseForge returned duplicate file identity '{id}'.");
            var displayName = ReadOptionalString(item, "displayName")
                ?? ReadOptionalString(item, "fileName")
                ?? $"File {id}";
            var fileName = ReadRequiredString(item, "fileName");
            var dependencies = NormalizeDependencies(item);
            result.Add(new CatalogModFile(
                CurseForgeCatalogPolicy.ProviderId,
                providerModId,
                id,
                displayName,
                fileName,
                CatalogFileCategory.Main,
                Version: ReadOptionalString(item, "displayName"),
                SizeBytes: ReadOptionalInt64(item, "fileLength"),
                UploadedAt: ReadOptionalDate(item, "fileDate"),
                Dependencies: dependencies));
        }
        return result;
    }

    private static IReadOnlyList<CatalogDependency> NormalizeDependencies(JsonElement file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!file.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (dependencies.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CurseForge dependencies payload must be an array.");

        var result = new List<CatalogDependency>();
        foreach (var dependency in dependencies.EnumerateArray())
        {
            if (dependency.ValueKind != JsonValueKind.Object) continue;
            var modId = ReadRequiredInt64(dependency, "modId").ToString(CultureInfo.InvariantCulture);
            result.Add(new CatalogDependency(
                $"CurseForge mod {modId}",
                CurseForgeCatalogPolicy.ProviderId,
                modId,
                $"https://www.curseforge.com/projects/{modId}",
                Required: ReadOptionalInt64(dependency, "relationType") == 3));
        }
        return result;
    }

    private static JsonElement RequireDataArray(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CurseForge response must contain a data array.");
        return data;
    }

    private static JsonElement RequireDataObject(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("CurseForge response must contain a data object.");
        return data;
    }

    private static JsonElement RequireDataValue(JsonDocument document)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!document.RootElement.TryGetProperty("data", out var data))
            throw new InvalidDataException("CurseForge response must contain data.");
        return data;
    }

    private static string ReadRequiredString(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalString(root, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"CurseForge property '{property}' is required.");
        return value;
    }

    private static string ReadRequiredString(JsonElement value, string context, bool directValue = true)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (directValue && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString()!.Trim();
        throw new InvalidDataException($"CurseForge {context} must be a non-empty string.");
    }

    private static string? ReadOptionalString(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"CurseForge property '{property}' must be a string.");
        return value.GetString()?.Trim();
    }

    private static string? ReadOptionalString(JsonElement root, string objectProperty, string nestedProperty)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(objectProperty, out var nested)
            || nested.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (nested.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"CurseForge property '{objectProperty}' must be an object.");
        return ReadOptionalString(nested, nestedProperty);
    }

    private static long ReadRequiredInt64(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = ReadOptionalInt64(root, property);
        if (value is null || value <= 0)
            throw new InvalidDataException($"CurseForge property '{property}' must be a positive integer.");
        return value.Value;
    }

    private static long? ReadOptionalInt64(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        throw new InvalidDataException($"CurseForge property '{property}' must be an integer.");
    }

    private static double? ReadOptionalDouble(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        throw new InvalidDataException($"CurseForge property '{property}' must be numeric.");
    }

    private static DateTimeOffset? ReadOptionalDate(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var text = ReadOptionalString(root, property);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
            throw new InvalidDataException($"CurseForge property '{property}' must be an ISO-8601 timestamp.");
        return result;
    }

    private static string[] ReadNameArray(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var array)
            || array.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"CurseForge property '{property}' must be an array.");
        return array.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => ReadOptionalString(x, "name"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray();
    }

    private static string[] ReadStringArray(JsonElement root, string arrayProperty, string valueProperty)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(arrayProperty, out var array)
            || array.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"CurseForge property '{arrayProperty}' must be an array.");
        return array.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => ReadOptionalString(x, valueProperty))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray();
    }

    private static IReadOnlyList<CatalogImage> ReadImageArray(JsonElement root, string property)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(property, out var array)
            || array.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"CurseForge property '{property}' must be an array.");

        var result = new List<CatalogImage>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var url = ReadOptionalString(item, "url");
            if (string.IsNullOrWhiteSpace(url)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CurseForge image URL must be absolute HTTPS.");
            result.Add(new CatalogImage(uri.AbsoluteUri, ReadOptionalString(item, "title")));
        }
        return result;
    }

    private void MarkConnected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        health = new CatalogProviderHealth(
            ProviderId,
            CatalogProviderState.Connected,
            "Connected to the official CurseForge API.",
            CheckedAt: DateTimeOffset.UtcNow);
    }

    private void TrackFailure(Exception ex, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (ex is OperationCanceledException && ct.IsCancellationRequested) return;

        if (ex is CurseForgeTransportException transportException)
        {
            if (transportException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                health = new CatalogProviderHealth(
                    ProviderId,
                    CatalogProviderState.AuthenticationRequired,
                    "CurseForge rejected the configured API key.",
                    CheckedAt: DateTimeOffset.UtcNow);
                return;
            }

            if (transportException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                health = new CatalogProviderHealth(
                    ProviderId,
                    CatalogProviderState.RateLimited,
                    "CurseForge API rate limit reached.",
                    new CatalogRateLimit(
                        null,
                        null,
                        null,
                        null,
                        transportException.RetryAfter is null ? null : DateTimeOffset.UtcNow.Add(transportException.RetryAfter.Value),
                        DateTimeOffset.UtcNow),
                    DateTimeOffset.UtcNow);
                return;
            }

            health = new CatalogProviderHealth(
                ProviderId,
                CatalogProviderState.Offline,
                "CurseForge API request failed.",
                CheckedAt: DateTimeOffset.UtcNow);
            return;
        }

        health = new CatalogProviderHealth(
            ProviderId,
            ex is InvalidDataException ? CatalogProviderState.Limited : CatalogProviderState.Offline,
            ex is InvalidDataException
                ? "CurseForge response schema changed or was invalid."
                : "CurseForge API is unreachable.",
            CheckedAt: DateTimeOffset.UtcNow);
    }
}
