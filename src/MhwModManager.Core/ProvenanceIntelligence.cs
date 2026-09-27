using System.Globalization;
using System.Text.RegularExpressions;

namespace MhwModManager.Core;

public static partial class ProvenanceIntelligence
{
    public static int NexusRoleRank(NexusFileCategory category) => category switch
    {
        NexusFileCategory.Main => 100,
        NexusFileCategory.Miscellaneous => 150,
        NexusFileCategory.Optional => 300,
        NexusFileCategory.Update => 400,
        NexusFileCategory.OldVersion => 25,
        NexusFileCategory.Archived => 10,
        NexusFileCategory.Removed => 0,
        _ => 75
    };

    public static bool TryNexusOverlay(
        ModDescriptor a,
        ModDescriptor b,
        PairStats stats,
        out ModDescriptor parent,
        out ModDescriptor child,
        out string reason,
        out int score)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        parent = a;
        child = b;
        reason = string.Empty;
        score = 0;

        if (!SameNexusMod(a,b)) return false;
        if (stats.SharedPaths == 0) return false;

        if (!string.IsNullOrWhiteSpace(a.NexusVersionId) &&
            StringComparer.OrdinalIgnoreCase.Equals(b.NexusPreviousVersionId,a.NexusVersionId))
        {
            parent=a; child=b; score=100;
            reason=$"Nexus version lineage explicitly marks '{b.DisplayName}' as replacing the installed version from '{a.DisplayName}'.";
            return true;
        }
        if (!string.IsNullOrWhiteSpace(b.NexusVersionId) &&
            StringComparer.OrdinalIgnoreCase.Equals(a.NexusPreviousVersionId,b.NexusVersionId))
        {
            parent=b; child=a; score=100;
            reason=$"Nexus version lineage explicitly marks '{a.DisplayName}' as replacing the installed version from '{b.DisplayName}'.";
            return true;
        }

        var ar=NexusRoleRank(a.NexusCategory);
        var br=NexusRoleRank(b.NexusCategory);
        if (ar==br) return false;
        var higher=ar>br?a:b;
        var lower=ReferenceEquals(higher,a)?b:a;

        // Nexus category semantics are strong evidence only when the packages actually overlap.
        if (higher.NexusCategory is NexusFileCategory.Optional or NexusFileCategory.Update)
        {
            parent=lower; child=higher; score=higher.NexusCategory==NexusFileCategory.Update?98:99;
            reason=$"Nexus classifies '{higher.DisplayName}' as {higher.NexusCategory.ToString().ToLowerInvariant()} within the same mod, so it overrides the lower-precedence package only where their files overlap.";
            return true;
        }

