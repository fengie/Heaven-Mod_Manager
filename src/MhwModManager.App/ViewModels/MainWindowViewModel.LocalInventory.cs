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
        var sources = Mods.SelectMany(row => row.Members.Concat(row.ArchivedMembers)).Select(mod => mod.SourcePath);
        var external = LocalModInventory.WorkshopLibrary(LocalModLocations, s.Paths.Game.SteamAppId, sources);
        libraryRows.ReplaceAll(Mods.Cast<object>().Concat(external).Concat(localPackageRows));
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
                var packages = LocalModInventory.RegisteredPackageLibrary(result, s.Paths.Game, sources.Select(mod => mod.SourcePath), ct);
                return (result, packages);
            }, ct);
            ct.ThrowIfCancellationRequested();
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
            LocalModInventoryStatus = "Inventory scan incomplete. " + ex.Message;
        }
        OnPropertyChanged(nameof(LocalModInventoryStatus));
    }
}
