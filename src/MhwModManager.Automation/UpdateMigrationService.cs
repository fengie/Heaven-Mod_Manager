using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed record UpdateMigrationPreview(string OlderId, string NewerId, UpdateDiffSummary Diff,
    int TransferableRules, int TotalRules, IReadOnlyList<string> Warnings, PlannerSnapshot Snapshot,
    DeploymentPlan Plan, IReadOnlyList<MetadataRowChange> Metadata);

public sealed class UpdateMigrationService(ManagerDatabase db, DeploymentPlanner planner, DeploymentExecutor executor)
{
    public async Task<UpdateMigrationPreview> PreviewAsync(string olderId, string newerId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (StringComparer.OrdinalIgnoreCase.Equals(olderId, newerId)) throw new InvalidDataException("Choose two different packages.");
        var snapshot = await db.LoadPlannerSnapshotAsync(ct);
        var older = snapshot.Mods.Single(m => m.Id == olderId); var newer = snapshot.Mods.Single(m => m.Id == newerId);
        if (older.IsSuperseded || newer.IsSuperseded) throw new InvalidDataException("Choose current, non-superseded packages.");
        if (!snapshot.Files.Any(f => f.ModId == newerId)) throw new InvalidDataException("Capture the replacement package before migrating.");
        if (older.NexusModId is not null && newer.NexusModId is not null && older.NexusModId != newer.NexusModId) throw new InvalidDataException("The packages have different Nexus mod identities.");
        var warnings = new List<string>(); var edits = new List<MetadataRowChange>();
        var rules = new List<ConflictRule>(); var total = 0; var transferable = 0;
        foreach (var rule in snapshot.Rules)
        {
            if (rule.LeftModId != olderId && rule.RightModId != olderId && rule.WinnerModId != olderId) { rules.Add(rule); continue; }
            total++;
            var mapped = rule with { LeftModId = rule.LeftModId == olderId ? newerId : rule.LeftModId, RightModId = rule.RightModId == olderId ? newerId : rule.RightModId, WinnerModId = rule.WinnerModId == olderId ? newerId : rule.WinnerModId };
            if (mapped.LeftModId is not null && mapped.LeftModId == mapped.RightModId) { warnings.Add("The relationship between old and replacement packages is retired: " + rule.Reason); continue; }
            if (mapped.Kind == RuleKind.ExactWinner && mapped.WinnerModId == newerId && !snapshot.Files.Any(f => f.ModId == newerId && PathRules.Comparer.Equals(f.Path, mapped.PathPattern)))
                throw new InvalidDataException("Cannot transfer exact-file rule; replacement lacks " + mapped.PathPattern + ". Edit the rule first.");
            rules.Add(mapped); transferable++;
        }
        foreach (var table in new[] { "conflict_rules", "resource_providers", "family_preferences", "mod_family_members" })
        {
            foreach (var row in await DeploymentMetadata.ReadRowsAsync(db, table, ct))
            {
                var columns = table switch { "conflict_rules" => new[] { "left_mod_id", "right_mod_id", "winner_mod_id" }, "family_preferences" => ["selected_mod_id"], _ => ["mod_id"] };
                if (!columns.Any(c => row.Before![c] == olderId)) continue;
                foreach (var column in columns) if (row.After![column] == olderId) row.After[column] = newerId;
                if (table == "conflict_rules" && !rules.Any(r => r.Id == row.Key["id"])) edits.Add(row with { After = null }); else edits.Add(row);
            }
        }
        var removedRuleIds = edits.Where(e => e.Table == "conflict_rules" && e.After is null).Select(e => e.Key["id"]).ToHashSet(StringComparer.Ordinal);
        var profileRuleRemovals = (await DeploymentMetadata.ReadRowsAsync(db, "profile_rules", ct)).Where(e => removedRuleIds.Contains(e.Before!["rule_id"]))
            .Select(e => e with { After = null }).ToArray();
        // Remove dependent links explicitly, so inverse edits can restore them after restoring the rule.
        edits.InsertRange(0, profileRuleRemovals);
        var existingMembership = (await DeploymentMetadata.ReadRowsAsync(db, "mod_family_members", ct)).Where(r => r.Before!["mod_id"] == newerId).ToArray();
        if (existingMembership.Length > 0 && edits.Any(e => e.Table == "mod_family_members"))
            throw new InvalidDataException("The replacement already has explicit family membership. Resolve it before transferring the old membership.");
        var modRow = (await DeploymentMetadata.ReadRowsAsync(db, "mods", ct)).Single(r => r.Key["id"] == newerId);
        modRow.After!["family_id"] = older.FamilyId;
        if (!DeploymentMetadata.Same(modRow.Before, modRow.After)) edits.Add(modRow);
        var supersession = new Dictionary<string, string?>(StringComparer.Ordinal) { ["older_mod_id"] = olderId, ["newer_mod_id"] = newerId, ["confidence_score"] = "100", ["reason"] = "User-approved update migration", ["created_at"] = DateTimeOffset.UtcNow.ToString("O") };
        edits.Add(new("mod_supersession", new(StringComparer.Ordinal) { ["older_mod_id"] = olderId }, null, supersession));
        var resources = snapshot.ResourceProviders.ToDictionary(p => p.Key, p => p.Value == olderId ? newerId : p.Value, PathRules.Comparer);
        foreach (var resource in snapshot.ResourceProviders.Where(p => p.Value == olderId))
            if (!snapshot.Files.Any(f => f.ModId == newerId && PathRules.Comparer.Equals(PathRules.ResourceNamespace(f.Path), resource.Key))) throw new InvalidDataException("Replacement cannot provide pinned resource " + resource.Key);
        var staged = snapshot with
        {
            Mods = snapshot.Mods.Select(m => m.Id == olderId ? m with { Enabled = false, IsSuperseded = true, SupersededByModId = newerId }
                : m.Id == newerId ? m with { Enabled = older.Enabled, Priority = older.Priority, FamilyId = older.FamilyId, FamilyRole = older.FamilyRole } : m).ToArray(),
            Rules = rules,
            ExactWinners = rules.Where(r => r.Kind == RuleKind.ExactWinner && r.PathPattern is not null && r.WinnerModId is not null).ToDictionary(r => r.PathPattern!, r => r.WinnerModId!, PathRules.Comparer),
            ResourceProviders = resources
        };
        if (older.FamilyId != newer.FamilyId) warnings.Add("Replacement family becomes " + (older.FamilyId ?? "none") + "; existing optional packages and selections are preserved.");
        warnings.Add("Replacement uses its installed payload and installer selections. Review FOMOD choices when installing the new archive; version-specific option IDs are not guessed.");
        return new(olderId, newerId, await new UpdateDiffService(db).CompareAsync(olderId, newerId, ct), transferable, total, warnings, staged, planner.Build(staged), edits);
    }

    public async Task<OperationResult> UpgradeAsync(string olderId, string newerId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var preview = await PreviewAsync(olderId, newerId, ct);
        var state = preview.Snapshot.Mods.ToDictionary(m => m.Id, m => (m.Enabled, m.Priority), StringComparer.OrdinalIgnoreCase);
        return await executor.ApplyWithMetadataAsync(preview.Plan, "Update migration " + olderId + " → " + newerId, preview.Metadata, state, ct: ct);
    }
}
