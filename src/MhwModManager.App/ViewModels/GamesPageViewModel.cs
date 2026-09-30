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

    public void Refresh()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!discoveryAttempted)
        {
            discoveryAttempted=true;
            try{registry.DiscoverAndRegisterInstalledGames();}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {MasterDebugLog.Write("GAME-DISCOVERY","Automatic installed-game discovery failed; loading the existing game registry.",ex);}
        }
        Rows.ReplaceAll(registry.Load());
    }
}
