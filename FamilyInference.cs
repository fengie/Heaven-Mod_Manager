namespace MhwModManager.Core;

public static class FamilyInference
{
    private static readonly string[] ComponentWords = ["top","waist","leg","legs","helm","helmet","chest","arms","thong","skirt","cape","belt","bra","jacket","invisible","open","pierced","nipples","fix","patch","option","optional"];
    public static IReadOnlyList<ModFamilySuggestion> Suggest(IReadOnlyList<ModDescriptor> mods)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var output = new List<ModFamilySuggestion>();
        foreach (var child in mods)
        foreach (var parent in mods)
        {
            if (ReferenceEquals(child,parent) || child.Id == parent.Id) continue;
            var c = Clean(child.Name); var p = Clean(parent.Name);
            if (!c.StartsWith(p + " - ", StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = c[(p.Length+3)..];
            var confidence = ComponentWords.Any(w => suffix.Contains(w,StringComparison.OrdinalIgnoreCase)) ? Confidence.High : Confidence.Medium;
            output.Add(new(parent.Id,child.Id,confidence,$"Name suffix '{suffix}' looks like a component/variant."));
        }
        return output.DistinctBy(x => x.ChildModId).ToArray();
    }
    private static string Clean(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return System.Text.RegularExpressions.Regex.Replace(name, @"-\d{2,7}-\d+(?:-\d+){0,5}(?:\s*\(\d+\))?$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }
}
