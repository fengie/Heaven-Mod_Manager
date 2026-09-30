using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.App.ViewModels;

public sealed record CatalogProviderFilter(string? Id,string DisplayName)
{
    public static CatalogProviderFilter All
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new(null,"All sources");
        }
    }
}

public sealed record CatalogBrowserItem(
    string CanonicalId,
    string ProviderName,
    string Name,
    string Summary,
    string Description,
    string AuthorVersionLine,
    string CategoryLabel,
    string StatsLine,
    string UpdatedLine,
    string FilesLine,
    string TagsLine,
    string FreshnessLabel,
    string SourceHint,
    string SourceUrl,
    DateTimeOffset SortDate,
    long SortDownloads,
    double SortRating)
{
    public static CatalogBrowserItem Create(CachedCatalogMod cached,string providerName,DateTimeOffset now)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(cached);
        var mod=cached.Mod;
        var summary=string.IsNullOrWhiteSpace(mod.Summary)?"No summary supplied by this source.":mod.Summary.Trim();
        var description=string.IsNullOrWhiteSpace(mod.Description)?summary:mod.Description.Trim();
        var author=string.IsNullOrWhiteSpace(mod.Author)?"Unknown author":mod.Author.Trim();
        var version=string.IsNullOrWhiteSpace(mod.Version)?"Version not listed":$"v{mod.Version.Trim()}";
        var category=string.IsNullOrWhiteSpace(mod.Category)?"Uncategorized":mod.Category.Trim();
        var updated=mod.UpdatedAt??mod.CreatedAt??cached.Cache.FetchedAt;
        var tags=mod.Tags.Where(tag=>!string.IsNullOrWhiteSpace(tag)).Take(8).ToArray();

        return new CatalogBrowserItem(
            mod.CanonicalId,
            providerName,
            mod.Name,
            summary,
            description,
            $"{author} • {version}",
            category,
            BuildStatsLine(mod),
            $"Updated {updated.LocalDateTime:MMM d, yyyy}",
            mod.Files.Count==0?"Files available from source":$"{mod.Files.Count} indexed file{(mod.Files.Count==1?string.Empty:"s")}",
            tags.Length==0?"No tags listed":string.Join("  •  ",tags),
            cached.IsStale(now)?"Cached • refresh recommended":"Fresh cache",
            "Provider-controlled downloads stay on the source's authorized flow.",
            mod.SourceUrl,
            updated,
            mod.Downloads??0,
            mod.Rating??0);
    }

    private static string BuildStatsLine(CatalogMod mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var parts=new List<string>(3);
        if(mod.Downloads is not null)parts.Add($"{mod.Downloads.Value:N0} downloads");
        if(mod.Endorsements is not null)parts.Add($"{mod.Endorsements.Value:N0} endorsements");
        if(mod.Rating is not null)parts.Add($"{mod.Rating.Value:0.0} rating");
        return parts.Count==0?"Stats not supplied":string.Join(" • ",parts);
    }
}

public sealed partial class CatalogBrowserViewModel:ObservableObject,IDisposable
{
    private readonly string[] sortModes=["Recently updated","Most downloaded","Highest rated","Name"];

    private readonly CatalogRepository repository;
    private readonly CatalogSyncService syncService;
    private readonly IModCatalogProvider[] providers;
    private readonly GameProfile game;
    private readonly Dictionary<string,string> providerNames;
    private readonly CancellationTokenSource lifetimeCts=new();
    private readonly SemaphoreSlim refreshGate=new(1,1);
    private bool loaded;
    private bool disposed;

