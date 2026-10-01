using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogBrowserViewModelTests:IDisposable
{
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"mhwmm-catalog-browser-"+Guid.NewGuid().ToString("N"));

    public CatalogBrowserViewModelTests()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try{Directory.Delete(root,true);}catch{}
    }

    [Fact]
    public async Task Initial_refresh_keeps_stale_cache_when_one_provider_fails()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var repository=await CreateRepositoryAsync("refresh");
        var game=GameProfile.MonsterHunterWorld(Path.Combine(root,"game"));
        await repository.UpsertAsync(CreateCached("alpha","old","Old cached mod",game.Id,DateTimeOffset.UtcNow.AddHours(-2),stale:true),TestToken);

        var offline=new FakeProvider("alpha"){SearchException=new HttpRequestException("offline")};
        var healthy=new FakeProvider("beta");
        using var vm=new CatalogBrowserViewModel(repository,new CatalogSyncService(repository),[offline,healthy],game);

        await vm.EnsureLoadedAsync(TestToken);

        Assert.Contains(vm.Results,item=>item.CanonicalId=="alpha:old");
        Assert.Contains(vm.Results,item=>item.CanonicalId=="beta:mod-1");
        Assert.Contains("cached only",vm.ProviderStatusText,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1,healthy.SearchCalls);
    }

    [Fact]
    public async Task Local_search_provider_filter_and_sort_use_cached_index()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var repository=await CreateRepositoryAsync("filters");
        var game=GameProfile.MonsterHunterWorld(Path.Combine(root,"game-filter"));
        var now=DateTimeOffset.UtcNow;
        await repository.UpsertAsync(CreateCached("alpha","one","Needle Utility",game.Id,now.AddMinutes(-20),downloads:12,rating:4.9),TestToken);
        await repository.UpsertAsync(CreateCached("beta","two","Needle Visuals",game.Id,now.AddMinutes(-5),downloads:900,rating:3.2),TestToken);
        await repository.UpsertAsync(CreateCached("beta","three","Other Mod",game.Id,now,downloads:1200,rating:5.0),TestToken);

        using var vm=new CatalogBrowserViewModel(
            repository,
            new CatalogSyncService(repository),
            [new FakeProvider("alpha"),new FakeProvider("beta")],
            game);

        vm.SearchText="Needle";
        vm.SelectedProvider=vm.ProviderFilters.Single(filter=>filter.Id=="beta");
        vm.SelectedSortMode="Most downloaded";
        await vm.ReloadAsync(TestToken);

        var result=Assert.Single(vm.Results);
        Assert.Equal("beta:two",result.CanonicalId);
        Assert.Equal("900 downloads • 3.2 rating",result.StatsLine);
    }

    [Fact]
    public async Task Installed_origin_check_stays_pinned_to_exact_file_identity()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var db=new ManagerDatabase(Path.Combine(root,"origin-flow","manager.db"));
        await db.InitializeAsync(TestToken);
        var repository=new CatalogRepository(db);
        var origins=new InstalledCatalogOriginRepository(db);
        var game=GameProfile.MonsterHunterWorld(Path.Combine(root,"origin-game"));
        var oldFile=new CatalogModFile(
            "alpha",
            "mod-1",
            "file-old",
            "Installed file",
            "installed.zip",
            CatalogFileCategory.Main,
            Version:"1.0");
        var newerFile=new CatalogModFile(
            "alpha",
            "mod-1",
            "file-new",
            "Newer file",
            "newer.zip",
            CatalogFileCategory.Main,
            Version:"2.0");
        var exactMod=new CatalogMod(
            "alpha:mod-1",
            "alpha",
            "mod-1",
            game.Id,
            "Exact Origin Fixture",
            "summary",
            "description",
            "author",
            "2.0",
            "Utility",
            [],
            null,
            [],
            null,
            DateTimeOffset.UtcNow,
            10,
            null,
            null,
            [],
            "https://mods.example.test/alpha/mod-1",
            [oldFile,newerFile]);
        await repository.UpsertAsync(
            new CachedCatalogMod(
                exactMod,
                new CatalogCacheMetadata(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1))),
            TestToken);
        await origins.UpsertAsync(
            new InstalledCatalogOrigin(
                "local-mod",
                "alpha",
                "mod-1",
                "file-old",
                "1.0",
                DateTimeOffset.UtcNow.AddDays(-1),
                "https://mods.example.test/alpha/mod-1",
                new string('a',64)),
            TestToken);

        var provider=new FakeProvider("alpha")
        {
            ExactMod=exactMod,
            ExactFiles=[oldFile,newerFile]
        };
        using var vm=new CatalogBrowserViewModel(
            repository,
            new CatalogSyncService(repository),
            [provider],
            game,
            origins,
            new InstalledCatalogOriginChecker());

        await vm.ReloadAsync(TestToken);
        vm.SelectedMod=Assert.Single(vm.Results);
        await vm.CheckSelectedInstalledOriginAsync(TestToken);

        Assert.True(vm.HasInstalledOrigin);
        Assert.Contains("exact installed file still resolves",vm.InstalledOriginStatusText,StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mod-1",provider.LastGetModId);
        Assert.Equal("mod-1",provider.LastGetFilesModId);
        Assert.Contains("file-new",provider.ExactFiles.Select(file=>file.ProviderFileId));
        Assert.DoesNotContain("newer file",vm.InstalledOriginStatusText,StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CatalogRepository> CreateRepositoryAsync(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var db=new ManagerDatabase(Path.Combine(root,name,"manager.db"));
        await db.InitializeAsync(TestToken);
        return new CatalogRepository(db);
    }

    private static CachedCatalogMod CreateCached(
        string providerId,
        string providerModId,
        string name,
        string gameId,
        DateTimeOffset fetchedAt,
        bool stale=false,
        long? downloads=null,
        double? rating=null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mod=new CatalogMod(
            CatalogMod.BuildCanonicalId(providerId,providerModId),
            providerId,
            providerModId,
            gameId,
            name,
            "A searchable catalog summary",
            "A detailed catalog description",
            "Fixture Author",
            "1.0",
            "Utility",
            ["fixture"],
            null,
            [],
            fetchedAt.AddDays(-10),
            fetchedAt,
            downloads,
            null,
            rating,
            [],
            $"https://mods.example.test/{providerId}/{providerModId}",
            []);
        return new CachedCatalogMod(
            mod,
            new CatalogCacheMetadata(
                fetchedAt,
                stale?fetchedAt.AddMinutes(-1):fetchedAt.AddHours(1)));
    }

    private sealed class FakeProvider(string providerId):IModCatalogProvider
    {
        public string ProviderId=>providerId;
        public string DisplayName=>$"{providerId} source";
        public CatalogProviderCapabilities Capabilities=>ExactMod is null
            ? CatalogProviderCapabilities.Browse|CatalogProviderCapabilities.Metadata
            : CatalogProviderCapabilities.Browse|CatalogProviderCapabilities.Metadata|CatalogProviderCapabilities.Updates;
        public CatalogProviderCompliance Compliance=>NexusV3CatalogPolicy.Compliance with{ProviderId=providerId};
        public Exception? SearchException{get;set;}
        public CatalogMod? ExactMod{get;set;}
        public IReadOnlyList<CatalogModFile> ExactFiles{get;set;}=[];
        public string? LastGetModId{get;private set;}
        public string? LastGetFilesModId{get;private set;}
        public int SearchCalls{get;private set;}

        public Task<IReadOnlyList<CatalogGame>> GetGamesAsync(CancellationToken ct=default)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Task.FromResult<IReadOnlyList<CatalogGame>>([]);
        }

        public Task<IReadOnlyList<CatalogMod>> SearchModsAsync(CatalogBrowseRequest request,CancellationToken ct=default)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            SearchCalls++;
            ct.ThrowIfCancellationRequested();
            if(SearchException is not null)return Task.FromException<IReadOnlyList<CatalogMod>>(SearchException);

            IReadOnlyList<CatalogMod> mods=
            [
                new(
                    CatalogMod.BuildCanonicalId(providerId,"mod-1"),
                    providerId,
                    "mod-1",
                    request.Game.Id,
                    $"{providerId} live mod",
                    "summary",
                    "description",
                    "author",
                    "1.0",
                    "Utility",
                    [],
                    null,
                    [],
                    null,
                    DateTimeOffset.UtcNow,
                    1,
                    null,
                    null,
                    [],
                    $"https://mods.example.test/{providerId}/mod-1",
                    [])
            ];
            return Task.FromResult(mods);
        }

        public Task<CatalogMod?> GetModAsync(GameProfile game,string providerModId,CancellationToken ct=default)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            ct.ThrowIfCancellationRequested();
            LastGetModId=providerModId;
            return Task.FromResult(
                ExactMod is not null&&ExactMod.ProviderModId.Equals(providerModId,StringComparison.Ordinal)
                    ? ExactMod
                    : null);
        }

        public Task<IReadOnlyList<CatalogModFile>> GetModFilesAsync(GameProfile game,string providerModId,CancellationToken ct=default)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            ct.ThrowIfCancellationRequested();
            LastGetFilesModId=providerModId;
            return Task.FromResult<IReadOnlyList<CatalogModFile>>(ExactFiles);
        }

        public Task<CatalogAcquisitionResolution> ResolveAcquisitionAsync(CatalogAcquisitionRequest request,CancellationToken ct=default)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            throw new NotSupportedException();
        }

        public Task<CatalogProviderHealth> GetHealthAsync(CancellationToken ct=default)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Task.FromResult(new CatalogProviderHealth(providerId,CatalogProviderState.Connected,"connected"));
        }
    }
}
