using MhwModManager.Filesystem;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<LocalModLocation> LocalModLocations { get; private set; } = [];
    public string LocalModInventoryStatus { get; private set; } = "Checking registered game folders and Workshop librariesâ€¦";

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
            var result = await Task.Run(() => s.GameRegistry.ScanLocalMods(ct), ct);
            ct.ThrowIfCancellationRequested();
            LocalModLocations = result;
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