    public ObservableRangeCollection<CatalogBrowserItem> Results{get;}=[];
    public ObservableRangeCollection<CatalogProviderFilter> ProviderFilters{get;}=[];
    public IReadOnlyList<string> SortModes
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return sortModes;
        }
    }

    [ObservableProperty]private string searchText="";
    [ObservableProperty]private CatalogProviderFilter selectedProvider=CatalogProviderFilter.All;
    [ObservableProperty]private string selectedSortMode="Recently updated";
    [ObservableProperty]private CatalogBrowserItem? selectedMod;
    [ObservableProperty]private bool isRefreshing;
    [ObservableProperty]private string statusText="Open Browse to load the local catalog.";
    [ObservableProperty]private string providerStatusText="Sources have not been refreshed yet.";

    public string ResultCountLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"{Results.Count} result{(Results.Count==1?string.Empty:"s")}";
        }
    }

    public CatalogBrowserViewModel(
        CatalogRepository repository,
        CatalogSyncService syncService,
        IEnumerable<IModCatalogProvider> providers,
        GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(game);

        this.repository=repository;
        this.syncService=syncService;
        this.providers=providers
            .Where(provider=>provider is not null&&provider.Compliance.Enabled)
            .OrderBy(provider=>provider.DisplayName,StringComparer.OrdinalIgnoreCase)
            .ToArray();
        this.game=game;
        providerNames=this.providers.ToDictionary(
            provider=>provider.ProviderId,
            provider=>provider.DisplayName,
            StringComparer.OrdinalIgnoreCase);

        ProviderFilters.ReplaceAll(
            new[]{CatalogProviderFilter.All}.Concat(
                this.providers.Select(provider=>new CatalogProviderFilter(provider.ProviderId,provider.DisplayName))));
        selectedProvider=ProviderFilters[0];
        ProviderStatusText=this.providers.Length==0
            ?"No live catalog sources are enabled for this game yet."
            :string.Join(" • ",this.providers.Select(provider=>$"{provider.DisplayName}: ready"));
    }

    public async Task EnsureLoadedAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(disposed||loaded)return;
        loaded=true;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetimeCts.Token);
        await ReloadAsync(linked.Token);
        if(providers.Length==0)return;
        await RefreshCatalogCoreAsync(linked.Token);
    }

    public Task ReloadAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ReloadFromCacheAsync(ct);
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ReloadFromCacheAsync(lifetimeCts.Token);
    }

    [RelayCommand]
    private Task RefreshCatalogAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return RefreshCatalogCoreAsync(lifetimeCts.Token);
    }

    [RelayCommand]
    private void OpenSelectedSource()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(SelectedMod is null||!TryGetSafeSourceUri(SelectedMod.SourceUrl,out var source))return;
        try
        {
            Process.Start(new ProcessStartInfo(source.AbsoluteUri){UseShellExecute=true});
        }
        catch(Exception ex) when(ex is Win32Exception or InvalidOperationException)
        {
            StatusText="Windows could not open the selected provider page.";
            MasterDebugLog.Write("CATALOG-BROWSE","Opening the provider source page failed.",ex);
        }
    }

    private async Task RefreshCatalogCoreAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(disposed||providers.Length==0)return;

        await refreshGate.WaitAsync(ct);
        try
        {
            IsRefreshing=true;
            StatusText="Refreshing supported mod sources… cached results remain available if a source is offline.";
            var reports=new List<string>(providers.Length);
            var successes=0;
            var failures=0;
            var discovered=0;

            foreach(var provider in providers)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result=await SyncProviderAsync(provider,ct);
                    successes++;
                    discovered+=result.ItemCount;
                    reports.Add($"{provider.DisplayName}: {result.ItemCount} refreshed");
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested)
                {
                    throw;
                }
                catch(Exception ex)
                {
                    failures++;
                    reports.Add($"{provider.DisplayName}: cached only");
                    MasterDebugLog.Write("CATALOG-BROWSE",$"Provider refresh failed safely. provider={provider.ProviderId}; cached rows preserved.",ex);
                }
            }

            ProviderStatusText=string.Join(" • ",reports);
            StatusText=failures==0
                ?$"Refreshed {discovered} mod entr{(discovered==1?"y":"ies")} from {successes} source{(successes==1?string.Empty:"s")}."
                :$"Refreshed {successes} source{(successes==1?string.Empty:"s")}; {failures} source{(failures==1?" is":"s are")} using cached results.";
            await ReloadFromCacheAsync(ct);
        }
        finally
        {
            IsRefreshing=false;
            refreshGate.Release();
        }
    }

    private async Task<CatalogSyncResult> SyncProviderAsync(IModCatalogProvider provider,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Exception? unsupported=null;
        foreach(var mode in GetBrowseModes(provider.ProviderId))
        {
            try
            {
                return await syncService.SyncAsync(
                    provider,
                    new CatalogBrowseRequest(
                        game,
                        Query:null,
                        Mode:mode,
                        Limit:provider.ProviderId.Equals("gamebanana",StringComparison.OrdinalIgnoreCase)?36:80),
                    new CatalogSyncOptions(TimeSpan.FromMinutes(30),HydrateFiles:false),
                    ct);
            }
            catch(NotSupportedException ex)
            {
                unsupported=ex;
            }
        }

        throw unsupported??new NotSupportedException($"Catalog provider '{provider.ProviderId}' does not expose a supported browse mode.");
    }

    private static IReadOnlyList<CatalogBrowseMode> GetBrowseModes(string providerId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(providerId.Equals("gamebanana",StringComparison.OrdinalIgnoreCase))
            return [CatalogBrowseMode.RecentlyUpdated,CatalogBrowseMode.Latest];
        return
        [
            CatalogBrowseMode.Trending,
            CatalogBrowseMode.RecentlyUpdated,
            CatalogBrowseMode.Latest,
            CatalogBrowseMode.Popular
        ];
    }

    private async Task ReloadFromCacheAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(disposed)return;
        var providerId=SelectedProvider.Id;
        var query=string.IsNullOrWhiteSpace(SearchText)?null:SearchText.Trim();
        var cached=await repository.SearchAsync(
            query,
            providerId,
            game.Id,
            includeStale:true,
            limit:250,
            now:DateTimeOffset.UtcNow,
            ct:ct);

        var now=DateTimeOffset.UtcNow;
        IEnumerable<CatalogBrowserItem> items=cached.Select(entry=>CatalogBrowserItem.Create(
            entry,
            providerNames.TryGetValue(entry.Mod.ProviderId,out var displayName)?displayName:entry.Mod.ProviderId,
            now));

        items=SelectedSortMode switch
        {
            "Most downloaded"=>items.OrderByDescending(item=>item.SortDownloads).ThenBy(item=>item.Name,StringComparer.OrdinalIgnoreCase),
            "Highest rated"=>items.OrderByDescending(item=>item.SortRating).ThenByDescending(item=>item.SortDownloads).ThenBy(item=>item.Name,StringComparer.OrdinalIgnoreCase),
            "Name"=>items.OrderBy(item=>item.Name,StringComparer.OrdinalIgnoreCase),
            _=>items.OrderByDescending(item=>item.SortDate).ThenBy(item=>item.Name,StringComparer.OrdinalIgnoreCase)
        };

        var previousId=SelectedMod?.CanonicalId;
        var materialized=items.ToArray();
        Results.ReplaceAll(materialized);
        SelectedMod=previousId is null
            ?Results.FirstOrDefault()
            :Results.FirstOrDefault(item=>item.CanonicalId.Equals(previousId,StringComparison.OrdinalIgnoreCase))??Results.FirstOrDefault();
        OnPropertyChanged(nameof(ResultCountLabel));

        if(!IsRefreshing)
        {
            StatusText=materialized.Length==0
                ?(query is null?"No cached mods yet. Refresh sources to start browsing.":"No cached mods match this search.")
                :$"Showing {materialized.Length} cached mod{(materialized.Length==1?string.Empty:"s")} for {game.DisplayName}.";
        }
    }

    private static bool TryGetSafeSourceUri(string value,out Uri source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(Uri.TryCreate(value,UriKind.Absolute,out var candidate)
           &&candidate.Scheme.Equals(Uri.UriSchemeHttps,StringComparison.OrdinalIgnoreCase))
        {
            source=candidate;
            return true;
        }

        source=null!;
        return false;
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(disposed)return;
        disposed=true;
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
