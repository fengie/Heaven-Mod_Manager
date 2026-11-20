using MhwModManager.Filesystem;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<LocalModLocation> LocalModLocations { get; private set; } = [];
    public string LocalModInventoryStatus { get; private set; } = "Checking registered game folders and Workshop libraries…";

    private async Task RefreshLocalModInventoryAsync(CancellationToken ct)
    {
        using var __mhwTrace = MhwModManager.Core.MasterDebugLog.BeginMethod();
        try
        {
            var result = await Task.Run(() => s.GameRegistry.ScanLocalMods(ct), ct);
            ct.ThrowIfCancellationRequested();
            LocalModLocations = result;
            OnPropertyChanged(nameof(LocalModLocations));
            LocalModInventoryStatus = $"{result.Count:N0} inventory entries";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            LocalModInventoryStatus = "Inventory scan incomplete. " + ex.Message;
        }
        OnPropertyChanged(nameof(LocalModInventoryStatus));
    }
}
