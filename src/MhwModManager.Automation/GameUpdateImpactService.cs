using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class GameUpdateImpactService(PlannerSnapshotRepository plannerSnapshots)
{
    public async Task<GameUpdateImpactReport> BuildAsync(bool changed, CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snap=await plannerSnapshots.LoadAsync(ct); var enabled=snap.Mods.Where(m=>m.Enabled).ToDictionary(m=>m.Id,StringComparer.OrdinalIgnoreCase);
        var byMod=snap.Files.Where(f=>enabled.ContainsKey(f.ModId)).GroupBy(f=>f.ModId,StringComparer.OrdinalIgnoreCase).ToArray();
        var textureOnly=0;var other=0;var high=new List<string>();
        foreach(var group in byMod){if(group.All(f=>f.FileClass==FileClass.Texture)){textureOnly++;continue;}if(group.Any(f=>f.FileClass is FileClass.Plugin or FileClass.Executable or FileClass.GameData))high.Add(enabled[group.Key].DisplayName);else other++;}
        return new(changed,high.Count,textureOnly,other,high,$"{textureOnly} texture-only mod(s) are low risk; {high.Count} plugin/executable/game-data mod(s) should be revalidated.");
    }
}
