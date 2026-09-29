using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed class ModCatalogProviderRegistry
{
    private readonly Dictionary<string, IModCatalogProvider> providers;

    public ModCatalogProviderRegistry(IEnumerable<IModCatalogProvider> providers)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.providers = providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
        Providers = this.providers.Values.OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<IModCatalogProvider> Providers { get; }

    public IModCatalogProvider Get(string providerId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}");
        if (!providers.TryGetValue(providerId, out var provider))
            throw new KeyNotFoundException($"Catalog provider '{providerId}' is not registered.");
        return provider;
    }
}

public sealed class CatalogCacheStore(string root)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(DateTimeOffset SyncedAt, IReadOnlyList<CatalogMod> Mods)?> ReadAsync(
        string providerId,
        string gameId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; game={gameId}");
        var path = CachePath(providerId, gameId);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var envelope = await JsonSerializer.DeserializeAsync<CatalogCacheEnvelope>(stream, JsonOptions, ct);
            return envelope is null ? null : (envelope.SyncedAt, envelope.Mods);
        }
        catch (JsonException ex)
        {
            MasterDebugLog.Write("CATALOG-CACHE", $"Ignoring malformed catalog cache {path}", ex);
            return null;
        }
        catch (IOException ex)
        {
            MasterDebugLog.Write("CATALOG-CACHE", $"Catalog cache read failed {path}", ex);
            return null;
        }
    }

    public async Task WriteAsync(
        string providerId,
        string gameId,
        IReadOnlyList<CatalogMod> mods,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; game={gameId}; count={mods.Count}");
        Directory.CreateDirectory(root);
        var path = CachePath(providerId, gameId);
        var temp = path + $".part-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, new CatalogCacheEnvelope(DateTimeOffset.UtcNow, mods), JsonOptions, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    private string CachePath(string providerId, string gameId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var provider = SafeSegment(providerId);
        var game = SafeSegment(gameId);
        return Path.Combine(root, $"{provider}-{game}.json");
    }

    private static string SafeSegment(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var safe = new string((value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private sealed record CatalogCacheEnvelope(DateTimeOffset SyncedAt, IReadOnlyList<CatalogMod> Mods);
}

public sealed class ModCatalogService(
    ModCatalogProviderRegistry registry,
    CatalogCacheStore cache,
    TimeSpan? cacheTtl = null)
{
    private readonly TimeSpan ttl = cacheTtl ?? TimeSpan.FromMinutes(15);

    public IReadOnlyList<IModCatalogProvider> Providers { get; } = registry.Providers;

    public async Task<(IReadOnlyList<CatalogMod> Mods, DateTimeOffset? SyncedAt, bool FromCache)> BrowseAsync(
        CatalogBrowseRequest request,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={request.Game.Id}; query={request.Query ?? "<empty>"}; mode={request.Mode}");
        var merged = new List<CatalogMod>();
        DateTimeOffset? oldestSync = null;
        var allFromCache = true;

        foreach (var provider in registry.Providers)
        {
            ct.ThrowIfCancellationRequested();
            if (!request.Game.HasNexusIntegration && provider.ProviderId.Equals("nexus", StringComparison.OrdinalIgnoreCase)) continue;

            var cached = await cache.ReadAsync(provider.ProviderId, request.Game.Id, ct);
            IReadOnlyList<CatalogMod> providerMods;
            DateTimeOffset syncTime;
            var useFreshCache = cached is not null && DateTimeOffset.UtcNow - cached.Value.SyncedAt <= ttl;
            if (useFreshCache)
            {
                providerMods = FilterAndSort(cached!.Value.Mods, request);
                syncTime = cached.Value.SyncedAt;
            }
            else
            {
                try
                {
                    var discoveryRequest = request with { Query = null, Limit = Math.Max(100, request.Limit) };
                    providerMods = request.Mode switch
                    {
                        CatalogBrowseMode.Trending => await provider.GetTrendingModsAsync(discoveryRequest, ct),
                        CatalogBrowseMode.Latest or CatalogBrowseMode.RecentlyUpdated => await provider.GetLatestModsAsync(discoveryRequest, ct),
                        _ => await provider.SearchModsAsync(discoveryRequest, ct)
                    };
                    if (providerMods.Count > 0)
                    {
                        await cache.WriteAsync(provider.ProviderId, request.Game.Id, providerMods, ct);
                        syncTime = DateTimeOffset.UtcNow;
                        providerMods = FilterAndSort(providerMods, request);
                        allFromCache = false;
                    }
                    else if (cached is not null)
                    {
                        providerMods = FilterAndSort(cached.Value.Mods, request);
                        syncTime = cached.Value.SyncedAt;
                    }
                    else
                    {
                        syncTime = DateTimeOffset.UtcNow;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
                {
                    MasterDebugLog.Write("CATALOG", $"Provider {provider.ProviderId} failed; using stale cache when available.", ex);
                    if (cached is null) continue;
                    providerMods = FilterAndSort(cached.Value.Mods, request);
                    syncTime = cached.Value.SyncedAt;
                }
            }

            merged.AddRange(providerMods);
            oldestSync = oldestSync is null || syncTime < oldestSync ? syncTime : oldestSync;
        }

        var deduped = Deduplicate(merged)
            .Take(Math.Clamp(request.Limit, 1, 500))
            .ToArray();
        return (deduped, oldestSync, allFromCache);
    }

    public Task<CatalogMod?> GetModAsync(
        string providerId,
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; game={game.Id}; mod={providerModId}");
        return registry.Get(providerId).GetModAsync(game, providerModId, ct);
    }

    public Task<IReadOnlyList<CatalogModFile>> GetFilesAsync(
        string providerId,
        GameProfile game,
        string providerModId,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; game={game.Id}; mod={providerModId}");
        return registry.Get(providerId).GetDownloadOptionsAsync(game, providerModId, ct);
    }

    public Task<CatalogDownloadResolution> ResolveDownloadAsync(CatalogDownloadRequest request, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={request.Mod.ProviderId}; mod={request.Mod.ProviderModId}; file={request.File.ProviderFileId}");
        return registry.Get(request.Mod.ProviderId).ResolveDownloadAsync(request, ct);
    }

    public Task<CatalogAuthenticationResult> AuthenticateAsync(string providerId, string secret, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={providerId}; secret=<redacted>");
        return registry.Get(providerId).AuthenticateAsync(new(secret), ct);
    }

    public async Task<IReadOnlyList<CatalogProviderHealth>> GetProviderHealthAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new List<CatalogProviderHealth>();
        foreach (var provider in registry.Providers)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await provider.GetHealthAsync(ct));
        }
        return result;
    }

    private static IReadOnlyList<CatalogMod> FilterAndSort(IReadOnlyList<CatalogMod> mods, CatalogBrowseRequest request)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"query={request.Query ?? "<empty>"}; mode={request.Mode}");
        IEnumerable<CatalogMod> query = mods;
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim();
            query = query.Where(mod =>
                mod.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                mod.Author.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                mod.Summary.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (mod.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                mod.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        query = request.Mode switch
        {
            CatalogBrowseMode.Latest => query.OrderByDescending(mod => mod.CreatedAt ?? mod.UpdatedAt),
            CatalogBrowseMode.RecentlyUpdated => query.OrderByDescending(mod => mod.UpdatedAt ?? mod.CreatedAt),
            CatalogBrowseMode.Popular => query.OrderByDescending(mod => mod.Downloads ?? mod.Endorsements ?? 0),
            _ => query.OrderByDescending(mod => mod.Endorsements ?? mod.Downloads ?? 0)
        };
        return query.ToArray();
    }

    private static IReadOnlyList<CatalogMod> Deduplicate(IEnumerable<CatalogMod> mods)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Provider identity is always safe. Cross-provider merging stays deliberately conservative
        // until explicit cross-links or hashes prove two listings are the same project.
        return mods
            .GroupBy(mod => $"{mod.ProviderId}:{mod.ProviderModId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(mod => mod.UpdatedAt ?? mod.CreatedAt).First())
            .OrderByDescending(mod => mod.Endorsements ?? mod.Downloads ?? 0)
            .ToArray();
    }
}

public sealed class CatalogDownloadManager
{
    private readonly string downloadRoot;
    private readonly HttpClient http;
    private readonly long maxBytes;

    public CatalogDownloadManager(string downloadRoot, HttpMessageHandler? handler = null, long maxBytes = 20L * 1024 * 1024 * 1024)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.downloadRoot = Path.GetFullPath(downloadRoot);
        this.maxBytes = maxBytes;
        http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MHW-Mod-Manager/8.8.11");
    }

    public async Task<CatalogDownloadArtifact> DownloadAsync(
        Uri downloadUri,
        CatalogModFile file,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"provider={file.ProviderId}; file={file.ProviderFileId}");
        if (!downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog downloads must use HTTPS.");

        Directory.CreateDirectory(downloadRoot);
        var fileName = SanitizeFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"{file.ProviderId}-{file.ProviderModId}-{file.ProviderFileId}.zip";
        var destination = UniqueDestination(Path.Combine(downloadRoot, fileName));
        var part = destination + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0L;
        if (existing > maxBytes)
        {
            File.Delete(part);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!append) existing = 0;
        var expected = response.Content.Headers.ContentLength;
        if (expected is > 0 && expected.Value + existing > maxBytes)
            throw new InvalidDataException($"Download exceeds the configured {maxBytes / (1024 * 1024)} MiB safety limit.");

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[131072];
            var total = existing;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;
                total = checked(total + read);
                if (total > maxBytes) throw new InvalidDataException("Download exceeded the configured safety limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                if (expected is > 0) progress?.Report(Math.Clamp((total - existing) / (double)expected.Value, 0, 1));
            }
            await output.FlushAsync(ct);
        }

        var length = new FileInfo(part).Length;
        if (length == 0) throw new InvalidDataException("Provider download completed with an empty archive.");
        var sha = await ComputeSha256Async(part, ct);
        File.Move(part, destination, false);
        progress?.Report(1);
        return new(destination, sha, length);
    }

    public async Task<CatalogDownloadArtifact> AcceptLocalArchiveAsync(string archivePath, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"file={Path.GetFileName(archivePath)}");
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path is required.", nameof(archivePath));
        var fullPath = Path.GetFullPath(archivePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The provider download could not be found.", fullPath);
        if (info.Length <= 0) throw new InvalidDataException("The provider download is empty.");
        if (info.Length > maxBytes) throw new InvalidDataException("The provider download exceeds the configured safety limit.");
        var sha = await ComputeSha256Async(fullPath, ct);
        return new(fullPath, sha, info.Length);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"file={Path.GetFileName(path)}");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SanitizeFileName(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var fileName = Path.GetFileName(value ?? string.Empty);
        foreach (var character in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(character, '_');
        fileName = fileName.Trim().TrimEnd('.', ' ');
        return fileName;
    }

    private static string UniqueDestination(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!File.Exists(path) && !File.Exists(path + ".part")) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".part")) return candidate;
        }
    }
}
