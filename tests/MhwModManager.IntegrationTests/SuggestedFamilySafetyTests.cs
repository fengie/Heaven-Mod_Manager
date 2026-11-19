using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>Review suggestions must not silently remove a previously saved human rule.</summary>
public sealed class SuggestedFamilySafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-suggested-family-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Suggested_family_rejects_existing_pair_rule_without_losing_original_choice()
    {
        var db = await NewDatabaseAsync();
        await using (var connection = await db.OpenAsync(Ct))
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
                VALUES('existing-incompatible','Incompatible','ModPair','base','addon',NULL,NULL,'user choice',1,$at)
                """;
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.ChainManualFamilyAsync("base", ["base"], [["addon"]], "Bundle", refuseExistingPairRules: true, ct: Ct));

        var mods = await db.GetModsAsync(Ct);
        Assert.All(mods, mod => Assert.Null(mod.FamilyId));
        var snapshot = await new PlannerSnapshotRepository(db).LoadAsync(Ct);
        var rule = Assert.Single(snapshot.Rules);
        Assert.Equal("existing-incompatible", rule.Id);
        Assert.Equal(RuleKind.Incompatible, rule.Kind);
    }

    [Fact]
    public async Task Suggested_family_can_persist_explicitly_confirmed_relation_without_preexisting_pair_rule()
    {
        var db = await NewDatabaseAsync();
        var family = await db.ChainManualFamilyAsync("base", ["base"], [["addon"]], "Bundle", refuseExistingPairRules: true, ct: Ct);

        Assert.StartsWith("manual:", family, StringComparison.OrdinalIgnoreCase);
        var mods = await db.GetModsAsync(Ct);
        Assert.All(mods, mod => Assert.Equal(family, mod.FamilyId));
        var snapshot = await new PlannerSnapshotRepository(db).LoadAsync(Ct);
        Assert.Single(snapshot.Rules, rule =>
            rule.Kind == RuleKind.Overlay && rule.Scope == RuleScope.ModPair && rule.Explicit);
    }

    [Fact]
    public async Task Separating_a_false_manual_family_clears_only_its_generated_overlay_rules()
    {
        var db = await NewDatabaseAsync();
        var priorFamily = await db.ChainManualFamilyAsync("base", ["base"], [["addon"]], "Misgrouped", ct: Ct);

        // An independent user-authored incompatibility must survive separation.
        await using (var connection = await db.OpenAsync(Ct))
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
                VALUES('user-incompatible','Incompatible','ModPair','base','addon',NULL,NULL,'explicit incompatibility',1,$at)
                """;
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        await db.SeparateLogicalMemberAsync("addon", Ct);

        var mods = await db.GetModsAsync(Ct);
        Assert.Equal(2, LogicalModFamilies.Build(mods).Count);
        Assert.Equal(priorFamily, Assert.Single(mods, mod => mod.Id == "base").FamilyId);
        var detached = Assert.Single(mods, mod => mod.Id == "addon");
        Assert.StartsWith("manual:split:", detached.FamilyId, StringComparison.OrdinalIgnoreCase);
        Assert.Null(detached.FamilyRole);
        var snapshot = await new PlannerSnapshotRepository(db).LoadAsync(Ct);
        var remaining = Assert.Single(snapshot.Rules);
        Assert.Equal("user-incompatible", remaining.Id);
        Assert.Equal(RuleKind.Incompatible, remaining.Kind);
    }

    [Fact]
    public async Task Missing_separation_target_rolls_back_without_changing_an_existing_family()
    {
        var db = await NewDatabaseAsync();
        var priorFamily = await db.ChainManualFamilyAsync("base", ["base"], [["addon"]], "Still linked", ct: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.SeparateLogicalMemberAsync("nonexistent", Ct));

        var mods = await db.GetModsAsync(Ct);
        Assert.All(mods, mod => Assert.Equal(priorFamily, mod.FamilyId));
        Assert.Single((await new PlannerSnapshotRepository(db).LoadAsync(Ct)).Rules);
    }

    private async Task<ManagerDatabase> NewDatabaseAsync()
    {
        var db = new ManagerDatabase(Path.Combine(root, Guid.NewGuid().ToString("N") + ".db"));
        await db.InitializeAsync(Ct);
        await db.UpsertModAsync(new ModDescriptor("base", "Base", "Base", Path.Combine(root, "a"), false, 1), Ct);
        await db.UpsertModAsync(new ModDescriptor("addon", "Addon", "Addon", Path.Combine(root, "b"), false, 2), Ct);
        return db;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
