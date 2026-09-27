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
        await RunBusy("games.discover","Scanning installed games","Checking Steam, Epic Games Store, and GOG installations…",true,async ct=>
        {
            var added=await Task.Run(()=>s.GameRegistry.DiscoverAndRegisterInstalledGames(),ct);
            await Application.Current.Dispatcher.InvokeAsync(()=>
            {
                GamesPage.Refresh();
                SelectedGame=Games.FirstOrDefault(x=>x.Id.Equals(s.Paths.Game.Id,StringComparison.OrdinalIgnoreCase));
            });
            StatusText=added.Count==0?"No new supported Windows game installations were found. You can always use + Game to pick any executable.":$"Added {added.Count} game profile(s). Select one and press Switch.";
        });
    }

    [RelayCommand]
    private void AddGame()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(StagedCount>0&&MessageBox.Show("You have staged changes that are not applied. Add/switch games anyway?","Staged changes",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
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
        if(StagedCount>0&&MessageBox.Show("Switching games discards this screen's staged state. Continue?","Switch game",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
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
