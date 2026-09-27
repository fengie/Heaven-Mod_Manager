namespace MhwModManager.Core;

public static class RuleGraph
{
    public static IReadOnlyList<string>? FindCycle(IEnumerable<ConflictRule> rules)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var edges = new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules.Where(r => r.Kind == RuleKind.Overlay && r.Scope == RuleScope.ModPair))
        {
            var winner = r.WinnerModId;
            var left = r.LeftModId;
            var right = r.RightModId;
            if (winner is null || left is null || right is null) continue;

            var loser = PathRules.Comparer.Equals(winner, left) ? right : left;
            if (!edges.TryGetValue(loser, out var list)) edges[loser] = list = [];
            list.Add(winner);
        }
        var state = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        IReadOnlyList<string>? cycle = null;
        bool Dfs(string node)
        {
            state[node] = 1; stack.Add(node);
            if (edges.TryGetValue(node, out var next))
                foreach (var n in next)
                {
                    state.TryGetValue(n, out var s);
                    if (s == 0 && Dfs(n)) return true;
                    if (s == 1)
                    {
                        var idx = stack.FindIndex(x => PathRules.Comparer.Equals(x, n));
                        cycle = stack.Skip(idx).Concat([n]).ToArray();
                        return true;
                    }
                }
            stack.RemoveAt(stack.Count - 1); state[node] = 2; return false;
        }
        foreach (var node in edges.Keys)
            if (!state.ContainsKey(node) && Dfs(node)) break;
        return cycle;
    }
}
