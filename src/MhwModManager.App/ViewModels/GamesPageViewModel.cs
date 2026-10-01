using System.Windows;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.App.ViewModels;

public sealed class GamesPageViewModel
{
    private readonly GameProfileRegistry registry;
    private bool discoveryAttempted;

    public GamesPageViewModel(GameProfileRegistry registry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.registry=registry;
    }

    public ObservableRangeCollection<GameProfile> Rows{get;}=[];
    public Task AutomaticDiscoveryTask{get;private set;}=Task.CompletedTask;

    public void Refresh()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Rows.ReplaceAll(registry.Load());
        if(discoveryAttempted)return;
        discoveryAttempted=true;
        AutomaticDiscoveryTask=DiscoverInstalledGamesAsync();
    }

    private async Task DiscoverInstalledGamesAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var added=await Task.Run(registry.DiscoverAndRegisterInstalledGames).ConfigureAwait(false);
            if(added.Count==0)return;
            var dispatcher=Application.Current?.Dispatcher;
            if(dispatcher is null)Rows.ReplaceAll(registry.Load());
            else if(!dispatcher.HasShutdownStarted&&!dispatcher.HasShutdownFinished)
                await dispatcher.InvokeAsync(()=>Rows.ReplaceAll(registry.Load()));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException or System.Security.SecurityException)
        {MasterDebugLog.Write("GAME-DISCOVERY","Automatic installed-game discovery failed; keeping the existing game registry visible.",ex);}
    }
}
