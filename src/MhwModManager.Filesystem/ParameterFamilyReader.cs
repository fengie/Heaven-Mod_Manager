using System.Text.RegularExpressions;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

/// <summary>A repeated numeric setting for independently selectable situations, declared by an author's note.</summary>
public static class ParameterFamilyReader
{
    private sealed record Pair(string Directory, string Stem, string[] Files);

    public static IReadOnlyList<AuthoredPart> Read(string root, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        root = SafeRecursiveTraversal.EnsureRootIsNotReparse(root);
        var entries = Directory.EnumerateFileSystemEntries(root).Take(49).ToArray();
        if (entries.Length is < 3 or > 48 || entries.Any(IsLinked)) return [];
        var directories = entries.Where(Directory.Exists).ToArray();
        var settings = new Dictionary<string, (string Number, string Unit)>(PathRules.Comparer);
        foreach (var dir in directories)
        {
            var match = Regex.Match(Path.GetFileName(dir), @"^(?<value>[0-9]{1,4})[ _-]+(?<unit>[a-zA-Z]{1,16})$");
            if (match.Success) settings.Add(dir,(match.Groups["value"].Value,match.Groups["unit"].Value));
        }
        if (settings.Count < 2 || settings.Values.Select(s => s.Unit).Distinct(PathRules.Comparer).Count() != 1 ||
            settings.Values.Select(s => s.Number).Distinct().Count() != settings.Count) return [];

        var notes = entries.Except(directories,PathRules.Comparer).ToArray();
        if (notes.Length is 0 or > 8 || notes.Any(n => !ManagedCollectionNotes.IsNote(n) &&
            !Path.GetExtension(n).Equals(".txt",StringComparison.OrdinalIgnoreCase))) return [];
        var text = new System.Text.StringBuilder();
        foreach (var note in notes.Where(n => Path.GetExtension(n).ToLowerInvariant() is ".txt" or ".md"))
        {
            using var input = File.OpenRead(note);
            if (input.Length > 65536) return [];
            using var reader = new StreamReader(input);
            text.AppendLine(reader.ReadToEnd());
        }
        var guide = text.ToString().ReplaceLineEndings("\n");
        if (!Regex.IsMatch(guide, @"\bmix(?:ing)?\s+(?:and|&)\s+match(?:ing)?\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(guide, @"\b(?:not|never|cannot|can't)\s+(?:be\s+)?mix", RegexOptions.IgnoreCase) ||
            !Regex.IsMatch(guide, @"\b(?:situations?|scenarios?|contexts?|per room)\b", RegexOptions.IgnoreCase) ||
            !guide.Contains("pak",StringComparison.OrdinalIgnoreCase) || !guide.Contains("sig",StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(guide, @"\b(?:pair(?:ed|s)?|both|same[ -]name|same filename|same file name)\b", RegexOptions.IgnoreCase))
            return [];

        var parts = new List<AuthoredPart>();
        var primaryGroups = new HashSet<string>(PathRules.Comparer);
        var secondaryGroups = new HashSet<string>(PathRules.Comparer);
        string? commonPrefix = null, secondaryName = null;
        foreach (var (dir,setting) in settings.OrderBy(s => s.Key,PathRules.Comparer))
        {
            var nested = Directory.EnumerateDirectories(dir).Take(2).ToArray();
            if (nested.Length > 1 || nested.Any(IsLinked)) return [];
            var seen = new HashSet<string>(PathRules.Comparer);
            var localPrimary = new HashSet<string>(PathRules.Comparer);
            foreach (var location in nested.Prepend(dir))
            {
                if (location != dir)
                {
                    secondaryName ??= Path.GetFileName(location);
                    if (!PathRules.Comparer.Equals(secondaryName,Path.GetFileName(location)) ||
                        Directory.EnumerateDirectories(location).Any()) return [];
                }
                var pairs = ReadPairs(location,ct);
                if (pairs.Length == 0) return [];
                var marker = setting.Number + setting.Unit;
                foreach (var pair in pairs)
                {
                    var at = pair.Stem.IndexOf(marker,StringComparison.OrdinalIgnoreCase);
                    if (at < 1 || pair.Stem.IndexOf(marker,at+marker.Length,StringComparison.OrdinalIgnoreCase) >= 0)
                        return [];
                    var prefix = pair.Stem[..at];
                    var situation = pair.Stem[(at+marker.Length)..];
                    commonPrefix ??= prefix;
                    if (!PathRules.Comparer.Equals(commonPrefix,prefix) || !IsLabel(situation) || !seen.Add(situation))
                        return [];
                    if (location == dir) localPrimary.Add(situation);
                    else secondaryGroups.Add(situation);
                    parts.Add(new(pair.Stem,pair.Directory,"Option",situation,pair.Files));
                }
            }
            if (localPrimary.Count < 2 || (primaryGroups.Count > 0 && !primaryGroups.SetEquals(localPrimary))) return [];
            primaryGroups.UnionWith(localPrimary);
        }
        if (primaryGroups.Overlaps(secondaryGroups)) return [];
        foreach (var dir in directories.Except(settings.Keys,PathRules.Comparer).Order(PathRules.Comparer))
        {
            if (Directory.EnumerateDirectories(dir).Any()) return [];
            var pairs = ReadPairs(dir,ct);
            if (pairs.Length == 0) return [];
            var name = Path.GetFileName(dir);
            // The opposite room setting has a matching author label in every filename and
            // shares actual rooms with the parameter choices. Do not invent missing choices.
            if (secondaryName != null && name.StartsWith("No"+secondaryName,StringComparison.OrdinalIgnoreCase))
            {
                var situations = new HashSet<string>(PathRules.Comparer);
                string? common = null;
                foreach (var pair in pairs)
                {
                    var at = pair.Stem.IndexOf(name,StringComparison.OrdinalIgnoreCase);
                    if (at < 0) return [];
                    var situation = pair.Stem[(at+name.Length)..];
                    common ??= pair.Stem[..at];
                    if (!PathRules.Comparer.Equals(common,pair.Stem[..at]) || !IsLabel(situation) ||
                        !situations.Add(situation) || primaryGroups.Contains(situation)) return [];
                    parts.Add(new(pair.Stem,dir,"Option",situation,pair.Files));
                }
                if (!secondaryGroups.Overlaps(situations)) return [];
            }
            else
            {
                // A distinct file proves a physical pair, but doesn't establish that it can
                // be enabled alongside every other helper. Retain unproven relations for review.
                var scope = pairs.Select(pair => HelperSituation(pair.Stem,guide,primaryGroups)).ToArray();
                for (var i = 0; i < pairs.Length; i++)
                {
                    var situation = scope[i];
                    var independent = situation != null && scope.Count(s => PathRules.Comparer.Equals(s,situation)) == 1;
                    var pair = pairs[i];
                    parts.Add(new(pair.Stem,dir,independent ? "Optional" : "Review",null,pair.Files));
                }
            }
        }
        if (parts.Select(p => p.Label).Distinct(PathRules.Comparer).Count() != parts.Count) return [];
        return parts.OrderBy(p => p.Label,PathRules.Comparer).ToArray();
    }

    private static string? HelperSituation(string stem, string guide, HashSet<string> situations)
    {
        using var trace = MasterDebugLog.BeginMethod();
        // Unreal packages commonly prefix an author's name with z for load order.
        // Accept only an actual name = description entry with one documented scope.
        var names = new[] { stem, stem.TrimStart('z','Z') }.Where(IsLabel).Select(Regex.Escape);
        var pattern = @"^[ \t]*(?:[-*][ \t]+)?(?:" + string.Join("|",names) + @")\s*=\s*(?<description>[^\r\n]+)$";
        var definitions = Regex.Matches(guide,pattern,RegexOptions.IgnoreCase|RegexOptions.Multiline);
        if (definitions.Count != 1) return null;
        var description = definitions[0].Groups["description"].Value;
        if (Regex.IsMatch(description,@"\b(requires?|incompatible|conflicts?|instead|unless|only with)\b",RegexOptions.IgnoreCase))
            return null;
        var matches = situations.Where(s => Regex.IsMatch(description,@"\b" + Regex.Escape(s) + @"\b",RegexOptions.IgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsLinked(string path)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsLabel(string value)
    {
        using var trace = MasterDebugLog.BeginMethod();
        return value.Length is > 0 and <= 64 && value.All(char.IsAsciiLetterOrDigit);
    }

    private static Pair[] ReadPairs(string directory, CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        var files = Directory.EnumerateFiles(directory).Take(65).ToArray();
        if (files.Length is 0 or > 64 || files.Any(IsLinked)) return [];
        var pairs = new List<Pair>();
        foreach (var group in files.Where(file=>!ManagedCollectionNotes.IsShellMetadata(file)).GroupBy(file => Path.GetFileNameWithoutExtension(file),PathRules.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            var members = group.Select(file => Path.GetFileName(file)).Order(PathRules.Comparer).ToArray();
            if (members.Length != 2 || !Path.GetExtension(members[0]).Equals(".pak",StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(members[1]).Equals(".sig",StringComparison.OrdinalIgnoreCase) ||
                !UnrealPakLayout.Complete(members,singleStem:true)) return [];
            pairs.Add(new(directory,group.Key,members));
        }
        return pairs.ToArray();
    }
}
