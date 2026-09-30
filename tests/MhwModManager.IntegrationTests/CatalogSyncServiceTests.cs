using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogSyncServiceTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhwmm-catalog-sync-" + Guid.NewGuid().ToString("N"));

    public CatalogSyncServiceTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Sync_hydrates_files_and_persists_cache_sync_and_rate_state()
    {
        var repository = await CreateRepositoryAsync("success");
        var now = new DateTimeOffset(2026, 9, 30, 4, 15, 0, TimeSpan.Zero);
        var provider = new FakeProvider();
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game"));
        var service = new CatalogSyncService(repository, new FixedTimeProvider(now));

        var result = await service.SyncAsync(
            provider,
            new CatalogBrowseRequest(game, "dragon blade", CatalogBrowseMode.Trending, 25),
            new CatalogSyncOptions(TimeSpan.FromMinutes(20)),
            TestToken);

        Assert.Equal("fixture", result.ProviderId);
        Assert.Equal(1, result.ItemCount);
        Assert.DoesNotContain("dragon blade", result.ScopeKey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, provider.FileHydrationCalls);

        var stored = await repository.GetAsync("fixture:mod-1", TestToken);
        Assert.NotNull(stored);
        Assert.Equal(now, stored!.Cache.FetchedAt);
        Assert.Equal(now.AddMinutes(20), stored.Cache.ExpiresAt);
        Assert.StartsWith("sha256:", stored.Cache.SourceFingerprint, StringComparison.Ordinal);
        Assert.Single(stored.Mod.Files);

        var sync = await repository.GetSyncStateAsync("fixture", result.ScopeKey, TestToken);
        Assert.NotNull(sync);
        Assert.Equal(CatalogSyncFailureKind.None, sync!.LastFailureKind);
        Assert.Equal(now, sync.LastSuccessAt);
        Assert.Equal(now, sync.LastAttemptAt);

        var rate = await repository.GetRateStateAsync("fixture", result.ScopeKey, TestToken);
        Assert.NotNull(rate);
        Assert.Equal(42, rate!.RateLimit.HourlyRemaining);
    }

    [Fact]
    public async Task Invalid_batch_is_rejected_before_any_catalog_item_is_written()
    {
        var repository = await CreateRepositoryAsync("invalid-batch");
        var now = new DateTimeOffset(2026, 9, 30, 4, 30, 0, TimeSpan.Zero);
        var provider = new FakeProvider
        {
            SearchResults =
            [
                CreateMod("fixture", "mod-1"),
                CreateMod("wrong-provider", "mod-2")
            ]
        };
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game-invalid"));
        var service = new CatalogSyncService(repository, new FixedTimeProvider(now));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SyncAsync(
                provider,
                new CatalogBrowseRequest(game),
                new CatalogSyncOptions(TimeSpan.FromMinutes(20), HydrateFiles: false),
                TestToken));

        Assert.Empty(await repository.SearchAsync(
            query: null,
            providerId: "fixture",
            gameId: game.Id,
            ct: TestToken));

        Assert.NotNull(provider.LastRequest);
        var scope = BuildExpectedScope(game, CatalogBrowseMode.Trending, null);
        var state = await repository.GetSyncStateAsync("fixture", scope, TestToken);
        Assert.NotNull(state);
        Assert.Equal(CatalogSyncFailureKind.SchemaDrift, state!.LastFailureKind);
        Assert.Null(state.LastSuccessAt);
    }

    [Fact]
    public async Task Provider_failure_preserves_existing_cache_and_records_failure()
    {
        var repository = await CreateRepositoryAsync("stale-preserved");
        var now = new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero);
        var time = new FixedTimeProvider(now);
        var provider = new FakeProvider();
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game-offline"));
        var service = new CatalogSyncService(repository, time);

        var first = await service.SyncAsync(
            provider,
            new CatalogBrowseRequest(game),
            new CatalogSyncOptions(TimeSpan.FromMinutes(1), HydrateFiles: false),
            TestToken);

        provider.SearchException = new HttpRequestException("offline");
        provider.Health = new CatalogProviderHealth(
            "fixture",
            CatalogProviderState.Offline,
            "Fixture provider offline.",
            CheckedAt: now.AddMinutes(2));
        time.SetUtcNow(now.AddMinutes(2));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.SyncAsync(
                provider,
                new CatalogBrowseRequest(game),
                new CatalogSyncOptions(TimeSpan.FromMinutes(1), HydrateFiles: false),
                TestToken));

        var cached = await repository.GetAsync("fixture:mod-1", TestToken);
        Assert.NotNull(cached);
        Assert.True(cached!.IsStale(time.GetUtcNow()));

        var state = await repository.GetSyncStateAsync("fixture", first.ScopeKey, TestToken);
        Assert.NotNull(state);
        Assert.Equal(CatalogSyncFailureKind.Offline, state!.LastFailureKind);
        Assert.Equal(now, state.LastSuccessAt);
        Assert.Equal(now.AddMinutes(2), state.LastAttemptAt);
    }

    private async Task<CatalogRepository> CreateRepositoryAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name, "manager.db"));
        await db.InitializeAsync(TestToken);
        return new CatalogRepository(db);
    }

    private static CatalogMod CreateMod(string providerId, string providerModId)
    {
        var canonicalId = CatalogMod.BuildCanonicalId(providerId, providerModId);
        return new CatalogMod(
            canonicalId,
            providerId,
            providerModId,
            "monster-hunter-world",
            "Fixture Dragon Blade",
            "Fixture summary",
            "Fixture description",
            "Fixture Author",
            "1.0.0",
            "Weapons",
            ["fixture", "weapon"],
            null,
            [],
            DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-09-29T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            100,
            10,
            4.5,
            [],
            $"https://mods.example.test/{providerModId}",
            []);
    }

    private static string BuildExpectedScope(
        GameProfile game,
        CatalogBrowseMode mode,
        string? query)
    {
        if (!string.IsNullOrWhiteSpace(query))
            throw new NotSupportedException("This fixture only predicts the no-query scope.");
        return $"{game.Id}:{mode.ToString().ToLowerInvariant()}:all";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset value = now;

        public override DateTimeOffset GetUtcNow() => value;

        public void SetUtcNow(DateTimeOffset next) => value = next;
    }

    private sealed class FakeProvider : IModCatalogProvider
    {
        public string ProviderId => "fixture";
        public string DisplayName => "Fixture Catalog";
        public CatalogProviderCapabilities Capabilities =>
            CatalogProviderCapabilities.Search |
            CatalogProviderCapabilities.Browse |
            CatalogProviderCapabilities.Metadata |
            CatalogProviderCapabilities.FileList;

        public CatalogProviderCompliance Compliance =>
            NexusV3CatalogPolicy.Compliance with { ProviderId = ProviderId };

        public IReadOnlyList<CatalogMod> SearchResults { get; set; } =
            [CreateMod("fixture", "mod-1")];

        public Exception? SearchException { get; set; }

        public CatalogProviderHealth Health { get; set; } = new(
            "fixture",
            CatalogProviderState.Connected,
            "Fixture provider connected.",
            new CatalogRateLimit(100, 42, 1000, 420, null, DateTimeOffset.Parse("2026-09-30T04:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            DateTimeOffset.Parse("2026-09-30T04:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        public CatalogBrowseRequest? LastRequest { get; private set; }

        public int FileHydrationCalls { get; private set; }

        public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogGame>>(
                [new("monster-hunter-world", "Monster Hunter: World", "fixture-game")]);

        public Task<IReadOnlyList<CatalogMod>> SearchModsAsync(
            CatalogBrowseRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastRequest = request;
            if (SearchException is not null)
                return Task.FromException<IReadOnlyList<CatalogMod>>(SearchException);
            return Task.FromResult(SearchResults);
        }

        public Task<CatalogMod?> GetModAsync(
            GameProfile game,
            string providerModId,
            CancellationToken ct = default) =>
            Task.FromResult<CatalogMod?>(SearchResults.FirstOrDefault(
                mod => mod.ProviderModId == providerModId));

        public Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(
            GameProfile game,
            string providerModId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            FileHydrationCalls++;
            IReadOnlyList<CatalogModFile> files =
            [
                new(
                    ProviderId,
                    providerModId,
                    "file-1",
                    "Main File",
                    "fixture.zip",
                    CatalogFileCategory.Main,
                    "1.0.0",
                    12345,
                    "Fixture archive",
                    DateTimeOffset.Parse("2026-09-29T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
            ];
            return Task.FromResult(files);
        }

        public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(
            CatalogAcquisitionRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new CatalogAcquisitionResolution(
                CatalogAcquisitionKind.Assisted,
                null,
                new Uri(request.Mod.SourceUrl),
                null,
                "Fixture assisted acquisition."));

        public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Health);
        }
    }
}
