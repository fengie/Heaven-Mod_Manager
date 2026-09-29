using System.Net;
using System.Security.Cryptography;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-catalog-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public CatalogTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Theory]
    [InlineData("MAIN", CatalogFileCategory.Main)]
    [InlineData("Main Files", CatalogFileCategory.Main)]
    [InlineData("optional", CatalogFileCategory.Optional)]
    [InlineData("Optional Files", CatalogFileCategory.Optional)]
    [InlineData("update", CatalogFileCategory.Update)]
    [InlineData("Miscellaneous", CatalogFileCategory.Miscellaneous)]
    [InlineData("Old Version", CatalogFileCategory.OldVersion)]
    [InlineData("archived", CatalogFileCategory.Archived)]
    [InlineData("removed", CatalogFileCategory.Removed)]
    [InlineData("brand-new-category", CatalogFileCategory.Unknown)]
    public void Nexus_file_categories_are_preserved_without_guessing_numeric_ids(
        string providerCategory,
        CatalogFileCategory expected)
    {
        Assert.Equal(expected, NexusCatalogProvider.ParseFileCategory(providerCategory));
    }

    [Fact]
    public async Task Catalog_uses_stale_cache_when_provider_is_temporarily_offline()
    {
        var gameRoot = Path.Combine(root, "game-cache");
        Directory.CreateDirectory(gameRoot);
        var game = GameProfile.Generic(
            "fixture",
            "Fixture Game",
            gameRoot,
            "game.exe",
            "mods");

        var provider = new StubProvider("fixture-provider", [CreateMod("fixture-provider", "42", game.Id, "Cached Mod")]);
        var cache = new CatalogCacheStore(Path.Combine(root, "cache"));
        var service = new ModCatalogService(new ModCatalogProviderRegistry([provider]), cache, TimeSpan.Zero);

        var first = await service.BrowseAsync(new(game, Limit: 20), TestToken);
        Assert.Single(first.Mods);
        Assert.False(first.FromCache);

        provider.FailRequests = true;
        var second = await service.BrowseAsync(new(game, Query: "cached", Limit: 20), TestToken);

        var cached = Assert.Single(second.Mods);
        Assert.Equal("Cached Mod", cached.Name);
        Assert.True(second.FromCache);
        Assert.NotNull(second.SyncedAt);
    }

    [Fact]
    public async Task Download_manager_resumes_existing_part_file_and_hashes_completed_archive()
    {
        var bytes = "hello catalog download"u8.ToArray();
        var handler = new RangeFixtureHandler(bytes);
        var downloadRoot = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloadRoot);
        var partial = Path.Combine(downloadRoot, "sample.zip.part");
        await File.WriteAllBytesAsync(partial, bytes[..5], TestToken);

        var file = new CatalogModFile(
            "fixture",
            "mod-1",
            "file-1",
            "Main file",
            "sample.zip",
            CatalogFileCategory.Main,
            SizeBytes: bytes.Length);

        var manager = new CatalogDownloadManager(downloadRoot, handler, 1024 * 1024);
        var artifact = await manager.DownloadAsync(new Uri("https://mods.example.test/sample.zip"), file, ct: TestToken);

        Assert.Equal(5, handler.RequestedRangeFrom);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(artifact.ArchivePath, TestToken));
        Assert.False(File.Exists(partial));
        Assert.Equal(bytes.Length, artifact.Length);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            artifact.Sha256);
    }

    [Fact]
    public async Task Download_manager_rejects_non_https_before_transport()
    {
        var handler = new RangeFixtureHandler("unused"u8.ToArray());
        var manager = new CatalogDownloadManager(Path.Combine(root, "https-only"), handler);
        var file = new CatalogModFile(
            "fixture",
            "mod-1",
            "file-1",
            "Main",
            "sample.zip",
            CatalogFileCategory.Main);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.DownloadAsync(new Uri("http://mods.example.test/sample.zip"), file, ct: TestToken));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Installed_catalog_origin_round_trips_provider_mod_and_file_identity()
    {
        var state = Path.Combine(root, "origin-state");
        var source = Path.Combine(root, "origin-source");
        Directory.CreateDirectory(source);
        var database = new ManagerDatabase(Path.Combine(state, "manager.db"));
        await database.InitializeAsync(TestToken);

        var mod = new ModDescriptor(
            "installed-1",
            "installed-1",
            "Installed fixture",
            source,
            false,
            10);
        await database.UpsertModAsync(mod, TestToken);

        var origin = new InstalledCatalogOrigin(
            mod.Id,
            "nexus",
            "1234",
            "5678",
            "2.0",
            DateTimeOffset.UtcNow,
            "https://www.nexusmods.com/monsterhunterworld/mods/1234",
            new string('a', 64),
            """{"fixture":true}""");
        await database.UpsertCatalogOriginAsync(origin, TestToken);

        var all = await database.GetCatalogOriginsAsync(TestToken);
        Assert.True(all.TryGetValue(mod.Id, out var stored));
        Assert.NotNull(stored);

        Assert.Equal("nexus", stored.ProviderId);
        Assert.Equal("1234", stored.ProviderModId);
        Assert.Equal("5678", stored.ProviderFileId);
        Assert.Equal("2.0", stored.InstalledVersion);
        Assert.Equal(origin.ArchiveSha256, stored.ArchiveSha256);

        var byProvider = await database.GetCatalogOriginsForProviderModAsync("nexus", "1234", TestToken);
        Assert.Single(byProvider);
        Assert.Equal(mod.Id, byProvider[0].ModId);
    }

    [Fact]
    public async Task Catalog_keeps_other_provider_results_when_one_provider_is_offline()
    {
        var gameRoot = Path.Combine(root, "game-provider-isolation");
        Directory.CreateDirectory(gameRoot);
        var game = GameProfile.Generic("fixture", "Fixture Game", gameRoot, "game.exe", "mods");

        var offline = new StubProvider("offline-provider", [CreateMod("offline-provider", "1", game.Id, "Offline Mod")])
        {
            FailRequests = true
        };
        var healthy = new StubProvider("healthy-provider", [CreateMod("healthy-provider", "2", game.Id, "Healthy Mod")]);
        var service = new ModCatalogService(
            new ModCatalogProviderRegistry([offline, healthy]),
            new CatalogCacheStore(Path.Combine(root, "provider-isolation-cache")),
            TimeSpan.Zero);

        var result = await service.BrowseAsync(new(game, Limit: 20), TestToken);

        var mod = Assert.Single(result.Mods);
        Assert.Equal("healthy-provider", mod.ProviderId);
        Assert.Equal("Healthy Mod", mod.Name);
    }

    [Fact]
    public async Task Catalog_does_not_fuzzy_merge_same_named_mods_from_different_providers()
    {
        var gameRoot = Path.Combine(root, "game-dedup");
        Directory.CreateDirectory(gameRoot);
        var game = GameProfile.Generic("fixture", "Fixture Game", gameRoot, "game.exe", "mods");

        var first = new StubProvider("provider-a", [CreateMod("provider-a", "10", game.Id, "Same Name")]);
        var second = new StubProvider("provider-b", [CreateMod("provider-b", "20", game.Id, "Same Name")]);
        var service = new ModCatalogService(
            new ModCatalogProviderRegistry([first, second]),
            new CatalogCacheStore(Path.Combine(root, "dedup-cache")),
            TimeSpan.Zero);

        var result = await service.BrowseAsync(new(game, Limit: 20), TestToken);

        Assert.Equal(2, result.Mods.Count);
        Assert.Contains(result.Mods, mod => mod.ProviderId == "provider-a");
        Assert.Contains(result.Mods, mod => mod.ProviderId == "provider-b");
    }

    [Fact]
    public async Task Download_manager_rejects_mismatched_content_range_without_touching_partial_file()
    {
        var bytes = "hello catalog download"u8.ToArray();
        var downloadRoot = Path.Combine(root, "bad-range");
        Directory.CreateDirectory(downloadRoot);
        var partial = Path.Combine(downloadRoot, "sample.zip.part");
        var prefix = bytes[..5];
        await File.WriteAllBytesAsync(partial, prefix, TestToken);

        var file = new CatalogModFile(
            "fixture",
            "mod-1",
            "file-1",
            "Main file",
            "sample.zip",
            CatalogFileCategory.Main,
            SizeBytes: bytes.Length);

        var manager = new CatalogDownloadManager(downloadRoot, new MismatchedRangeFixtureHandler(bytes), 1024 * 1024);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.DownloadAsync(new Uri("https://mods.example.test/sample.zip"), file, ct: TestToken));
        Assert.Equal(prefix, await File.ReadAllBytesAsync(partial, TestToken));
    }

    private static CatalogMod CreateMod(string providerId, string providerModId, string gameId, string name)
    {
        return new(
            CatalogMod.BuildCanonicalId(providerId, providerModId),
            providerId,
            providerModId,
            gameId,
            name,
            "fixture summary",
            "fixture description",
            "Fixture Author",
            "1.0",
            "Gameplay",
            ["fixture"],
            null,
            [],
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            100,
            10,
            null,
            [],
            $"https://mods.example.test/{providerModId}",
            []);
    }

    private sealed class StubProvider(string providerId, IReadOnlyList<CatalogMod> mods) : IModCatalogProvider
    {
        public bool FailRequests { get; set; }
        public string ProviderId { get; } = providerId;
        public string DisplayName { get; } = "Fixture Provider";
        public CatalogProviderCapabilities Capabilities { get; } =
            CatalogProviderCapabilities.Search |
            CatalogProviderCapabilities.Browse |
            CatalogProviderCapabilities.Metadata |
            CatalogProviderCapabilities.FileList;

        public Task<CatalogAuthenticationResult> AuthenticateAsync(CatalogAuthenticationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CatalogAuthenticationResult(true, "fixture"));

        public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogGame>>([]);

        public Task<IReadOnlyList<CatalogMod>> SearchModsAsync(CatalogBrowseRequest request, CancellationToken ct = default) =>
            ReturnMods();

        public Task<IReadOnlyList<CatalogMod>> GetTrendingModsAsync(CatalogBrowseRequest request, CancellationToken ct = default) =>
            ReturnMods();

        public Task<IReadOnlyList<CatalogMod>> GetLatestModsAsync(CatalogBrowseRequest request, CancellationToken ct = default) =>
            ReturnMods();

        public Task<CatalogMod?> GetModAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult(mods.FirstOrDefault(mod => mod.ProviderModId == providerModId));

        public Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogModFile>>([]);

        public Task<IReadOnlyList<CatalogDependency>> GetDependenciesAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogDependency>>([]);

        public Task<IReadOnlyList<CatalogImage>> GetScreenshotsAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogImage>>([]);

        public Task<IReadOnlyList<CatalogModFile>> GetDownloadOptionsAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogModFile>>([]);

        public Task<CatalogDownloadResolution> ResolveDownloadAsync(CatalogDownloadRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CatalogDownloadResolution(false, null, null, null, "fixture"));

        public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new CatalogProviderHealth(ProviderId, CatalogProviderState.Connected, "fixture"));

        private Task<IReadOnlyList<CatalogMod>> ReturnMods()
        {
            if (FailRequests) throw new HttpRequestException("fixture provider offline");
            return Task.FromResult(mods);
        }
    }

    private sealed class MismatchedRangeFixtureHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = request
            };
            response.Content.Headers.ContentRange = new(0, payload.LongLength - 1, payload.LongLength) { Unit = "bytes" };
            return Task.FromResult(response);
        }
    }

    private sealed class RangeFixtureHandler(byte[] payload) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public long? RequestedRangeFrom { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var offset = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            RequestedRangeFrom = request.Headers.Range is null ? null : offset;
            var body = payload[(int)offset..];

            var response = new HttpResponseMessage(
                request.Headers.Range is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = request
            };
            if (request.Headers.Range is not null)
                response.Content.Headers.ContentRange = new(offset, payload.LongLength - 1, payload.LongLength) { Unit = "bytes" };
            return Task.FromResult(response);
        }
    }
}
