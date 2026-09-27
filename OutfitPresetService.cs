using System.Diagnostics.CodeAnalysis;
using MhwModManager.Core;

namespace MhwModManager.Automation;

public sealed class OutfitPresetService
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Preset inference is an injectable policy service and may gain configuration state.")]
    public IReadOnlyList<OutfitPreset> Build(LogicalModFamily family)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (family.Members.Count <= 1) return [];
        var main = family.Members.Where(m => !HasAny(m.DisplayName,"top","waist","leg","cape","skimpy","thong","invisible","open")).Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var full = family.Members.Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skimpy = family.Members.Where(m => main.Contains(m.Id) || HasAny(m.DisplayName,"skimpy","thong","open","invisible","no cape")).Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var noCape = family.Members.Where(m => main.Contains(m.Id) || HasAny(m.DisplayName,"no cape")).Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var presets = new List<OutfitPreset>{new(family.Id,"Default",main,"Main package and required base layers."),new(family.Id,"Full",full,"Enable every additive component in this logical mod.")};
        if(skimpy.Count>main.Count)presets.Add(new(family.Id,"Skimpy",skimpy,"Automatically select skimpy/open/invisible option layers."));
        if(noCape.Count>main.Count)presets.Add(new(family.Id,"No Cape",noCape,"Base layers plus the no-cape option."));
        return presets;
    }
    private static bool HasAny(string value,params string[] tokens)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return tokens.Any(t=>value.Contains(t,StringComparison.OrdinalIgnoreCase));
    }
}
