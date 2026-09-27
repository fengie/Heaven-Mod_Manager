using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.App.ViewModels;

public sealed class GamesPageViewModel
{
    private readonly GameProfileRegistry registry;

    public GamesPageViewModel(GameProfileRegistry registry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.registry=registry;
    }

    public ObservableRangeCollection<GameProfile> Rows{get;}=[];

    public void Refresh()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Rows.ReplaceAll(registry.Load());
    }
}
