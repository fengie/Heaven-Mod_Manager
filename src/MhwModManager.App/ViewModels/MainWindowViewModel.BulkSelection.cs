using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool suppressBulkSelectionNotifications;

    public bool HasPendingDeployment
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return HasStagedChanges || rulesNeedApply;
        }
    }

    public int BulkSelectedCount
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Mods.Count(row => row.BulkSelected && row.CanStage);
        }
    }

    public bool HasBulkSelection
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return BulkSelectedCount > 0;
        }
    }

    public string BulkSelectionSummary
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            var selected = BulkSelectedCount;
            var visible = ModsView.Cast<ModRowViewModel>().Count(row => row.BulkSelected && row.CanStage);
            return $"{selected} marked for bulk action ({selected - visible} hidden by filters). Selection does not change game files.";
        }
    }

    private void NotifyBulkSelection()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (suppressBulkSelectionNotifications) return;
        OnPropertyChanged(nameof(BulkSelectedCount));
        OnPropertyChanged(nameof(HasBulkSelection));
        OnPropertyChanged(nameof(BulkSelectionSummary));
    }

    [RelayCommand]
    private void SelectAllVisible()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var targets = ModsView.Cast<ModRowViewModel>().Where(row => row.CanStage).ToArray();
        suppressBulkSelectionNotifications = true;
        try { foreach (var row in targets) row.BulkSelected = true; }
        finally { suppressBulkSelectionNotifications = false; NotifyBulkSelection(); }
        StatusText = $"Marked {targets.Length} visible mod(s). No game files changed.";
    }

    [RelayCommand]
    private void ClearBulkSelection()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        suppressBulkSelectionNotifications = true;
        try { foreach (var row in Mods) row.BulkSelected = false; }
        finally { suppressBulkSelectionNotifications = false; NotifyBulkSelection(); }
        StatusText = "Cleared bulk selection. No game files changed.";
    }

    [RelayCommand]
    private Task EnableSelected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        StageSelected(true);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task DisableSelected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        StageSelected(false);
        return Task.CompletedTask;
    }

    private void StageSelected(bool enabled)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var targets = Mods.Where(row => row.BulkSelected && row.CanStage).ToArray();
        if (targets.Length == 0)
        {
            StatusText = "Mark one or more managed mods first.";
            return;
        }
        if (busyCts is not null)
        {
            StatusText = "Finish the current operation before staging selected mods.";
            return;
        }

        // Marks select targets, never authorize live writes. Batch UI updates
        // while preserving all other pending choices and the user's marks.
        suppressChanged = true;
        try { foreach (var row in targets) row.StagedEnabled = enabled; }
        finally { suppressChanged = false; Changed(); }
        StatusText = HasPendingDeployment
            ? $"Staged {targets.Length} marked mod(s) {(enabled ? "on" : "off")}. {StagedCount} pending. Apply Mod Changes commits the setup."
            : "Selected mods already match the requested activation. No game files changed.";
    }
}
