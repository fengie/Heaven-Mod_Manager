using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

/// <summary>
/// Nexus catalog adapter. Stable v1 REST endpoints are used for the released path. Nexus' v2
/// GraphQL surface is intentionally not a required dependency because Nexus currently describes
/// it as work in progress. Search therefore filters the stable discovery feeds locally.
/// </summary>
public sealed partial class NexusCatalogProvider : IModCatalogProvider
{
    private const string BaseAddress = "https://api.nexusmods.com/v1/";
    private const string ApplicationName = "MHW-Mod-Manager";
    private const string ApplicationVersion = "8.8.11";
    private readonly CatalogCredentialStore credentials;
    private readonly HttpClient http;
    private CatalogProviderHealth health = new(
        "nexus",
        CatalogProviderState.AuthenticationRequired,
        "Connect Nexus Mods to browse its catalog.",
        CheckedAt: DateTimeOffset.UtcNow);

    public NexusCatalogProvider(CatalogCredentialStore credentials, HttpMessageHandler? handler = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.credentials = credentials;
        http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MHW-Mod-Manager/8.8.11");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Application-Name", ApplicationName);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Application-Version", ApplicationVersion);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string ProviderId { get; } = "nexus";
    public string DisplayName { get; } = "Nexus Mods";
    public CatalogProviderCapabilities Capabilities { get; } =
        CatalogProviderCapabilities.Search |
        CatalogProviderCapabilities.Browse |
        CatalogProviderCapabilities.Categories |
        CatalogProviderCapabilities.Metadata |
        CatalogProviderCapabilities.Images |
        CatalogProviderCapabilities.FileList |
        CatalogProviderCapabilities.FileVariants |
        CatalogProviderCapabilities.DirectDownload |
        CatalogProviderCapabilities.AuthenticatedDownload |
        CatalogProviderCapabilities.BrowserAssistedDownload |
        CatalogProviderCapabilities.Updates |
        CatalogProviderCapabilities.Ratings |
        CatalogProviderCapabilities.VersionHistory;

    public async Task<CatalogAuthenticationResult> AuthenticateAsync(CatalogAuthenticationRequest request, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("secret=<redacted>");
        if (string.IsNullOrWhiteSpace(request.Secret)) return new(false, "Enter a Nexus API key.");

        using var message = CreateRequest(HttpMethod.Get, "users/validate.json", request.Secret.Trim());
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        UpdateRateLimit(response);
        if (!response.IsSuccessStatusCode)
        {
            health = new(ProviderId, CatalogProviderState.AuthenticationRequired, $"Nexus authentication failed ({(int)response.StatusCode}).", health.RateLimit, DateTimeOffset.UtcNow);
            return new(false, "Nexus rejected that API key.");
        }

        credentials.WriteSecret(ProviderId, request.Secret);
        health = new(ProviderId, CatalogProviderState.Connected, "Connected to Nexus Mods.", health.RateLimit, DateTimeOffset.UtcNow);
        return new(true, "Nexus Mods connected.");
    }

