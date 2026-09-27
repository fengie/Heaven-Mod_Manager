using MhwModManager.Core;
using System.Diagnostics;
namespace MhwModManager.Mhw;
public sealed record ProcessBlocker(int Id,string Name,string? MainWindowTitle);
public sealed class GameProcessGuard(GameProfile game)
{
    public IReadOnlyList<ProcessBlocker> GetKnownBlockers()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.DisplayName}");
        var list=new List<ProcessBlocker>();
        foreach(var p in Process.GetProcessesByName(game.ProcessName))
        {
            try{list.Add(new(p.Id,p.ProcessName,p.MainWindowTitle));}catch{}
        }
        return list;
    }
}
