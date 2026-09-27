namespace MhwModManager.Core;

public sealed record OverlayResolution(string WinnerModId, ConflictRule Rule);

/// <summary>
/// Immutable lookup structure for conflict rules. The planner creates one per snapshot so
/// conflict evaluation scales with actual rule edges rather than paths x total-rules.
/// </summary>
public sealed class ConflictRuleIndex
{
    private readonly Dictionary<(string,string), ConflictRule> overlays;
    private readonly Dictionary<string, List<(string winner, ConflictRule rule)>> overlayByLoser;
    private readonly Dictionary<string, List<(string other, ConflictRule rule)>> incompatibilitiesByMod;

    private ConflictRuleIndex(
        Dictionary<(string,string), ConflictRule> overlays,
        Dictionary<string, List<(string winner, ConflictRule rule)>> overlayByLoser,
        Dictionary<string, List<(string other, ConflictRule rule)>> incompatibilitiesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.overlays = overlays;
        this.overlayByLoser = overlayByLoser;
        this.incompatibilitiesByMod = incompatibilitiesByMod;
    }

    public static ConflictRuleIndex Create(IEnumerable<ConflictRule> rules)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var overlays = new Dictionary<(string,string), ConflictRule>();
        var incompatibilities = new Dictionary<string, List<(string, ConflictRule)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (rule.LeftModId is null || rule.RightModId is null) continue;
            var key = ConflictEngine.PairKey(rule.LeftModId, rule.RightModId);

            if (rule.Kind == RuleKind.Overlay && rule.Scope == RuleScope.ModPair && rule.WinnerModId is not null)
            {
                // Explicit human rules always beat inferred rules. Within the same class, newer wins
                // deterministically so legacy duplicate rules remain stable.
                if (!overlays.TryGetValue(key, out var current) ||
                    (rule.Explicit && !current.Explicit) ||
                    (rule.Explicit == current.Explicit && rule.CreatedUtc >= current.CreatedUtc))
                    overlays[key] = rule;
            }
            else if (rule.Kind == RuleKind.Incompatible)
            {
                AddIncompatible(rule.LeftModId, rule.RightModId, rule);
                AddIncompatible(rule.RightModId, rule.LeftModId, rule);
            }
        }

        var overlayByLoser = new Dictionary<string, List<(string, ConflictRule)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in overlays.Values)
        {
            if (rule.LeftModId is null || rule.RightModId is null || rule.WinnerModId is null) continue;
            var loser = PathRules.Comparer.Equals(rule.WinnerModId, rule.LeftModId) ? rule.RightModId : rule.LeftModId;
            if (!overlayByLoser.TryGetValue(loser, out var list)) overlayByLoser[loser] = list = [];
            list.Add((rule.WinnerModId, rule));
        }

        return new(overlays, overlayByLoser, incompatibilities);

        void AddIncompatible(string mod, string other, ConflictRule rule)
        {
            if (!incompatibilities.TryGetValue(mod, out var list)) incompatibilities[mod] = list = [];
            list.Add((other, rule));
        }
    }

    public ConflictRule? FindOverlay(string left, string right) =>
        overlays.GetValueOrDefault(ConflictEngine.PairKey(left, right));

    /// <summary>
    /// Returns a unique overlay winner only when every candidate is connected through remembered
    /// or high-confidence auto overlay edges to that winner. Unrelated alternatives therefore do
    /// not become silently ordered merely because one happens to have a higher priority.
    /// </summary>
    public OverlayResolution? FindFullyOrderedOverlayWinner(IReadOnlySet<string> candidateIds)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (candidateIds.Count < 2) return null;
        var roots = GetOverlayRoots(candidateIds);
        if (roots.Count != 1) return null;
        var root = roots[0];

        foreach (var id in candidateIds)
            if (!PathRules.Comparer.Equals(id, root) && !CanReach(id, root, candidateIds)) return null;

        var decidingRule = overlays.Values
            .Where(r => r.WinnerModId is not null && PathRules.Comparer.Equals(r.WinnerModId, root) &&
                        r.LeftModId is not null && r.RightModId is not null &&
                        candidateIds.Contains(r.LeftModId) && candidateIds.Contains(r.RightModId))
            .OrderByDescending(r => r.Explicit)
            .ThenByDescending(r => r.CreatedUtc)
            .FirstOrDefault();
        return decidingRule is null ? null : new(root, decidingRule);
    }

    /// <summary>Overlay-undominated candidates. Texture priority may choose among these roots.</summary>
    public IReadOnlyList<string> GetOverlayRoots(IReadOnlySet<string> candidateIds)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var losers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loser in candidateIds)
        {
            if (!overlayByLoser.TryGetValue(loser, out var edges)) continue;
            if (edges.Any(e => candidateIds.Contains(e.winner))) losers.Add(loser);
        }
        return candidateIds.Where(id => !losers.Contains(id)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool CanReach(string start, string target, IReadOnlySet<string> allowed)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            if (PathRules.Comparer.Equals(current, target)) return true;
            if (!overlayByLoser.TryGetValue(current, out var edges)) continue;
            foreach (var (winner, _) in edges)
                if (allowed.Contains(winner)) stack.Push(winner);
        }
        return false;
    }

    public ConflictRule? FindIncompatible(IReadOnlySet<string> candidateIds)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var id in candidateIds)
        {
            if (!incompatibilitiesByMod.TryGetValue(id, out var edges)) continue;
            foreach (var (other, rule) in edges)
                if (candidateIds.Contains(other)) return rule;
        }
        return null;
    }
}
