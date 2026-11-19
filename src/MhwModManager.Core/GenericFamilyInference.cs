using System.Text.RegularExpressions;

namespace MhwModManager.Core;

/// <summary>
/// Generic evidence-based family inference. It intentionally does not know mod brands/authors.
/// Source metadata is supporting evidence. Distinctively shared payload bytes may also identify
/// separately acquired components, but matching names, paths, or boilerplate alone are insufficient.
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

    private static readonly IReadOnlySet<string> ChoiceWords = ModChoiceSignals.PickOneTokens;

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
        // A common resource bundled into every download is not a family anchor.
        // Count distinct packages, not occurrences within one archive.
        var rareContentAnchors = profiles.Values.SelectMany(profile => profile.Files.ContentAnchors)
            .GroupBy(anchor => anchor, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 2)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairs = new List<Evidence>();
        foreach (var (i,j) in ConcreteCandidatePairs(candidates,profiles,rareContentAnchors))
        {
            var evidence = ScorePair(candidates[i], candidates[j], profiles[candidates[i].Id], profiles[candidates[j].Id], rareContentAnchors);
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
            using var __mhwTrace = MasterDebugLog.BeginMethod();
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
            var blocked=false;
            foreach(var leftId in leftMembers)
            {
                foreach(var rightId in rightMembers)
                {
                    if(!HardBlock(candidateById[leftId],candidateById[rightId],profiles[leftId],profiles[rightId],rareContentAnchors))continue;
                    blocked=true;
                    break;
                }
                if(blocked)break;
            }
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

    private static IEnumerable<(int Left,int Right)> ConcreteCandidatePairs(
        ModDescriptor[] candidates,
        IReadOnlyDictionary<string,CandidateProfile> profiles,
        IReadOnlySet<string> rareContentAnchors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var postings=new Dictionary<string,List<int>>(StringComparer.OrdinalIgnoreCase);
        for(var i=0;i<candidates.Length;i++)
        {
            var profile=profiles[candidates[i].Id];
            // Shared folder roots alone never satisfy ScorePair's strong evidence gate.
            // Their high-fanout Cartesian product is therefore redundant. Paths and
            // semantic assets cover overlap/namespace evidence; distinctive normalized
            // path+SHA256 anchors also preserve lineage across raw separator differences.
            AddPostings("p:",profile.Files.Paths,i);
            AddPostings("a:",profile.Files.Assets,i);
            AddPostings("h:",profile.Files.ContentAnchors.Where(rareContentAnchors.Contains),i);
        }

        var encodedPairs=new HashSet<long>();
        foreach(var members in postings.Values)
        {
            for(var left=0;left<members.Count;left++)
            for(var right=left+1;right<members.Count;right++)
            {
                var i=members[left];var j=members[right];
                if(i>j)(i,j)=(j,i);
                encodedPairs.Add(((long)i<<32)|(uint)j);
            }
        }

        foreach(var encoded in encodedPairs.Order())
            yield return ((int)(encoded>>32),(int)(encoded&uint.MaxValue));

        void AddPostings(string prefix,IEnumerable<string> values,int index)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            foreach(var value in values)
            {
                var key=prefix+value;
                if(!postings.TryGetValue(key,out var members))
                {
                    members=new List<int>();
                    postings[key]=members;
                }
                members.Add(index);
            }
        }
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
                IdentityTokens(m.DisplayName),
                HasRoleSignal(m.DisplayName),
                HasChoiceSignal(m.Name + " " + m.DisplayName)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static FileProfile BuildFileProfile(IReadOnlyList<ModFileDescriptor> files,bool includeMhwAssetSemantics)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var paths=files.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anchors=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files)
        {
            try
            {
                if(includeMhwAssetSemantics && PathRules.TryGetArmorComponent(file.Path,out var modelId,out _))assets.Add("armor:"+modelId);
                if(includeMhwAssetSemantics)
                {
                    var ns=PathRules.ResourceNamespace(file.Path);if(ns is not null)assets.Add("resource:"+ns);
                }
                var normalized=PathRules.Normalize(file.Path);
                // Exact path + captured SHA-256 of real deployable assets is stronger
                // than labels. Do not treat tiny files or shared loader binaries as anchors.
                if (file.Length >= 128 &&
                    file.FileClass is FileClass.Structural or FileClass.Texture &&
                    file.BlobSha256 is { Length: 64 } &&
                    file.BlobSha256.All(Uri.IsHexDigit))
                    anchors.Add(normalized.ToLowerInvariant() + "|" + file.BlobSha256.ToLowerInvariant());
                var segments=normalized.Split('\\');
                if(segments.Length>=4)roots.Add(string.Join("\\",segments.Take(4)).ToLowerInvariant());
                else if(segments.Length>=2)roots.Add(string.Join("\\",segments.Take(2)).ToLowerInvariant());
            }
            catch(ArgumentException){ }
        }
        return new(paths,assets,roots,anchors);
    }

    private static Evidence? ScorePair(ModDescriptor a,ModDescriptor b,CandidateProfile ap,CandidateProfile bp,IReadOnlySet<string> rareContentAnchors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!string.IsNullOrWhiteSpace(a.NexusModId)&&!string.IsNullOrWhiteSpace(b.NexusModId)&&!StringComparer.OrdinalIgnoreCase.Equals(a.NexusModId,b.NexusModId))return null;

        var nameSimilarity=TokenSimilarity(ap.IdentityTokens,bp.IdentityTokens);
        var exactStem=ap.IdentityStem.Length>0&&StringComparer.OrdinalIgnoreCase.Equals(ap.IdentityStem,bp.IdentityStem);
        var prefix=IsMeaningfulPrefix(ap.IdentityStem,bp.IdentityStem)||IsMeaningfulPrefix(bp.IdentityStem,ap.IdentityStem);
        // An explicit pick-one cue is negative evidence for automatic composition.
        // Overlapping target paths and a matching name stem cannot overturn it.
        // Saved human decisions and source-scoped Nexus grouping are handled
        // independently by LogicalModFamilies and remain authoritative.
        if(ap.HasChoiceSignal||bp.HasChoiceSignal)return null;
        var roleDifference=ap.HasRoleSignal||bp.HasRoleSignal;
        var contentLineage=HasContentLineage(ap,bp,rareContentAnchors,out var sharedContentAnchors);

        // Ordinary inference requires a role signal,
        // matching names, or an exact stem. Two distinct *rare byte-identical assets*
        // also permit family grouping without shared names; shared directories do not.
        if(!roleDifference && nameSimilarity < .60 && !exactStem && !contentLineage) return null;

        var sharedPaths=IntersectionCount(ap.Files.Paths,bp.Files.Paths);
        var smaller=Math.Min(ap.Files.Paths.Count,bp.Files.Paths.Count);
        var overlap=smaller==0?0:sharedPaths/(double)smaller;
        var sharedAssets=IntersectionCount(ap.Files.Assets,bp.Files.Assets);
        // No returned evidence can be accepted unless it is strong.
        // Compute that exact predicate before allocating explanations or scanning
        // shared roots: high-fanout loader roots often connect unrelated files.
        var strong=contentLineage || (roleDifference&&(overlap>=.30||sharedAssets>0)) ||
            (nameSimilarity>=.60&&(overlap>=.55||sharedAssets>0)) ||
            (exactStem&&overlap>=.30);
        if(!strong)return null;
        var sharedRoots=IntersectionCount(ap.Files.Roots,bp.Files.Roots);

        static int IntersectionCount(HashSet<string> left,HashSet<string> right)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            if(left.Count==0||right.Count==0)return 0;
            var scan=left.Count<=right.Count?left:right;
            var lookup=ReferenceEquals(scan,left)?right:left;
            var count=0;
            foreach(var value in scan)if(lookup.Contains(value))count++;
            return count;
        }

        var score=0;var reasons=new List<string>();
        if(contentLineage){score+=90;reasons.Add($"{sharedContentAnchors} matching path-and-SHA256 payload anchors with distinctive content, independent of names/sources");}
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

        return new(a.Id,b.Id,score,strong,string.Join("; ",reasons));
    }

    // Content agreement proves a possible logical family, never overwrite order.
    // Require at least two pair-distinctive nontrivial content anchors, OR at least
    // three matching payload anchors including one pair-distinctive anchor. A later
    // unrelated package copying two common files must not erase a stronger A/B
    // relationship. All-common boilerplate remains insufficient; an inherently
    // ambiguous 2-anchor overlap must still abstain.
    private static bool HasContentLineage(CandidateProfile a,CandidateProfile b,
        IReadOnlySet<string> rareContentAnchors,out int sharedAnchors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        sharedAnchors=0;
        if(a.HasChoiceSignal||b.HasChoiceSignal||
            a.Files.ContentAnchors.Count<2||b.Files.ContentAnchors.Count<2)
            return false;
        var small=a.Files.ContentAnchors.Count<=b.Files.ContentAnchors.Count
            ?a.Files.ContentAnchors:b.Files.ContentAnchors;
        var large=ReferenceEquals(small,a.Files.ContentAnchors)
            ?b.Files.ContentAnchors:a.Files.ContentAnchors;
        var pairDistinctive=0;
        foreach(var anchor in small)
        {
            if(!large.Contains(anchor))continue;
            sharedAnchors++;
            if(rareContentAnchors.Contains(anchor))pairDistinctive++;
        }
        // Preserve distinctive evidence when another package later copies a
        // subset, without admitting any group based on wholly common files.
        return (pairDistinctive>=2 || (pairDistinctive>=1 && sharedAnchors>=3)) &&
            a.Files.Paths.Count>=3 && b.Files.Paths.Count>=3 &&
            !a.Files.Paths.SetEquals(b.Files.Paths);
    }

    private static bool HardBlock(ModDescriptor a,ModDescriptor b,CandidateProfile ap,CandidateProfile bp,IReadOnlySet<string> rareContentAnchors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!string.IsNullOrWhiteSpace(a.NexusModId)&&!string.IsNullOrWhiteSpace(b.NexusModId)&&!StringComparer.OrdinalIgnoreCase.Equals(a.NexusModId,b.NexusModId))return true;
        if(HasContentLineage(ap,bp,rareContentAnchors,out _))return false;
        var sim=TokenSimilarity(ap.IdentityTokens,bp.IdentityTokens);
        // A third package with unrelated identity cannot bridge two inferred
        // components solely by replacing the same model/texture namespace.
        if(sim<.25)return true;
        return false;
    }

    private static string IdentityStem(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tokens=Tokenize(CleanName(value));
        while(tokens.Count>1 && (RoleWords.Contains(tokens[^1])||ChoiceWords.Contains(tokens[^1])||VersionTokenRegex().IsMatch(tokens[^1])))tokens.RemoveAt(tokens.Count-1);
        return string.Join(' ',tokens.Where(t=>!StopWords.Contains(t)));
    }

    private static HashSet<string> IdentityTokens(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Tokenize(CleanName(value))
            .Where(t=>!StopWords.Contains(t)&&!RoleWords.Contains(t)&&!ChoiceWords.Contains(t)&&!VersionTokenRegex().IsMatch(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static List<string> Tokenize(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return TokenRegex().Matches(value.ToLowerInvariant()).Cast<Match>().Select(m=>m.Value).Where(x=>x.Length>1).ToList();
    }
    private static string CleanName(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var n=ModDisplayNames.StripTransportDigest(NexusSuffixRegex().Replace(NexusArchiveIdentity.RemoveSuffix(value),string.Empty));n=VersionSuffixRegex().Replace(n,string.Empty);return WhitespaceRegex().Replace(n.Replace('_',' ').Replace('-',' ')," ").Trim();
    }
    private static string NormalizeKey(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Join('-',Tokenize(value)).ToLowerInvariant();
    }

    private sealed record FileProfile(HashSet<string> Paths,HashSet<string> Assets,HashSet<string> Roots,HashSet<string> ContentAnchors);
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
