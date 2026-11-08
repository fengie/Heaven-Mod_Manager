using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ScanInstalledGames()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("games.discover","Scanning installed games","Checking Steam, Epic Games Store, GOG, and Xbox installations…",true,async ct=>
        {
            var result=await Task.Run(()=>s.GameRegistry.DiscoverAndRegisterInstalledGamesDetailed(),ct);
            await Application.Current.Dispatcher.InvokeAsync(()=>
            {
                GamesPage.Refresh();
                SelectedGame=Games.FirstOrDefault(x=>x.Id.Equals(s.Paths.Game.Id,StringComparison.OrdinalIgnoreCase));
            });
            StatusText=FormatGameDiscoveryStatus(result.AddedCount,result.RepairedCount);
        });
    }

    private static string FormatGameDiscoveryStatus(int addedCount,int repairedCount)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (addedCount,repairedCount) switch
        {
            (0,0)=>"No new games were found automatically. You can still choose Add Game and select the game executable yourself.",
            (>0,0)=>$"Added {addedCount} game(s). Select one and choose Use This Game.",
            (0,>0)=>$"Repaired {repairedCount} existing game profile(s). Select one and choose Use This Game.",
            _=>$"Added {addedCount} game(s) and repaired {repairedCount} existing game profile(s). Select one and choose Use This Game."
        };
    }

    [RelayCommand]
    private async Task AddGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!ConfirmSwitchWithoutPendingChanges()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Select a game executable",
            Filter = "Windows games (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        var profile = s.GameRegistry.AddGenericFromExecutable(dialog.FileName);
        await ((MhwModManager.App.App)Application.Current).SwitchGameInPlaceAsync(profile);
    }

    [RelayCommand]
    private async Task SwitchGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var requested = SelectedGame;
        if (requested is null || requested.Equals(s.Paths.Game)) return;
        if (!ConfirmSwitchWithoutPendingChanges()) return;
        await ((MhwModManager.App.App)Application.Current).SwitchGameInPlaceAsync(requested);
    }

    [RelayCommand]
    private async Task ConfigureGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var window = new MhwModManager.App.GameProfileEditorWindow(s.Paths.Game)
        {
            Owner = Application.Current.MainWindow
        };
        if (window.ShowDialog() != true || window.Result is null) return;
        if (!ConfirmSwitchWithoutPendingChanges()) return;
        s.GameRegistry.Upsert(window.Result);
        await ((MhwModManager.App.App)Application.Current).SwitchGameInPlaceAsync(window.Result);
    }

    private bool ConfirmSwitchWithoutPendingChanges()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (StagedCount == 0) return true;
        return MessageBox.Show(
            "There are pending mod changes that have not been applied. Switch games without applying those changes?",
            "Pending Mod Changes", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    internal bool TryBeginGameSwitch()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed || CriticalOperation || BusyVisibility == Visibility.Visible
            || ProgramUpdateCheckInProgress || stagedProgramUpdate is not null)
        {
            StatusText = "A mod transaction or program update is in progress. Finish it before switching games.";
            return false;
        }
        if (!programUpdateHandoffGate.TryBeginForeground())
        {
            StatusText = "A program update is preparing a safe handoff. Game switching is unavailable right now.";
            return false;
        }
        BusyTitle = "Switching game";
        BusyDetail = "Preparing an isolated game session without restarting the manager…";
        CancelVisibility = Visibility.Collapsed;
        CriticalOperation = true;
        BusyVisibility = Visibility.Visible;
        return true;
    }

    internal void EndGameSwitch()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        BusyVisibility = Visibility.Collapsed;
        CriticalOperation = false;
        programUpdateHandoffGate.EndForeground();
    }
}
