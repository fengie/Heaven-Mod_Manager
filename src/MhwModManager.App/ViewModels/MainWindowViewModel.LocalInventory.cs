using System.ComponentModel;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MhwModManager.App.ViewModels;

public sealed class LibraryRowStyleSelector : System.Windows.Controls.StyleSelector
{
    public System.Windows.Style? ExternalStyle { get; set; }

    public override System.Windows.Style? SelectStyle(object item, System.Windows.DependencyObject container)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        return item is ExternalLibraryReference ? ExternalStyle : base.SelectStyle(item, container);
    }
}

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<LocalModLocation> LocalModLocations { get; private set; } = [];
    public string LocalModInventoryStatus { get; private set; } = "Checking registered game folders and Workshop libraries…";

    [ObservableProperty] private LocalModLocation? selectedInstalledFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleLocalModLocations))]
    private string localModQuery = "";

    public IEnumerable<LocalModLocation> VisibleLocalModLocations
    {
        get
        {
            using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
            var query = LocalModQuery.Trim();
            return query.Length == 0 ? LocalModLocations : LocalModLocations.Where(row =>
                row.Game.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.LibraryStatus.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Evidence.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Location.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    private readonly ObservableRangeCollection<object> libraryRows = [];
    public ICollectionView LibraryView { get; }
    public int VisibleLibraryCount
    {
        get
        {
            using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
            return LibraryView.Cast<object>().Count();
        }
    }
    private IReadOnlyList<ExternalLibraryReference> localPackageRows = [];
    private IReadOnlyList<ExternalLibraryReference>? previousLibrary;
    public bool CheckingDiscoveredLibrary { get; private set; } = true;

    private async Task RestoreDiscoveredLibraryAsync(CancellationToken ct)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        var previous = await Task.Run(() => s.GameRegistry.LoadDiscoveredLibrary(s.Paths.Game), ct);
        ct.ThrowIfCancellationRequested();
        if (!CheckingDiscoveredLibrary || previous.Count == 0) return;
        previousLibrary = previous.Select(row => row with { OwnerLabel = "Previously discovered · checking source" }).ToArray();
        RebuildLibraryRows();
    }

    private ExternalLibraryReference? selectedExternal;
    public object? SelectedLibraryItem
    {
        get
        {
            using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
            return (object?)SelectedMod ?? selectedExternal;
        }
        set
        {
            using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
            if (ReferenceEquals(value, SelectedLibraryItem)) return;
            var previousMod = SelectedMod;
            selectedExternal = value as ExternalLibraryReference;
            SelectedMod = value as ModRowViewModel;
            if (previousMod == SelectedMod) OnPropertyChanged(nameof(SelectedLibraryItem));
        }
    }

    private void RebuildLibraryRows()
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        var sources = Mods.SelectMany(row => row.Members.Concat(row.ArchivedMembers)).Select(mod => mod.SourcePath).ToHashSet(PathRules.Comparer);
        var rows = (previousLibrary ?? localPackageRows).Where(row => !row.Locations.Any(sources.Contains));
        libraryRows.ReplaceAll(Mods.Cast<object>().Concat(rows));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(InstalledCountLabel));
        OnPropertyChanged(nameof(AllViewLabel));
        OnPropertyChanged(nameof(VisibleLibraryCount));
    }

    private bool FilterLibraryItem(object item)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        if (item is not ExternalLibraryReference external) return FilterMod(item);
        var query = SearchText.Trim();
        return ModViewMode == "All" && (external.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || external.ItemId.Contains(query, StringComparison.OrdinalIgnoreCase)
            || external.OwnerLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
            || external.SourceSummary.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshLibraryFilters()
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        ModsView.Refresh();
        LibraryView.Refresh();
    }

    private HashSet<(string Id,string Path,string Name)>? catalogInventoryIdentities;
    private Task? localInventoryScan;
    private bool localInventoryChanged;
    private bool fullInventoryRequested;

    private void InvalidateLocalInventory()
    {
        using var trace = MasterDebugLog.BeginMethod();
        if (disposed || localInventoryScan == null || backgroundCts.IsCancellationRequested) return;
        localInventoryChanged = true;
        _ = RefreshLocalModInventoryAsync(backgroundCts.Token);
    }

    private Task RefreshLocalModInventoryAsync(CancellationToken ct, bool fullVisit = false)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        // A user may have added a file after the running scan enumerated its directory.
        // Multiple visits before that scan finishes share a single full follow-up pass.
        if (fullVisit) fullInventoryRequested = true;
        if (localInventoryScan is { IsCompleted: false }) return localInventoryScan;
        localInventoryScan = ScanUntilCurrentAsync(ct);
        return localInventoryScan;
    }

    private async Task ScanUntilCurrentAsync(CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        do
        {
            var currentOnly = localInventoryChanged && !fullInventoryRequested && !CheckingDiscoveredLibrary;
            fullInventoryRequested = false;
            localInventoryChanged = false;
            await ScanLocalModInventoryAsync(currentOnly,ct);
        } while ((localInventoryChanged || fullInventoryRequested) && !ct.IsCancellationRequested);
    }

    private async Task ScanLocalModInventoryAsync(bool currentOnly,CancellationToken ct)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        try
        {
            var (result, packages) = await Task.Run(async () =>
            {
                var result = currentOnly ? LocalModInventory.RefreshCurrentInstallation(LocalModLocations,s.Paths.Game,ct) : s.GameRegistry.ScanLocalMods(ct);
                var sources = await s.Database.GetModsAsync(ct);
                var paths = sources.Select(mod => mod.SourcePath).ToArray();
                var titles = new Dictionary<string,string>(PathRules.Comparer);
                var represented = await s.Adoption.RepresentedInventoryPathsAsync(result,sources,s.Executor.Destination,ct,titles);
                var sourceTitles = sources.GroupBy(mod=>mod.SourcePath,PathRules.Comparer)
                    .ToDictionary(group=>group.Key,group=>string.Join(", ",group.Select(mod=>mod.DisplayName)),PathRules.Comparer);
                var packages = LocalModInventory.RegisteredPackageLibrary(result, s.Paths.Game, paths, sourceTitles, titles, ct);
                represented.UnionWith(titles.Keys);
                var installed = LocalModInventory.InstalledPackages(result,s.Paths.Game,represented,ct);
                var workshop = LocalModInventory.WorkshopLibrary(result, s.Paths.Game.SteamAppId, paths);
                ct.ThrowIfCancellationRequested();
                var library = s.GameRegistry.ReconcileDiscoveredLibrary(s.Paths.Game,
                    workshop.Cast<ExternalLibraryReference>().Concat(packages).Concat(installed).ToArray(), result);
                try
                {
                    s.GameRegistry.SaveDiscoveredLibrary(s.Paths.Game, library);
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    s.Log.Warning(ex, "Could not save the discovered mod library for the next startup.");
                }
                var references = library.SelectMany(row => row.Locations.Select(path => (path,row.DisplayName))).ToLookup(item=>item.path,item=>item.DisplayName,PathRules.Comparer);
                var annotated = result.Select(entry => entry with
                {
                    LibraryStatus = represented.Contains(entry.Location) ? "Managed deployment or package · " + titles.GetValueOrDefault(entry.Location) :
                        entry.PreviousProfileId == s.Paths.Game.Id ? "Previous installation" :
                        references.Contains(entry.Location) ? "External reference · " + string.Join(", ",references[entry.Location].Distinct()) :
                        entry.RegisteredProfileId != null && entry.RegisteredProfileId != s.Paths.Game.Id ? "Other game" : "Evidence only"
                }).ToArray();
                return (annotated, library);
            }, ct);
            ct.ThrowIfCancellationRequested();
            CheckingDiscoveredLibrary = false;
            previousLibrary = null;
            OnPropertyChanged(nameof(CheckingDiscoveredLibrary));
            LocalModLocations = result;
            localPackageRows = packages;
            RebuildLibraryRows();
            OnPropertyChanged(nameof(LocalModLocations));
            OnPropertyChanged(nameof(VisibleLocalModLocations));
            LocalModInventoryStatus = $"{result.Length:N0} inventory entries · checked {DateTime.Now:t}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            previousLibrary = (previousLibrary ?? localPackageRows).Select(row => row with { OwnerLabel = "Previously discovered · verification unavailable" }).ToArray();
            CheckingDiscoveredLibrary = false;
            OnPropertyChanged(nameof(CheckingDiscoveredLibrary));
            RebuildLibraryRows();
            LocalModInventoryStatus = "Inventory scan incomplete. " + ex.Message;
        }
        OnPropertyChanged(nameof(LocalModInventoryStatus));
    }
}
