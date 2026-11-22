using System.Diagnostics;
using System.IO;
using System.Net.Http;
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

    /// <summary>
    /// Reads only the approved Heaven.gg game-link registry on user request.
    /// The web catalog is never used to register, relink or deploy a game.
    /// </summary>
    [RelayCommand]
    private async Task OpenHeavenGameGuide()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected = SelectedGame?.Id ?? s.Paths.Game.Id;
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            var target = await HeavenGameHubCatalog.FindGuideAsync(selected, client);
            if (target is null)
            {
                StatusText = "Heaven.gg does not currently publish an approved guide for this game. Your game and mods were not changed.";
                return;
            }

            var launched = ProcessDebug.Start(new ProcessStartInfo(target.AbsoluteUri)
            {
                UseShellExecute = true
            }, "heaven-gg-public-guide");
            StatusText = launched is null
                ? "No browser could be opened for the public Heaven.gg guide."
                : "Opened the public Heaven.gg guide in your browser. No mod files were changed.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or System.ComponentModel.Win32Exception or InvalidOperationException
            or IOException)
        {
            MasterDebugLog.Write("HEAVEN-GG", "Public read-only game guide unavailable.", ex);
            StatusText = "Heaven.gg could not be opened right now. Your game and mods were not changed.";
        }
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
        try
        {
            var profile = s.GameRegistry.AddGenericFromExecutable(dialog.FileName, activate: false);
            await ((MhwModManager.App.App)Application.Current).SwitchGameInPlaceAsync(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MasterDebugLog.Write("GAME-SWITCH", "Could not add the selected game.", ex);
            StatusText = "Could not add the selected game: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SwitchGame(GameProfile? requested)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (requested is null || StringComparer.OrdinalIgnoreCase.Equals(requested.Id, s.Paths.Game.Id)) return;
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
        // Persist the edited profile for the candidate bootstrap, but do not strand
        // the existing session on a changed profile if the transition fails.
        if (!window.Result.Id.Equals(s.Paths.Game.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Editing a game's profile cannot change its storage identity. Use Add Game.");
        var previous = s.GameRegistry.Load().FirstOrDefault(x =>
            x.Id.Equals(window.Result.Id, StringComparison.OrdinalIgnoreCase)) ?? s.Paths.Game;
        s.GameRegistry.Upsert(window.Result);
        var switched = await ((MhwModManager.App.App)Application.Current).SwitchGameInPlaceAsync(window.Result);
        if (!switched) s.GameRegistry.Upsert(previous);
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
