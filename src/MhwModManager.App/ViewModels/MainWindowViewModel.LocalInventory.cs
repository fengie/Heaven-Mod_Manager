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
            selectedExternal = value as ExternalLibraryReference;
            SelectedMod = value as ModRowViewModel;
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

    private Task? localInventoryScan;

    private Task RefreshLocalModInventoryAsync(CancellationToken ct)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        if (localInventoryScan is { IsCompleted: false }) return localInventoryScan;
        localInventoryScan = ScanLocalModInventoryAsync(ct);
        return localInventoryScan;
    }

    private async Task ScanLocalModInventoryAsync(CancellationToken ct)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        try
        {
            var (result, packages) = await Task.Run(async () =>
            {
                var result = s.GameRegistry.ScanLocalMods(ct);
                var sources = await s.Database.GetModsAsync(ct);
                var paths = sources.Select(mod => mod.SourcePath).ToArray();
                var packages = LocalModInventory.RegisteredPackageLibrary(result, s.Paths.Game, paths, ct);
                var workshop = LocalModInventory.WorkshopLibrary(result, s.Paths.Game.SteamAppId, paths);
                ct.ThrowIfCancellationRequested();
                var library = s.GameRegistry.ReconcileDiscoveredLibrary(s.Paths.Game,
                    workshop.Cast<ExternalLibraryReference>().Concat(packages).ToArray(), result);
                try
                {
                    s.GameRegistry.SaveDiscoveredLibrary(s.Paths.Game, library);
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    s.Log.Warning(ex, "Could not save the discovered mod library for the next startup.");
                }
                return (result, library);
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
            LocalModInventoryStatus = $"{result.Count:N0} inventory entries · checked {DateTime.Now:t}";
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
