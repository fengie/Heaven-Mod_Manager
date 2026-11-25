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
        return ApplySelectedAsync(true);
    }

    [RelayCommand]
    private Task DisableSelected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ApplySelectedAsync(false);
    }

    private async Task ApplySelectedAsync(bool enabled)
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
            StatusText = "Finish the current operation before applying selected mods.";
            return;
        }

        // Never commit unrelated earlier staging silently. Existing choices can be
        // combined, but only after an explicit confirmation of the full batch.
        var hadPending = HasPendingDeployment;
        suppressChanged = true;
        try { foreach (var row in targets) row.StagedEnabled = enabled; }
        finally { suppressChanged = false; Changed(); }
        if (!HasPendingDeployment)
        {
            ClearBulkSelection();
            StatusText = "Selected mods already match the requested activation.";
            return;
        }
        await ApplyStagedChangesAsync(confirm: true, forceConfirm: hadPending);
        if (!HasPendingDeployment)
        {
            var result = StatusText;
            ClearBulkSelection();
            StatusText = result;
        }
    }
}
