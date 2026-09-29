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
        await RunBusy("workflows.open", "Loadouts and rules", "Capturing packages for accurate previews…", true, async ct =>
        {
            await s.Catalog.EnsureCapturedAsync(await s.Database.GetModsAsync(ct), ct);
            await metadataGate.WaitAsync(ct);
            try
            {
                var staged = Mods.SelectMany(m => m.ExpandStage()).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
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
