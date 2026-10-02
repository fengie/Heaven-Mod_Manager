using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogDiscoveryServiceTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-discovery-" + Guid.NewGuid().ToString("N"));

    public CatalogDiscoveryServiceTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task One_provider_failure_does_not_block_other_providers()
    {
        var repository = await CreateRepositoryAsync("partial");
        var service = new CatalogDiscoveryService(new CatalogSyncService(repository));
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game"));
        var offline = new FakeProvider("alpha")
        {
            SearchException = new HttpRequestException("offline"),
            Health = new("alpha", CatalogProviderState.Offline, "offline")
        };
        var healthy = new FakeProvider("beta");

        var result = await service.RefreshAsync(
            [healthy, offline],
            new CatalogBrowseRequest(game),
            new CatalogSyncOptions(TimeSpan.FromMinutes(10), HydrateFiles: false),
            TestToken);

        Assert.Equal(1, result.SuccessfulProviders);
        Assert.Equal(1, result.FailedProviders);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(["alpha", "beta"], result.Providers.Select(x => x.ProviderId).ToArray());
        Assert.Equal(CatalogSyncFailureKind.Offline, result.Providers[0].FailureKind);
        Assert.True(result.Providers[1].Succeeded);
        Assert.NotNull(await repository.GetAsync("beta:mod-1", TestToken));
    }

    [Fact]
    public async Task Explicit_search_only_runs_search_capable_providers_and_isolates_failures()
    {
        var repository = await CreateRepositoryAsync("explicit-search");
        var service = new CatalogDiscoveryService(new CatalogSyncService(repository));
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game-search"));
        var unsupported = new FakeProvider(
            "alpha",
            CatalogProviderCapabilities.Browse | CatalogProviderCapabilities.Metadata);
        var healthy = new FakeProvider("beta");
        var offline = new FakeProvider("gamma")
        {
            SearchException = new HttpRequestException("offline"),
            Health = new("gamma", CatalogProviderState.Offline, "offline")
        };

        var result = await service.SearchAsync(
            [offline, unsupported, healthy],
            game,
            "  dragon blade  ",
            37,
            new CatalogSyncOptions(TimeSpan.FromMinutes(10), HydrateFiles: false),
            TestToken);

        Assert.Equal(0, unsupported.SearchCalls);
        Assert.Equal(1, healthy.SearchCalls);
        Assert.Equal(1, offline.SearchCalls);
        Assert.NotNull(healthy.LastRequest);
        Assert.Equal("dragon blade", healthy.LastRequest!.Query);
        Assert.Equal(37, healthy.LastRequest.Limit);
        Assert.Equal(["beta", "gamma"], result.Providers.Select(provider => provider.ProviderId).ToArray());
        Assert.Equal(1, result.SuccessfulProviders);
        Assert.Equal(1, result.FailedProviders);
        Assert.Equal(CatalogSyncFailureKind.Offline, result.Providers[1].FailureKind);
        Assert.NotNull(await repository.GetAsync("beta:mod-1", TestToken));
    }

    [Fact]
    public async Task Duplicate_provider_ids_fail_before_any_provider_runs()
    {
        var repository = await CreateRepositoryAsync("duplicates");
        var service = new CatalogDiscoveryService(new CatalogSyncService(repository));
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game-dup"));
        var first = new FakeProvider("same");
        var second = new FakeProvider("SAME");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RefreshAsync(
                [first, second],
                new CatalogBrowseRequest(game),
                ct: TestToken));

        Assert.Equal(0, first.SearchCalls);
        Assert.Equal(0, second.SearchCalls);
    }

    [Fact]
    public async Task Caller_cancellation_stops_before_later_provider_runs()
    {
        var repository = await CreateRepositoryAsync("cancel");
        var service = new CatalogDiscoveryService(new CatalogSyncService(repository));
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game-cancel"));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var first = new FakeProvider("alpha")
        {
            OnSearch = () => cts.Cancel()
        };
        var second = new FakeProvider("beta");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RefreshAsync(
                [first, second],
                new CatalogBrowseRequest(game),
                new CatalogSyncOptions(TimeSpan.FromMinutes(10), HydrateFiles: false),
                cts.Token));

        Assert.Equal(1, first.SearchCalls);
        Assert.Equal(0, second.SearchCalls);
    }

    private async Task<CatalogRepository> CreateRepositoryAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name, "manager.db"));
        await db.InitializeAsync(TestToken);
        return new CatalogRepository(db);
    }

    private sealed class FakeProvider(
        string providerId,
        CatalogProviderCapabilities? capabilities = null) : IModCatalogProvider
    {
        public string ProviderId => providerId;
        public string DisplayName => providerId;
        public CatalogProviderCapabilities Capabilities =>
            capabilities ??
            (CatalogProviderCapabilities.Search | CatalogProviderCapabilities.Browse | CatalogProviderCapabilities.Metadata);
        public CatalogProviderCompliance Compliance =>
            NexusV3CatalogPolicy.Compliance with { ProviderId = providerId };
        public Exception? SearchException { get; set; }
        public Action? OnSearch { get; set; }
        public int SearchCalls { get; private set; }
        public CatalogBrowseRequest? LastRequest { get; private set; }
        public CatalogProviderHealth Health { get; set; } =
            new(providerId, CatalogProviderState.Connected, "connected");

        public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogGame>>([]);

        public Task<IReadOnlyList<CatalogMod>> SearchModsAsync(CatalogBrowseRequest request, CancellationToken ct = default)
        {
            SearchCalls++;
            LastRequest = request;
            OnSearch?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (SearchException is not null)
                return Task.FromException<IReadOnlyList<CatalogMod>>(SearchException);

            IReadOnlyList<CatalogMod> mods =
            [
                new(
                    CatalogMod.BuildCanonicalId(providerId, "mod-1"),
                    providerId,
                    "mod-1",
                    request.Game.Id,
                    $"{providerId} fixture mod",
                    "summary",
                    "description",
                    "author",
                    "1.0",
                    "Utility",
                    [],
                    null,
                    [],
                    null,
                    null,
                    1,
                    null,
                    null,
                    [],
                    $"https://mods.example.test/{providerId}/mod-1",
                    [])
            ];
            return Task.FromResult(mods);
        }

        public Task<CatalogMod?> GetModAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult<CatalogMod?>(null);

        public Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(GameProfile game, string providerModId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogModFile>>([]);

        public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(CatalogAcquisitionRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(Health);
    }
}
