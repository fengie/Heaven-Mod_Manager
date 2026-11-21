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
        if (family.Members.Any(member => !string.IsNullOrWhiteSpace(member.ChoiceGroup) || member.FamilyId?.StartsWith("authored:",StringComparison.Ordinal)==true))
        {
            var selected = family.Members.Where(member=>member.ChoiceGroup is null).Select(member=>member.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var group in family.Members.Where(member=>member.ChoiceGroup is not null).GroupBy(member=>member.ChoiceGroup))
            {
                var active = group.Where(member=>member.Enabled).ToArray();
                var choice = active.Length == 1 ? active[0] : group.FirstOrDefault(member=>member.Id==member.PreferredChoiceId);
                if (choice is not null) selected.Add(choice.Id);
            }
            var defaults = family.Members.Where(member=>member.FamilyRole=="Main" || (member.ChoiceGroup is not null && selected.Contains(member.Id)))
                .Select(member=>member.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return [new(family.Id,"Default",defaults,"Base and selected alternatives."),
                new(family.Id,"Full",selected,"Enable additive components and keep the selected alternatives.")];
        }
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