        return false;
    }

    public static IReadOnlyList<(string older,string newer,int score,string reason)> BuildSupersessionLinks(IReadOnlyList<ModDescriptor> mods)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var links=new Dictionary<string,(string older,string newer,int score,string reason)>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in mods.Where(m=>!string.IsNullOrWhiteSpace(m.NexusModId)||!string.IsNullOrWhiteSpace(m.NexusModUuid))
                                 .GroupBy(m=>m.NexusModUuid??m.NexusModId!,StringComparer.OrdinalIgnoreCase))
        {
            var items=group.ToArray();
            for(var i=0;i<items.Length-1;i++)
            for(var j=i+1;j<items.Length;j++)
            {
                var a=items[i];var b=items[j];
                if (!SameFileLineage(a,b)) continue;

                ModDescriptor? older=null,newer=null;
                var score=0;
                if(!string.IsNullOrWhiteSpace(a.NexusVersionId)&&StringComparer.OrdinalIgnoreCase.Equals(b.NexusPreviousVersionId,a.NexusVersionId)){older=a;newer=b;score=100;}
                else if(!string.IsNullOrWhiteSpace(b.NexusVersionId)&&StringComparer.OrdinalIgnoreCase.Equals(a.NexusPreviousVersionId,b.NexusVersionId)){older=b;newer=a;score=100;}
                else if(a.NexusUploadedAt.HasValue&&b.NexusUploadedAt.HasValue&&a.NexusUploadedAt!=b.NexusUploadedAt)
                {
                    older=a.NexusUploadedAt<b.NexusUploadedAt?a:b;newer=ReferenceEquals(older,a)?b:a;score=97;
                }
                else if(TryVersion(a.NexusVersion??a.Name,out var av)&&TryVersion(b.NexusVersion??b.Name,out var bv))
                {
                    var cmp=CompareVersion(av,bv);if(cmp!=0){older=cmp<0?a:b;newer=ReferenceEquals(older,a)?b:a;score=95;}
                }
                if(older is null||newer is null||score<95)continue;
                links[older.Id]=(older.Id,newer.Id,score,$"'{newer.DisplayName}' is a newer Nexus revision in the same file lineage; the older source remains archived for rollback but is removed from normal conflict calculation.");
            }
        }
        return links.Values.ToArray();
    }


    public static IReadOnlyList<(string older,string newer,int score,string reason)> BuildLocalTextureSupersessionLinks(
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyList<ModFileDescriptor> files)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var filesByMod=files.GroupBy(f=>f.ModId,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var candidates=mods.Where(m=>!m.IsSuperseded&&filesByMod.TryGetValue(m.Id,out var mf)&&mf.Length>0&&mf.All(f=>f.FileClass==FileClass.Texture))
            .Select(m=>new{Mod=m,Stem=Normalize(m.DisplayName)})
            .Where(x=>x.Stem.Length>0)
            .GroupBy(x=>x.Stem,StringComparer.OrdinalIgnoreCase);
        var links=new Dictionary<string,(string older,string newer,int score,string reason)>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in candidates)
        {
            var items=group.ToArray();
            for(var i=0;i<items.Length-1;i++)
            for(var j=i+1;j<items.Length;j++)
            {
                var a=items[i].Mod;var b=items[j].Mod;
                if(ProvenanceIntelligence.SameNexusMod(a,b))continue;
                var af=filesByMod[a.Id];var bf=filesByMod[b.Id];
                var aPaths=af.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(!bf.Any(f=>aPaths.Contains(f.Path)))continue;
                if(!TryVersion(a.DisplayName,out var av)||!TryVersion(b.DisplayName,out var bv))continue;
                var cmp=CompareVersion(av,bv);if(cmp==0)continue;
                var older=cmp<0?a:b;var newer=ReferenceEquals(older,a)?b:a;
                links[older.Id]=(older.Id,newer.Id,94,$"'{newer.DisplayName}' is an explicit newer version of the same texture-only package and shares deployed texture paths with '{older.DisplayName}'. The older source remains available for rollback but is hidden from normal conflict calculation.");
            }
        }
        return links.Values.ToArray();
    }

    public static bool SameNexusMod(ModDescriptor a,ModDescriptor b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!string.IsNullOrWhiteSpace(a.NexusModUuid)&&StringComparer.OrdinalIgnoreCase.Equals(a.NexusModUuid,b.NexusModUuid))return true;
        return !string.IsNullOrWhiteSpace(a.NexusModId)&&StringComparer.OrdinalIgnoreCase.Equals(a.NexusModId,b.NexusModId);
    }

    private static bool SameFileLineage(ModDescriptor a,ModDescriptor b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!SameNexusMod(a,b))return false;
        if(!string.IsNullOrWhiteSpace(a.NexusFileId)&&!string.IsNullOrWhiteSpace(b.NexusFileId))
            return StringComparer.OrdinalIgnoreCase.Equals(a.NexusFileId,b.NexusFileId);
        var ac=Normalize(a.DisplayName);var bc=Normalize(b.DisplayName);
        return ac.Length>0&&StringComparer.OrdinalIgnoreCase.Equals(ac,bc);
    }

    private static string Normalize(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        value=VersionSuffix().Replace(value,string.Empty);
        return string.Join(' ',Token().Matches(value.ToLowerInvariant()).Cast<Match>().Select(m=>m.Value).Where(t=>t is not "old" and not "new" and not "updated" and not "update"));
    }

    private static bool TryVersion(string source,out int[] version)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var m=Version().Match(source);if(!m.Success){version=[];return false;}
        var parts=m.Groups[1].Value.Split('.');version=new int[parts.Length];
        for(var i=0;i<parts.Length;i++)if(!int.TryParse(parts[i],NumberStyles.None,CultureInfo.InvariantCulture,out version[i])){version=[];return false;}
        return true;
    }
    private static int CompareVersion(int[] a,int[] b){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var n=Math.Max(a.Length,b.Length);for(var i=0;i<n;i++){var av=i<a.Length?a[i]:0;var bv=i<b.Length?b[i]:0;if(av!=bv)return av.CompareTo(bv);}return 0;}

    [GeneratedRegex(@"(?:\s+|[-_])v?\d+(?:\.\d+){0,3}$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex VersionSuffix();
    [GeneratedRegex(@"(?:^|[\s_\-])v?(\d{1,4}(?:\.\d{1,4}){0,3})(?:$|[\s_\-])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex Version();
    [GeneratedRegex(@"[a-z0-9]+(?:-[a-z0-9]+)?",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex Token();
}
