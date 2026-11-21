using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.App.ViewModels;

public sealed partial class GamesPageViewModel : ObservableObject
{
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMinutes(2);
    private readonly GameProfileRegistry registry;
    private readonly TimeProvider clock;
    private DateTimeOffset nextDiscovery;
    [ObservableProperty] private GameProfile? selected;

    public GamesPageViewModel(GameProfileRegistry registry, TimeProvider? clock = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.registry=registry;
        this.clock=clock ?? TimeProvider.System;
    }

    public ObservableRangeCollection<GameProfile> Rows{get;}=[];
    public Task AutomaticDiscoveryTask{get;private set;}=Task.CompletedTask;

    public void Refresh()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ReadRegistered();
        var now=clock.GetUtcNow();
        if (!AutomaticDiscoveryTask.IsCompleted || now<nextDiscovery) return;
        nextDiscovery=now+DiscoveryInterval;
        AutomaticDiscoveryTask=DiscoverInstalledGamesAsync();
    }

    private void ReadRegistered()
    {
        using var trace=MasterDebugLog.BeginMethod();
        var registered=registry.Load();
        if (Rows.SequenceEqual(registered)) return;
        var selectedId=Selected?.Id;
        Rows.ReplaceAll(registered);
        if (selectedId is not null)
            Selected=Rows.FirstOrDefault(game=>StringComparer.OrdinalIgnoreCase.Equals(game.Id,selectedId));
    }

    private async Task DiscoverInstalledGamesAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var changed=await Task.Run(registry.DiscoverAndRegisterInstalledGames);
            if(changed.Count==0)return;
            ReadRegistered();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException or System.Security.SecurityException)
        {MasterDebugLog.Write("GAME-DISCOVERY","Automatic installed-game discovery failed; keeping the existing game registry visible.",ex);}
    }
}