    public async Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var doc = await GetJsonAsync("games.json", ct);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<CatalogGame>();
        return doc.RootElement.EnumerateArray()
            .Select(game => new CatalogGame(
                GetString(game, "domain_name") ?? GetString(game, "id") ?? "unknown",
                GetString(game, "name") ?? GetString(game, "domain_name") ?? "Unknown game",
                GetString(game, "domain_name")))
            .Where(game => !game.Id.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<CatalogMod>> SearchModsAsync(CatalogBrowseRequest request, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={request.Game.Id}; query={request.Query ?? "<empty>"}");
        var discovered = await DiscoverAsync(request.Game, ct);
        var query = request.Query?.Trim();
        IEnumerable<CatalogMod> filtered = discovered;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(mod =>
                mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                mod.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                mod.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (mod.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                mod.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
        return Sort(filtered, request.Mode).Take(Math.Clamp(request.Limit, 1, 500)).ToArray();
    }

    public async Task<IReadOnlyList<CatalogMod>> GetTrendingModsAsync(CatalogBrowseRequest request, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={request.Game.Id}");
        var domain = RequireGameDomain(request.Game);
        using var doc = await GetJsonAsync($"games/{Uri.EscapeDataString(domain)}/mods/trending.json", ct);
        return ParseModArray(doc, request.Game).Take(Math.Clamp(request.Limit, 1, 500)).ToArray();
    }

    public async Task<IReadOnlyList<CatalogMod>> GetLatestModsAsync(CatalogBrowseRequest request, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={request.Game.Id}");
        var domain = RequireGameDomain(request.Game);
        using var added = await GetJsonAsync($"games/{Uri.EscapeDataString(domain)}/mods/latest_added.json", ct);
        using var updated = await GetJsonAsync($"games/{Uri.EscapeDataString(domain)}/mods/latest_updated.json", ct);
        return ParseModArray(added, request.Game)
            .Concat(ParseModArray(updated, request.Game))
            .GroupBy(mod => mod.ProviderModId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(mod => mod.UpdatedAt ?? mod.CreatedAt)
            .Take(Math.Clamp(request.Limit, 1, 500))
            .ToArray();
    }

    public async Task<CatalogMod?> GetModAsync(GameProfile game, string providerModId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        var domain = RequireGameDomain(game);
        using var doc = await GetJsonAsync($"games/{Uri.EscapeDataString(domain)}/mods/{Uri.EscapeDataString(providerModId)}.json", ct);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var files = await GetModFilesAsync(game, providerModId, ct);
        return ParseMod(doc.RootElement, game) with { Files = files };
    }

    public async Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(GameProfile game, string providerModId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        var domain = RequireGameDomain(game);
        using var doc = await GetJsonAsync($"games/{Uri.EscapeDataString(domain)}/mods/{Uri.EscapeDataString(providerModId)}/files.json", ct);
        if (doc is null) return Array.Empty<CatalogModFile>();
        var root = doc.RootElement;
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return Array.Empty<CatalogModFile>();

        return files.EnumerateArray()
            .Select(file => ParseFile(providerModId, file))
            .Where(file => file.Category is not CatalogFileCategory.Removed)
            .OrderBy(file => FileCategoryOrder(file.Category))
            .ThenByDescending(file => file.UploadedAt)
            .ToArray();
    }

    public Task<IReadOnlyList<CatalogDependency>> GetDependenciesAsync(GameProfile game, string providerModId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        return Task.FromResult<IReadOnlyList<CatalogDependency>>(Array.Empty<CatalogDependency>());
    }

    public async Task<IReadOnlyList<CatalogImage>> GetScreenshotsAsync(GameProfile game, string providerModId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        var mod = await GetModAsync(game, providerModId, ct);
        return mod?.Screenshots ?? Array.Empty<CatalogImage>();
    }

    public Task<IReadOnlyList<CatalogModFile>> GetDownloadOptionsAsync(GameProfile game, string providerModId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}; mod={providerModId}");
        return GetModFilesAsync(game, providerModId, ct);
    }

    public async Task<CatalogDownloadResolution> ResolveDownloadAsync(CatalogDownloadRequest request, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={request.Game.Id}; mod={request.Mod.ProviderModId}; file={request.File.ProviderFileId}");
        var domain = RequireGameDomain(request.Game);
        var relative = $"games/{Uri.EscapeDataString(domain)}/mods/{Uri.EscapeDataString(request.Mod.ProviderModId)}/files/{Uri.EscapeDataString(request.File.ProviderFileId)}/download_link.json";
        if (!string.IsNullOrWhiteSpace(request.NxmKey) && request.NxmExpires is > 0)
            relative += $"?key={Uri.EscapeDataString(request.NxmKey)}&expires={request.NxmExpires.Value.ToString(CultureInfo.InvariantCulture)}";

        var key = credentials.ReadSecret(ProviderId);
        var assisted = BuildFilePage(domain, request.Mod.ProviderModId, request.File.ProviderFileId);
        if (string.IsNullOrWhiteSpace(key))
            return new(false, null, assisted, null, "Connect Nexus Mods before downloading.");

        using var message = CreateRequest(HttpMethod.Get, relative, key);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        UpdateRateLimit(response);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return new(false, null, assisted, null,
                "Nexus requires this download to be authorized on the website for the current account. No restriction is bypassed.");
        }
        if ((int)response.StatusCode == 429)
        {
            health = new(ProviderId, CatalogProviderState.RateLimited, "Nexus API rate limit reached.", health.RateLimit, DateTimeOffset.UtcNow);
            return new(false, null, assisted, null, "Nexus API rate limit reached. Try again after the provider resets your quota.");
        }
        if (!response.IsSuccessStatusCode)
            return new(false, null, assisted, null, $"Nexus could not resolve this download ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new(false, null, assisted, null, "Nexus returned no direct download links.");

        foreach (var candidate in doc.RootElement.EnumerateArray())
        {
            var uriText = GetString(candidate, "URI") ?? GetString(candidate, "uri");
            if (Uri.TryCreate(uriText, UriKind.Absolute, out var uri) && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return new(true, uri, assisted, DateTimeOffset.UtcNow.AddMinutes(10), "Direct Nexus download resolved.");
        }
        return new(false, null, assisted, null, "Nexus returned no usable HTTPS download link.");
    }

    public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        if (credentials.ReadSecret(ProviderId) is null)
            health = new(ProviderId, CatalogProviderState.AuthenticationRequired, "Connect Nexus Mods to browse and download.", health.RateLimit, DateTimeOffset.UtcNow);
        return Task.FromResult(health);
    }

    private async Task<IReadOnlyList<CatalogMod>> DiscoverAsync(GameProfile game, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        var request = new CatalogBrowseRequest(game, Limit: 100);
        var trending = await GetTrendingModsAsync(request, ct);
        var latest = await GetLatestModsAsync(request, ct);
        return trending.Concat(latest)
            .GroupBy(mod => mod.ProviderModId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(mod => mod.UpdatedAt ?? mod.CreatedAt).First())
            .ToArray();
    }

    private async Task<JsonDocument?> GetJsonAsync(string relative, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"relative={relative}");
        var key = credentials.ReadSecret(ProviderId);
        if (string.IsNullOrWhiteSpace(key))
        {
            health = new(ProviderId, CatalogProviderState.AuthenticationRequired, "Nexus API key required.", health.RateLimit, DateTimeOffset.UtcNow);
            return null;
        }

        using var request = CreateRequest(HttpMethod.Get, relative, key);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            UpdateRateLimit(response);
            if ((int)response.StatusCode == 429)
            {
                health = new(ProviderId, CatalogProviderState.RateLimited, "Nexus API rate limit reached.", health.RateLimit, DateTimeOffset.UtcNow);
                return null;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                health = new(ProviderId, CatalogProviderState.AuthenticationRequired, "Nexus authentication is required.", health.RateLimit, DateTimeOffset.UtcNow);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                health = new(ProviderId, CatalogProviderState.Limited, $"Nexus request failed ({(int)response.StatusCode}). Cached catalog data remains usable.", health.RateLimit, DateTimeOffset.UtcNow);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            health = new(ProviderId, CatalogProviderState.Connected, "Connected to Nexus Mods.", health.RateLimit, DateTimeOffset.UtcNow);
            return doc;
        }
        catch (HttpRequestException ex)
        {
            MasterDebugLog.Write("CATALOG-NEXUS", $"Nexus request failed relative={relative}", ex);
            health = new(ProviderId, CatalogProviderState.Offline, "Nexus Mods is temporarily unreachable. Cached data remains usable.", health.RateLimit, DateTimeOffset.UtcNow);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            health = new(ProviderId, CatalogProviderState.Offline, "Nexus request timed out. Cached data remains usable.", health.RateLimit, DateTimeOffset.UtcNow);
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relative, string apiKey)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"method={method}; relative={relative}; apiKey=<redacted>");
        var request = new HttpRequestMessage(method, new Uri(new Uri(BaseAddress), relative));
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.TryAddWithoutValidation("Application-Name", ApplicationName);
        request.Headers.TryAddWithoutValidation("Application-Version", ApplicationVersion);
        return request;
    }

    private void UpdateRateLimit(HttpResponseMessage response)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"status={(int)response.StatusCode}");
        health = health with
        {
            RateLimit = new(
                HeaderInt(response, "X-RL-Hourly-Limit"),
                HeaderInt(response, "X-RL-Hourly-Remaining"),
                HeaderInt(response, "X-RL-Daily-Limit"),
                HeaderInt(response, "X-RL-Daily-Remaining"),
                DateTimeOffset.UtcNow)
        };
    }

    private static int? HeaderInt(HttpResponseMessage response, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"header={name}");
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<CatalogMod> ParseModArray(JsonDocument? doc, GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<CatalogMod>();
        return doc.RootElement.EnumerateArray().Select(item => ParseMod(item, game)).ToArray();
    }

    private static CatalogMod ParseMod(JsonElement item, GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        var id = GetString(item, "mod_id") ?? GetString(item, "id") ?? "unknown";
        var domain = RequireGameDomain(game);
        var picture = GetString(item, "picture_url");
        var images = string.IsNullOrWhiteSpace(picture)
            ? Array.Empty<CatalogImage>()
            : new[] { new CatalogImage(picture, IsThumbnail: true) };
        var summary = CleanText(GetString(item, "summary") ?? string.Empty);
        var description = CleanText(GetString(item, "description") ?? summary);
        var author = GetString(item, "author") ?? GetString(item, "uploaded_by") ?? "Unknown author";
        var category = GetString(item, "category_name") ?? GetString(item, "category_id");
        var source = $"https://www.nexusmods.com/{domain}/mods/{id}";
        return new(
            CatalogMod.BuildCanonicalId("nexus", id),
            "nexus",
            id,
            game.Id,
            GetString(item, "name") ?? $"Nexus mod {id}",
            summary,
            description,
            author,
            GetString(item, "version"),
            category,
            Array.Empty<string>(),
            picture,
            images,
            Timestamp(item, "created_timestamp"),
            Timestamp(item, "updated_timestamp"),
            GetLong(item, "mod_downloads") ?? GetLong(item, "download_count"),
            GetLong(item, "endorsement_count"),
            null,
            Array.Empty<CatalogDependency>(),
            source,
            Array.Empty<CatalogModFile>(),
            item.GetRawText());
    }

    private static CatalogModFile ParseFile(string providerModId, JsonElement file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mod={providerModId}");
        var id = GetString(file, "file_id") ?? GetString(file, "id") ?? "unknown";
        var category = ParseFileCategory(GetString(file, "category_name"));
        var sizeKb = GetLong(file, "size_kb");
        var size = GetLong(file, "size") ?? (sizeKb is null ? null : checked(sizeKb.Value * 1024L));
        return new(
            "nexus",
            providerModId,
            id,
            GetString(file, "name") ?? GetString(file, "file_name") ?? $"File {id}",
            GetString(file, "file_name") ?? GetString(file, "name") ?? $"nexus-{providerModId}-{id}.zip",
            category,
            GetString(file, "version") ?? GetString(file, "mod_version"),
            size,
            CleanText(GetString(file, "description") ?? string.Empty),
            Timestamp(file, "uploaded_timestamp"),
            Recommended: GetBool(file, "is_primary"),
            Dependencies: Array.Empty<CatalogDependency>(),
            ProviderMetadata: file.GetRawText());
    }

    public static CatalogFileCategory ParseFileCategory(string? categoryName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = categoryName?.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return normalized switch
        {
            "main" or "main_files" => CatalogFileCategory.Main,
            "optional" or "optional_files" => CatalogFileCategory.Optional,
            "update" or "updates" => CatalogFileCategory.Update,
            "miscellaneous" or "misc" => CatalogFileCategory.Miscellaneous,
            "old_version" or "old_versions" => CatalogFileCategory.OldVersion,
            "archived" => CatalogFileCategory.Archived,
            "removed" or "deleted" => CatalogFileCategory.Removed,
            _ => CatalogFileCategory.Unknown
        };
    }

    private static int FileCategoryOrder(CatalogFileCategory category)
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

    private static IEnumerable<CatalogMod> Sort(IEnumerable<CatalogMod> mods, CatalogBrowseMode mode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mode={mode}");
        return mode switch
        {
            CatalogBrowseMode.RecentlyUpdated => mods.OrderByDescending(mod => mod.UpdatedAt ?? mod.CreatedAt),
            CatalogBrowseMode.Latest => mods.OrderByDescending(mod => mod.CreatedAt ?? mod.UpdatedAt),
            CatalogBrowseMode.Popular => mods.OrderByDescending(mod => mod.Downloads ?? mod.Endorsements ?? 0),
            _ => mods.OrderByDescending(mod => mod.Endorsements ?? mod.Downloads ?? 0)
        };
    }

    private static Uri BuildFilePage(string domain, string modId, string fileId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new Uri($"https://www.nexusmods.com/{domain}/mods/{Uri.EscapeDataString(modId)}?tab=files&file_id={Uri.EscapeDataString(fileId)}");
    }

    private static string RequireGameDomain(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.Id}");
        if (string.IsNullOrWhiteSpace(game.NexusGameDomain))
            throw new InvalidOperationException($"{game.DisplayName} does not have a Nexus Mods domain configured.");
        return game.NexusGameDomain;
    }

    private static string? GetString(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={name}");
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static long? GetLong(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={name}");
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={name}");
        if (!element.TryGetProperty(name, out var value)) return false;
        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0) ||
               bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static DateTimeOffset? Timestamp(JsonElement element, string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"property={name}");
        var raw = GetLong(element, name);
        if (raw is > 0) return DateTimeOffset.FromUnixTimeSeconds(raw.Value);
        var text = GetString(element, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    private static string CleanText(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var withoutMarkup = HtmlTagRegex().Replace(value, " ");
        return WebUtility.HtmlDecode(WhitespaceRegex().Replace(withoutMarkup, " ")).Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
