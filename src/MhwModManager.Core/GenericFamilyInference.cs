using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Generic evidence-based family inference. It intentionally does not know mod brands/authors.
/// Strong source metadata wins; local inference requires both semantic/name evidence and concrete
/// file-layout evidence so unrelated replacements do not collapse merely because their names look alike.
/// </summary>
public static partial class GenericFamilyInference
{
    private static readonly HashSet<string> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "base","main","default","core","optional","option","addon","add-on","component","part",
        "patch","fix","fixed","hotfix","update","updated","upgrade","revision","revised",
        "top","bottom","waist","wst","leg","legs","helm","helmet","head","chest","body","arms","arm",
        "cape","belt","skirt","pants","shorts","sleeve","sleeves","glove","gloves","boot","boots",
        "texture","textures","retexture","skin","skins","mesh","meshes","physics","ctc","ccl"
    };

    private static readonly HashSet<string> ChoiceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "alternative","alt","variant","choice","choose","either","color","colour","style","version"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","and","or","the","for","of","to","with","without","from","file","files","mod","mods","v"
    };

    public sealed record Evidence(string LeftModId,string RightModId,int Score,bool Strong,string Summary);

    public static IReadOnlyDictionary<string,string> InferKeys(
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyList<ModFileDescriptor> files,
        out IReadOnlyList<Evidence> acceptedEvidence,
        bool includeMhwAssetSemantics = true)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var candidates = mods.Where(m => !m.IsSuperseded && string.IsNullOrWhiteSpace(m.FamilyId) && string.IsNullOrWhiteSpace(m.NexusModId)).ToArray();
        if (candidates.Length < 2)
        {
            acceptedEvidence = Array.Empty<Evidence>();
            return new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        }

        var profiles = BuildProfiles(candidates, files, includeMhwAssetSemantics);
        var pairs = new List<Evidence>();
        for (var i=0;i<candidates.Length;i++)
        for (var j=i+1;j<candidates.Length;j++)
        {
            var evidence = ScorePair(candidates[i], candidates[j], profiles[candidates[i].Id], profiles[candidates[j].Id]);
            if (evidence is not null && evidence.Score >= 75 && evidence.Strong) pairs.Add(evidence);
        }

        var parent = candidates.ToDictionary(m=>m.Id,m=>m.Id,StringComparer.OrdinalIgnoreCase);
        var candidateById = candidates.ToDictionary(m=>m.Id,StringComparer.OrdinalIgnoreCase);
        var clusterMembers = candidates.ToDictionary(
            m=>m.Id,
            m=>new HashSet<string>(StringComparer.OrdinalIgnoreCase){m.Id},
            StringComparer.OrdinalIgnoreCase);
        string Find(string id)
        {
            while (!StringComparer.OrdinalIgnoreCase.Equals(parent[id], id))
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }
            return id;
        }
        void Union(string a,string b)
        {
            var ra=Find(a);var rb=Find(b);if(StringComparer.OrdinalIgnoreCase.Equals(ra,rb))return;
            var keep=StringComparer.OrdinalIgnoreCase.Compare(ra,rb)<=0?ra:rb;
            var drop=StringComparer.OrdinalIgnoreCase.Equals(keep,ra)?rb:ra;
            parent[drop]=keep;
            clusterMembers[keep].UnionWith(clusterMembers[drop]);
            clusterMembers.Remove(drop);
        }

        foreach (var pair in pairs.OrderByDescending(x=>x.Score).ThenBy(x=>x.LeftModId,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.RightModId,StringComparer.OrdinalIgnoreCase))
        {
            // Avoid transitive over-grouping: before joining clusters, every cross-cluster pair must not
            // have strong negative evidence. This is a lightweight complete-link safety check.
            var leftRoot=Find(pair.LeftModId);var rightRoot=Find(pair.RightModId);
            if(StringComparer.OrdinalIgnoreCase.Equals(leftRoot,rightRoot))continue;
            var leftMembers=clusterMembers[leftRoot];
            var rightMembers=clusterMembers[rightRoot];
            var blocked=leftMembers.Any(leftId=>rightMembers.Any(rightId=>
                HardBlock(candidateById[leftId],candidateById[rightId],profiles[leftId],profiles[rightId])));
            if(!blocked)Union(pair.LeftModId,pair.RightModId);
        }

        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in candidates.GroupBy(m=>Find(m.Id),StringComparer.OrdinalIgnoreCase))
        {
            var members=group.ToArray();if(members.Length<2)continue;
            var canonical=members.OrderBy(m=>IdentityStem(m.DisplayName).Length).ThenBy(m=>m.DisplayName,StringComparer.OrdinalIgnoreCase).First();
            var stem=NormalizeKey(IdentityStem(canonical.DisplayName));
            var rootKey=NormalizeKey(group.Key);
            var key="evidence:"+(stem.Length==0?"family":stem)+":"+rootKey;
            foreach(var member in members)result[member.Id]=key;
        }

        acceptedEvidence=pairs.Where(p=>result.ContainsKey(p.LeftModId)&&result.ContainsKey(p.RightModId) && StringComparer.OrdinalIgnoreCase.Equals(result[p.LeftModId],result[p.RightModId])).ToArray();
        return result;
    }

    private static Dictionary<string,CandidateProfile> BuildProfiles(IReadOnlyList<ModDescriptor> mods,IReadOnlyList<ModFileDescriptor> files,bool includeMhwAssetSemantics)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var valid=mods.Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grouped=files.Where(f=>valid.Contains(f.ModId)).GroupBy(f=>f.ModId,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        return mods.ToDictionary(
            m=>m.Id,
            m=>new CandidateProfile(
                BuildFileProfile(grouped.GetValueOrDefault(m.Id)??Array.Empty<ModFileDescriptor>(),includeMhwAssetSemantics),
                IdentityStem(m.DisplayName),
                IdentityTokens(m.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                HasRoleSignal(m.DisplayName),
                HasChoiceSignal(m.DisplayName)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static FileProfile BuildFileProfile(IReadOnlyList<ModFileDescriptor> files,bool includeMhwAssetSemantics)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var paths=files.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files)
        {
            try
            {
                if(includeMhwAssetSemantics && PathRules.TryGetArmorComponent(file.Path,out var modelId,out _))assets.Add("armor:"+modelId);
                if(includeMhwAssetSemantics)
                {
                    var ns=PathRules.ResourceNamespace(file.Path);if(ns is not null)assets.Add("resource:"+ns);
                }
                var segments=PathRules.Normalize(file.Path).Split('\\');
                if(segments.Length>=4)roots.Add(string.Join("\\",segments.Take(4)).ToLowerInvariant());
                else if(segments.Length>=2)roots.Add(string.Join("\\",segments.Take(2)).ToLowerInvariant());
            }
            catch(ArgumentException){ }
        }
        return new(paths,assets,roots);
    }

    private static Evidence? ScorePair(ModDescriptor a,ModDescriptor b,CandidateProfile ap,CandidateProfile bp)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!string.IsNullOrWhiteSpace(a.NexusModId)&&!string.IsNullOrWhiteSpace(b.NexusModId)&&!StringComparer.OrdinalIgnoreCase.Equals(a.NexusModId,b.NexusModId))return null;

        var nameSimilarity=TokenSimilarity(ap.IdentityTokens,bp.IdentityTokens);
        var exactStem=ap.IdentityStem.Length>0&&StringComparer.OrdinalIgnoreCase.Equals(ap.IdentityStem,bp.IdentityStem);
        var prefix=IsMeaningfulPrefix(ap.IdentityStem,bp.IdentityStem)||IsMeaningfulPrefix(bp.IdentityStem,ap.IdentityStem);
        var roleDifference=ap.HasRoleSignal||bp.HasRoleSignal;
        var choiceOnly=ap.HasChoiceSignal||bp.HasChoiceSignal;

        var sharedPaths=CountOverlap(ap.Files.Paths,bp.Files.Paths);
        var smaller=Math.Min(ap.Files.Paths.Count,bp.Files.Paths.Count);
        var overlap=smaller==0?0:sharedPaths/(double)smaller;
        var sharedAssets=CountOverlap(ap.Files.Assets,bp.Files.Assets);
        var sharedRoots=CountOverlap(ap.Files.Roots,bp.Files.Roots);

        // Identical labels alone are not evidence. Likewise, a local "Alt/Variant" name is not safely
        // composable without source lineage; keep it separate unless concrete file topology proves kinship.
        var concrete=overlap>=.20||sharedAssets>0||sharedRoots>0;
        if(!concrete)return null;
        if(choiceOnly&&!exactStem&&nameSimilarity<.70)return null;

        var score=0;var reasons=new List<string>();
        if(exactStem){score+=45;reasons.Add("same semantic name stem");}
        else if(nameSimilarity>=.80){score+=35;reasons.Add($"name tokens {nameSimilarity:P0}");}
        else if(nameSimilarity>=.60){score+=24;reasons.Add($"name tokens {nameSimilarity:P0}");}
        if(prefix){score+=12;reasons.Add("name prefix lineage");}
        if(roleDifference){score+=12;reasons.Add("component/patch role signal");}
        if(overlap>=.85){score+=42;reasons.Add($"file subset overlap {overlap:P0}");}
        else if(overlap>=.55){score+=32;reasons.Add($"file overlap {overlap:P0}");}
        else if(overlap>=.30){score+=22;reasons.Add($"file overlap {overlap:P0}");}
        else if(overlap>=.20){score+=12;reasons.Add($"file overlap {overlap:P0}");}
        if(sharedAssets>0){score+=24;reasons.Add($"shared asset identity x{sharedAssets}");}
        if(sharedRoots>0){score+=Math.Min(12,4*sharedRoots);reasons.Add($"shared content root x{sharedRoots}");}

        var strong=(roleDifference&&(overlap>=.30||sharedAssets>0)) || (nameSimilarity>=.60&&(overlap>=.55||sharedAssets>0)) || (exactStem&&overlap>=.30);
        return new(a.Id,b.Id,score,strong,string.Join("; ",reasons));
    }

    private static bool HardBlock(ModDescriptor a,ModDescriptor b,CandidateProfile ap,CandidateProfile bp)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!string.IsNullOrWhiteSpace(a.NexusModId)&&!string.IsNullOrWhiteSpace(b.NexusModId)&&!StringComparer.OrdinalIgnoreCase.Equals(a.NexusModId,b.NexusModId))return true;
        var sim=TokenSimilarity(ap.IdentityTokens,bp.IdentityTokens);
        var sharedAssets=ap.Files.Assets.Overlaps(bp.Files.Assets);
        var smaller=Math.Min(ap.Files.Paths.Count,bp.Files.Paths.Count);
        var sharedPaths=CountOverlap(ap.Files.Paths,bp.Files.Paths);
        var overlap=smaller==0?0:sharedPaths/(double)smaller;
        return sim<.25&&!sharedAssets&&overlap<.20;
    }

    private static string IdentityStem(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tokens=Tokenize(CleanName(value));
        while(tokens.Count>1 && (RoleWords.Contains(tokens[^1])||ChoiceWords.Contains(tokens[^1])||VersionTokenRegex().IsMatch(tokens[^1])))tokens.RemoveAt(tokens.Count-1);
        return string.Join(' ',tokens.Where(t=>!StopWords.Contains(t)));
    }

    private static string[] IdentityTokens(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Tokenize(CleanName(value)).Where(t=>!StopWords.Contains(t)&&!RoleWords.Contains(t)&&!ChoiceWords.Contains(t)&&!VersionTokenRegex().IsMatch(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static bool HasRoleSignal(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Tokenize(value).Any(RoleWords.Contains);
    }
    private static bool HasChoiceSignal(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Tokenize(value).Any(ChoiceWords.Contains);
    }
    private static bool IsMeaningfulPrefix(string shorter,string longer)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return shorter.Length>=5&&longer.Length>shorter.Length&&longer.StartsWith(shorter+" ",StringComparison.OrdinalIgnoreCase);
    }
    private static double TokenSimilarity(HashSet<string> a,HashSet<string> b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(a.Count==0||b.Count==0)return 0;
        var smaller=a.Count<=b.Count?a:b;
        var larger=ReferenceEquals(smaller,a)?b:a;
        var hit=0;
        foreach(var token in smaller)if(larger.Contains(token))hit++;
        var union=a.Count+b.Count-hit;
        return union==0?0:hit/(double)union;
    }

    private static int CountOverlap(HashSet<string> a,HashSet<string> b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(a.Count==0||b.Count==0)return 0;
        var smaller=a.Count<=b.Count?a:b;
        var larger=ReferenceEquals(smaller,a)?b:a;
        var count=0;
        foreach(var value in smaller)if(larger.Contains(value))count++;
        return count;
    }
    private static List<string> Tokenize(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return TokenRegex().Matches(value.ToLowerInvariant()).Cast<Match>().Select(m=>m.Value).Where(x=>x.Length>1).ToList();
    }
    private static string CleanName(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var n=NexusSuffixRegex().Replace(value,string.Empty);n=VersionSuffixRegex().Replace(n,string.Empty);return WhitespaceRegex().Replace(n.Replace('_',' ').Replace('-',' ')," ").Trim();
    }
    private static string NormalizeKey(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Join('-',Tokenize(value)).ToLowerInvariant();
    }

    private sealed record FileProfile(HashSet<string> Paths,HashSet<string> Assets,HashSet<string> Roots);
    private sealed record CandidateProfile(
        FileProfile Files,
        string IdentityStem,
        HashSet<string> IdentityTokens,
        bool HasRoleSignal,
        bool HasChoiceSignal);

    [GeneratedRegex(@"-\d{2,7}-\d+(?:-\d+){0,7}(?:\s*\(\d+\))?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex NexusSuffixRegex();
    [GeneratedRegex(@"(?:\s+|[-_])v?\d+(?:\.\d+){0,3}$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex VersionSuffixRegex();
    [GeneratedRegex(@"^v?\d+(?:\.\d+){0,3}$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex VersionTokenRegex();
    [GeneratedRegex(@"[a-z0-9]+(?:-[a-z0-9]+)?",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]private static partial Regex TokenRegex();
    [GeneratedRegex(@"\s+",RegexOptions.CultureInvariant)]private static partial Regex WhitespaceRegex();
}
