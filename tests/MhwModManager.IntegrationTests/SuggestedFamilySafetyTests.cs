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
            db.ChainManualFamilyAsync("base", ["base"], [["addon"]], "Bundle", Ct,
                refuseExistingPairRules: true));

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
        var family = await db.ChainManualFamilyAsync("base", ["base"], [["addon"]], "Bundle", Ct,
            refuseExistingPairRules: true);

        Assert.StartsWith("manual:", family, StringComparison.OrdinalIgnoreCase);
        var mods = await db.GetModsAsync(Ct);
        Assert.All(mods, mod => Assert.Equal(family, mod.FamilyId));
        var snapshot = await new PlannerSnapshotRepository(db).LoadAsync(Ct);
        Assert.Single(snapshot.Rules, rule =>
            rule.Kind == RuleKind.Overlay && rule.Scope == RuleScope.ModPair && rule.Explicit);
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
