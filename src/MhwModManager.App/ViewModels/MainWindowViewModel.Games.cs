using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MhwModManager.Core;
using MhwModManager.Diagnostics;

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
    private void AddGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(StagedCount>0&&MessageBox.Show("You have pending mod changes that have not been applied. Add or switch games anyway?","Pending Mod Changes",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var dialog=new OpenFileDialog{Title="Select a game executable",Filter="Windows games (*.exe)|*.exe",CheckFileExists=true,Multiselect=false};
        if(dialog.ShowDialog()!=true)return;
        var profile=s.GameRegistry.AddGenericFromExecutable(dialog.FileName);
        RestartIntoGame(profile);
    }

    [RelayCommand]
    private void SwitchGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(SelectedGame is null||SelectedGame.Id.Equals(s.Paths.Game.Id,StringComparison.OrdinalIgnoreCase))return;
        if(StagedCount>0&&MessageBox.Show("Switching games will discard the pending changes on this screen. Continue?","Switch Game",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        s.GameRegistry.SetActive(SelectedGame.Id);
        RestartIntoGame(SelectedGame);
    }

    [RelayCommand]
    private void ConfigureGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var window=new MhwModManager.App.GameProfileEditorWindow(s.Paths.Game){Owner=Application.Current.MainWindow};
        if(window.ShowDialog()!=true||window.Result is null)return;
        s.GameRegistry.Upsert(window.Result);
        s.GameRegistry.SetActive(window.Result.Id);
        RestartIntoGame(window.Result);
    }

    private static void RestartIntoGame(GameProfile profile)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={profile.Id}");
        var exe=Environment.ProcessPath;
        if(string.IsNullOrWhiteSpace(exe))throw new InvalidOperationException("Could not determine the manager executable for restart.");
        ProcessDebug.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=AppContext.BaseDirectory},"switch-game-restart");
        Application.Current.Shutdown();
    }
}
