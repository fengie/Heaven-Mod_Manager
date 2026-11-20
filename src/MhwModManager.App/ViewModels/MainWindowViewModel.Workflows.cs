using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task OpenWorkflows()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("workflows.open", "Loadouts and rules", "Preparing the staged loadout…", true, async ct =>
        {
            var staged = CaptureStage();
            // Applied packages already have the exact deployed index. Only newly staged
            // providers need capture to preview their effect in the pending loadout.
            var pending = Mods.SelectMany(row => row.Members)
                .Where(mod => !mod.Enabled && staged.TryGetValue(mod.Id, out var entry) && entry.enabled);
            await s.Catalog.EnsureCapturedAsync(pending.Where(mod => !mod.RequiresLayoutReview), ct);
            await metadataGate.WaitAsync(ct);
            try
            {
                var window = new WorkflowWindow(s, staged) { Owner = Application.Current.MainWindow };
                window.ShowDialog();
                var requested = window.RequestedStage ?? (window.AppliedMigration ? null : staged);
                await ReloadMods(ct);
                if (requested is not null) foreach (var row in Mods) row.ApplyProfileState(requested);
                await RefreshProfiles(ct); await RefreshAnalysis(ct);
            }
            finally { metadataGate.Release(); }
        });
    }
}
