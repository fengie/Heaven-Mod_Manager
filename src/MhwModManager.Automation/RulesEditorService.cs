using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class RulesEditorService(ManagerDatabase db)
{
    public async Task SaveAsync(ConflictRule rule, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule.Reason);
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        var ids = snapshot.Mods.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in new[] { rule.LeftModId, rule.RightModId, rule.WinnerModId }.Where(x => x is not null))
            if (!ids.Contains(id!)) throw new InvalidDataException("Rule references an unknown mod: " + id);
        if (rule.Kind is RuleKind.Overlay or RuleKind.Incompatible)
        {
            if (rule.Scope != RuleScope.ModPair) throw new InvalidDataException("Overlay and incompatibility rules require ModPair scope.");
            if (rule.LeftModId is null || rule.RightModId is null || StringComparer.OrdinalIgnoreCase.Equals(rule.LeftModId, rule.RightModId))
                throw new InvalidDataException("Choose two different mods.");
            if (rule.Kind == RuleKind.Overlay && !new[] { rule.LeftModId, rule.RightModId }.Contains(rule.WinnerModId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("The winner must be one of the two mods.");
        }
        if (rule.Kind == RuleKind.ExactWinner)
        {
            if (rule.WinnerModId is null || rule.Scope != RuleScope.ExactPath || rule.PathPattern is null) throw new InvalidDataException("Exact winners require a file and a provider.");
            var path = PathRules.Normalize(rule.PathPattern);
            if (!snapshot.Files.Any(f => StringComparer.OrdinalIgnoreCase.Equals(f.ModId, rule.WinnerModId) && PathRules.Comparer.Equals(f.Path, path)))
                throw new InvalidDataException("The selected mod does not provide this file.");
            if (snapshot.Rules.Any(r => r.Id != rule.Id && r.Kind == RuleKind.ExactWinner && PathRules.Comparer.Equals(r.PathPattern, path)))
                throw new InvalidDataException("An exact-file rule already exists for this path. Select and edit that rule.");
            rule = rule with { PathPattern = path };
        }
        if (rule.Kind == RuleKind.ResourceProvider) throw new InvalidDataException("Use SaveProviderAsync for shared resource namespaces.");
        if (rule.Scope == RuleScope.PathPrefix && rule.PathPattern is not null) rule = rule with { PathPattern = PathRules.Normalize(rule.PathPattern) };
        var rules = snapshot.Rules.Where(r => r.Id != rule.Id).Append(rule).ToArray();
        var cycle = RuleGraph.FindCycle(rules);
        if (cycle is not null) throw new InvalidDataException("Overlay cycle: " + string.Join(" → ", cycle));
        await db.ExecuteAsync("INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at) VALUES($i,$k,$s,$l,$r,$w,$p,$d,1,$u) ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,scope=excluded.scope,left_mod_id=excluded.left_mod_id,right_mod_id=excluded.right_mod_id,winner_mod_id=excluded.winner_mod_id,path_pattern=excluded.path_pattern,reason=excluded.reason,explicit=1",
            new Dictionary<string, object?> { ["$i"] = rule.Id, ["$k"] = rule.Kind.ToString(), ["$s"] = rule.Scope.ToString(), ["$l"] = rule.LeftModId, ["$r"] = rule.RightModId, ["$w"] = rule.WinnerModId, ["$p"] = rule.PathPattern, ["$d"] = rule.Reason, ["$u"] = DateTimeOffset.UtcNow.ToString("O") }, ct);
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await db.ExecuteAsync("DELETE FROM conflict_rules WHERE id=$i", new Dictionary<string, object?> { ["$i"] = id }, ct);
    }

    public async Task SaveProviderAsync(string resourceNamespace, string? modId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var path = PathRules.Normalize(resourceNamespace);
        if (modId is null)
        {
            await db.ExecuteAsync("DELETE FROM resource_providers WHERE namespace=$p", new Dictionary<string, object?> { ["$p"] = path }, ct); return;
        }
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        if (!snapshot.Files.Any(f => StringComparer.OrdinalIgnoreCase.Equals(f.ModId, modId) && PathRules.Comparer.Equals(PathRules.ResourceNamespace(f.Path), path)))
            throw new InvalidDataException("The mod does not provide this resource namespace.");
        await db.ExecuteAsync("INSERT INTO resource_providers(namespace,mod_id) VALUES($p,$m) ON CONFLICT(namespace) DO UPDATE SET mod_id=excluded.mod_id",
            new Dictionary<string, object?> { ["$p"] = path, ["$m"] = modId }, ct);
    }
}
