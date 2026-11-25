namespace MhwModManager.Core;

/// <summary>A read-only presentation of the actual planner output, never a second resolver.</summary>
public sealed record DeploymentReviewRow(string Action, string Path, string BeforeProvider, string AfterProvider,
    string? BeforeDigest, string? AfterDigest, string ReasonCode, string Detail);

public static class DeploymentReview
{
    public static IReadOnlyList<DeploymentReviewRow> Project(DeploymentPlan plan, IReadOnlyList<ModDescriptor> mods)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Global blockers may deliberately share a pseudo-path (e.g. multiple exclusive groups).
        // Retain every blocker and do not fabricate a partial executable deployment.
        if (plan.IsBlocked)
            return plan.Conflicts.Where(decision => decision.Blocking)
                .Select(decision => new DeploymentReviewRow("Blocked", decision.Path, "Not evaluated",
                    "No safe winner", null, null, decision.ReasonCode, DescribeDecision(decision)))
                .ToArray();

        var names = mods.ToDictionary(mod => mod.Id, mod => mod.DisplayName, PathRules.Comparer);
        var decisions = plan.Conflicts.ToLookup(decision => decision.Path, PathRules.Comparer);
        var changed = new HashSet<string>(PathRules.Comparer);
        var rows = new List<DeploymentReviewRow>(plan.Changes.Count);
        foreach (var change in plan.Changes)
        {
            changed.Add(change.Path);
            var decision = decisions[change.Path].FirstOrDefault();
            var before = ProviderLabel(change.ProviderBefore, names, "Not managed");
            var after = ProviderLabel(change.ProviderAfter, names, change.Kind switch
            {
                ChangeKind.Remove => "Removed",
                ChangeKind.RestoreOriginal => "Original backup",
                _ => "No managed provider"
            });
            rows.Add(new(change.Kind.ToString(), change.Path, before, after,
                change.BeforeBlobSha256, change.AfterBlobSha256, decision?.ReasonCode ?? "deployment-change",
                decision is null ? $"Planner will {change.Kind} this target." : DescribeDecision(decision)));
        }

        // Show normal, already resolved overlaps as information, not as errors or extra writes.
        foreach (var decision in plan.Conflicts)
        {
            if (decision.Kind == ConflictKind.None || changed.Contains(decision.Path)) continue;
            var winner = ProviderLabel(decision.WinnerModId, names, "No managed provider");
            rows.Add(new("Resolved (no file change)", decision.Path, winner, winner,
                null, null, decision.ReasonCode, DescribeDecision(decision)));
        }
        return rows;
    }

    private static string ProviderLabel(string? id, Dictionary<string, string> names, string fallback)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return id is null ? fallback : names.TryGetValue(id, out var name) ? $"{name} [{id}]" : id;
    }

    private static string DescribeDecision(ConflictDecision decision)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var evidence = string.IsNullOrWhiteSpace(decision.Evidence) ? "" : " Evidence: " + decision.Evidence;
        var rule = string.IsNullOrWhiteSpace(decision.RuleId) ? "" : " Rule: " + decision.RuleId;
        return $"{decision.Explanation} ({decision.Kind}; {decision.Confidence}).{evidence}{rule}";
    }
}
