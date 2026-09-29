using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class WorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-workflows-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public WorkflowTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
    private async Task<ManagerDatabase> DatabaseAsync()
    {
        var db = new ManagerDatabase(Path.Combine(root, Guid.NewGuid().ToString("N") + ".db"));
        await db.InitializeAsync(Token);
        return db;
    }
    private async Task SeedAsync(ManagerDatabase db, string id, bool enabled = false, string? nexusFile = null)
    {
        await db.UpsertModAsync(new(id, id, id, Path.Combine(root, id), enabled, 1, NexusModId: "42", NexusFileId: nexusFile, NexusVersion: "1"), Token);
        await db.ReplaceModFilesAsync(id, [new(id, @"nativePC\a.tex", new string('a', 64), null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
    }
    [Fact]
    public async Task RecipeRoundTripRemapsNexusIdentityWithoutEnablingLiveMods()
    {
        var source = await DatabaseAsync(); await SeedAsync(source, "old-pc-id", true, "7");
        var path = Path.Combine(root, "setup.mhwrecipe");
        await new CollectionRecipeService(source).ExportAsync(path, Token);
        var target = await DatabaseAsync(); await SeedAsync(target, "new-pc-id", false, "7");
        var service = new CollectionRecipeService(target);
        var preview = await service.PreviewAsync(path, Token);
        Assert.Equal(RecipeMatchKind.FoundThroughNexusIdentity, Assert.Single(preview.Matches).Kind);
        var profile = await service.ImportAsync(path, "Imported", Token);
        Assert.True((await new ProfileRepository(target).LoadAsync(profile, Token))["new-pc-id"].enabled);
        Assert.False(Assert.Single(await target.GetModsAsync(Token)).Enabled);
    }
    [Fact]
    public async Task RecipeHashMismatchCannotRestore()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "a", true);
        var path = Path.Combine(root, "hash.ummpack"); var service = new CollectionRecipeService(db);
        await service.ExportAsync(path, Token);
        await db.ReplaceModFilesAsync("a", [new("a", @"nativePC\a.tex", new string('b', 64), null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        Assert.Equal(RecipeMatchKind.HashMismatch, Assert.Single((await service.PreviewAsync(path, Token)).Matches).Kind);
        var profile = await service.ImportAsync(path, "Mismatch", Token);
        Assert.False((await new ProfileRepository(db).LoadAsync(profile, Token))["a"].enabled);
    }
    [Fact]
    public async Task RecipeRejectsDuplicateIdsAndWrongGame()
    {
        var db = await DatabaseAsync(); var path = Path.Combine(root, "bad.json");
        var recipe = new CollectionRecipe(2, DateTimeOffset.UtcNow, [new("a", "a", true, 1), new("A", "b", true, 2)]);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(recipe, Json), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => new CollectionRecipeService(db).PreviewAsync(path, Token));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(recipe with { Mods = [], GameId = "other-game" }, Json), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => new CollectionRecipeService(db, GameProfile.MonsterHunterWorld(root)).PreviewAsync(path, Token));
    }
    [Fact]
    public async Task ProfilesStoreDeltasAndFollowParentEdits()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "a"); await SeedAsync(db, "b");
        var profiles = new ProfileRepository(db);
        var parent = await profiles.SaveAsync("Base", new Dictionary<string, (bool, int)> { ["a"] = (true, 1), ["b"] = (false, 2) }, ct: Token);
        var child = await profiles.SaveAsync("Child", new Dictionary<string, (bool, int)> { ["a"] = (true, 1), ["b"] = (true, 2) }, parent, Token);
        await using (var c = await db.OpenAsync(Token))
        {
            await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM profile_mods WHERE profile_id=$p"; cmd.Parameters.AddWithValue("$p", child);
            Assert.Equal(1L, await cmd.ExecuteScalarAsync(Token));
        }
        await profiles.SaveAsync("Base", new Dictionary<string, (bool, int)> { ["a"] = (true, 9), ["b"] = (false, 2) }, ct: Token);
        var resolved = await profiles.LoadAsync(child, Token);
        Assert.Equal((true, 9), resolved["a"]); Assert.Equal((true, 2), resolved["b"]);
        Assert.Equal(2, (await profiles.ListAsync(Token)).Single(p => p.Id == child).EnabledMods);
    }
    [Fact]
    public async Task ProfileCycleIsRejectedWithoutLosingPriorState()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "a"); var profiles = new ProfileRepository(db);
        var state = new Dictionary<string, (bool, int)> { ["a"] = (true, 1) };
        var a = await profiles.SaveAsync("a", state, ct: Token); var b = await profiles.SaveAsync("b", state, a, Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => profiles.SaveAsync("a", state, b, Token));
        Assert.Equal((true, 1), (await profiles.LoadAsync(a, Token))["a"]);
    }
    [Fact]
    public async Task RulesRejectCyclesAndInvalidExactProviders()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "a"); await SeedAsync(db, "b");
        var service = new RulesEditorService(db);
        await service.SaveAsync(new("ab", RuleKind.Overlay, RuleScope.ModPair, "a", "b", "b", null, "b overlays a", true, DateTimeOffset.UtcNow), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(new("ba", RuleKind.Overlay, RuleScope.ModPair, "b", "a", "a", null, "cycle", true, DateTimeOffset.UtcNow), Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(new("exact", RuleKind.ExactWinner, RuleScope.ExactPath, null, null, "b", @"nativePC\missing.tex", "bad", true, DateTimeOffset.UtcNow), Token));
        Assert.Single((await db.LoadPlannerSnapshotAsync(Token)).Rules);
    }
    [Fact]
    public async Task InspectorAndProfileDiffUseActualPlannerWinner()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "a", true); await SeedAsync(db, "b", true);
        await new RulesEditorService(db).SaveAsync(new("exact", RuleKind.ExactWinner, RuleScope.ExactPath, null, null, "a", @"nativePC\a.tex", "explicit choice", true, DateTimeOffset.UtcNow), Token);
        var snapshot = await db.LoadPlannerSnapshotAsync(Token); var planner = new DeploymentPlanner(new ConflictEngine());
        var asset = Assert.Single(WorkflowAnalysis.Explore(snapshot, planner));
        Assert.Equal("a", asset.WinnerId); Assert.Contains(asset.Evidence, e => e.Decisive && e.Source == "Manual rule");
        var diff = WorkflowAnalysis.Compare(snapshot, planner, new Dictionary<string, (bool, int)> { ["a"] = (true, 1) }, new Dictionary<string, (bool, int)> { ["b"] = (true, 2) });
        Assert.Single(diff.ProviderChanges); Assert.Equal(2, diff.Mods.Count);
    }
    [Fact]
    public async Task CrashMinimizerFindsCrossPartitionPairAndRejectsBadBaseline()
    {
        var engine = new CrashBisectorEngine();
        var result = await engine.RunAsync(["a", "b", "c", "d", "e", "f"], (set, _) => Task.FromResult(set.Contains("a") && set.Contains("f")), Token);
        Assert.True(result.Isolated); Assert.Equal(["a", "f"], result.Suspects);
        var baseline = await engine.RunAsync(["a"], (_, _) => Task.FromResult(true), Token); Assert.False(baseline.Isolated);
        var absent = await engine.RunAsync(["a"], (_, _) => Task.FromResult(false), Token); Assert.False(absent.Isolated);
    }
    [Fact]
    public async Task CrashProbeCancellationIsNotReportedAsAnIsolatedFailure()
    {
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CrashBisectorEngine().RunAsync(["a"], (_, _) => { cts.Cancel(); return Task.FromResult(false); }, cts.Token));
    }
    [Fact]
    public async Task FomodSelectsOnlyChosenPayloadAndConditionalFiles()
    {
        var package = Path.Combine(root, "package"); Directory.CreateDirectory(Path.Combine(package, "fomod"));
        await File.WriteAllTextAsync(Path.Combine(package, "a.tex"), "A", Token); await File.WriteAllTextAsync(Path.Combine(package, "b.tex"), "B", Token);
        await File.WriteAllTextAsync(Path.Combine(package, "fomod", "ModuleConfig.xml"), """
        <config><moduleName>Test</moduleName><installSteps><installStep name="Body"><optionalFileGroups><group name="Texture" type="SelectExactlyOne"><plugins>
        <plugin name="A"><files><file source="a.tex" destination="body.tex"/></files><conditionFlags><flag name="body">a</flag></conditionFlags><typeDescriptor><type name="Optional"/></typeDescriptor></plugin>
        <plugin name="B"><files><file source="b.tex" destination="body.tex"/></files><typeDescriptor><type name="Optional"/></typeDescriptor></plugin>
        </plugins></group></optionalFileGroups></installStep></installSteps><conditionalFileInstalls><patterns><pattern><dependencies><flagDependency flag="body" value="a"/></dependencies><files><file source="a.tex" destination="bonus.tex"/></files></pattern></patterns></conditionalFileInstalls></config>
        """, Token);
        var service = new FomodInstallerService(package); var game = GameProfile.MonsterHunterWorld(root);
        Assert.Throws<InvalidDataException>(() => service.Plan(new HashSet<string>(), game));
        Assert.Throws<InvalidDataException>(() => service.Plan(new HashSet<string> { "0/0/0", "0/0/1" }, game));
        var selected = new HashSet<string> { "0/0/0" }; var destination = Path.Combine(root, "installed");
        await service.InstallAsync(selected, game, destination, Token);
        Assert.Equal("A", await File.ReadAllTextAsync(Path.Combine(destination, "nativePC", "body.tex"), Token));
        Assert.Equal(2, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(64, service.Remember(selected).ConfigSha256.Length);
    }
    [Theory]
    [InlineData("<moduleDependencies><gameDependency version=\"1\"/></moduleDependencies>")]
    [InlineData("<requiredInstallFiles><file source=\"../outside.tex\" destination=\"a.tex\"/></requiredInstallFiles>")]
    [InlineData("<requiredInstallFiles><file source=\"a.tex\" destination=\"../../escape.tex\"/></requiredInstallFiles>")]
    public async Task FomodRejectsUnsupportedDependenciesAndTraversal(string content)
    {
        var package = Path.Combine(root, "unsafe"); Directory.CreateDirectory(Path.Combine(package, "fomod"));
        await File.WriteAllTextAsync(Path.Combine(package, "a.tex"), "A", Token);
        await File.WriteAllTextAsync(Path.Combine(package, "fomod", "ModuleConfig.xml"), "<config>" + content + "</config>", Token);
        Assert.Throws<InvalidDataException>(() => new FomodInstallerService(package).Plan(new HashSet<string>(), GameProfile.MonsterHunterWorld(root)));
    }
    [Fact]
    public async Task MigrationCommitAndUndoRestoreMetadataAndFilesTogether()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "old", true); await SeedAsync(db, "new");
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var blobs = new BlobStore(Path.Combine(root, "blobs"), db); var hash = new HashingService();
        foreach (var id in new[] { "old", "new" })
        {
            var source = Path.Combine(root, id + ".tex"); await File.WriteAllTextAsync(source, id, Token); var sha = await blobs.CaptureAsync(source, Token);
            await db.ReplaceModFilesAsync(id, [new(id, @"nativePC\a.tex", sha, null, id.Length, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        }
        await db.SetFamilyIdAsync("old", "family", Token);
        await new RulesEditorService(db).SaveAsync(new("pin", RuleKind.ExactWinner, RuleScope.ExactPath, null, null, "old", @"nativePC\a.tex", "pin", true, DateTimeOffset.UtcNow), Token);
        var planner = new DeploymentPlanner(new ConflictEngine()); var executor = new DeploymentExecutor(db, blobs, hash, game);
        Assert.True((await executor.ApplyAsync(planner.Build(await db.LoadPlannerSnapshotAsync(Token)), "initial", ct: Token)).Success);
        var migration = new UpdateMigrationService(db, planner, executor);
        var result = await migration.UpgradeAsync("old", "new", Token); Assert.True(result.Success, result.Exception?.ToString());
        var updated = await db.LoadPlannerSnapshotAsync(Token);
        Assert.True(updated.Mods.Single(m => m.Id == "old").IsSuperseded); Assert.Equal("family", updated.Mods.Single(m => m.Id == "new").FamilyId);
        Assert.Equal("new", Assert.Single(updated.Rules).WinnerModId);
        Assert.True((await executor.UndoLastAsync(Token)).Success);
        var restored = await db.LoadPlannerSnapshotAsync(Token);
        Assert.False(restored.Mods.Single(m => m.Id == "old").IsSuperseded); Assert.Null(restored.Mods.Single(m => m.Id == "new").FamilyId);
        Assert.Equal("old", Assert.Single(restored.Rules).WinnerModId);
        Assert.Equal("old", restored.CurrentManifest[@"nativePC\a.tex"].ProviderModId);
    }
    [Theory]
    [InlineData("after-file-write", false)]
    [InlineData("before-db-commit", false)]
    [InlineData("after-db-commit", true)]
    public async Task MigrationCrashRecoveryKeepsFilesAndMetadataOnSameSideOfCommit(string fault, bool committed)
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "old", true); await SeedAsync(db, "new");
        var game = Path.Combine(root, "crash-game"); Directory.CreateDirectory(game);
        var blobs = new BlobStore(Path.Combine(root, "crash-blobs"), db); var hashing = new HashingService();
        foreach (var id in new[] { "old", "new" })
        {
            var source = Path.Combine(root, id + ".bin"); await File.WriteAllTextAsync(source, id, Token);
            await db.ReplaceModFilesAsync(id, [new(id, @"nativePC\a.tex", await blobs.CaptureAsync(source, Token), null, id.Length, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        }
        var planner = new DeploymentPlanner(new ConflictEngine()); var normal = new DeploymentExecutor(db, blobs, hashing, game);
        Assert.True((await normal.ApplyAsync(planner.Build(await db.LoadPlannerSnapshotAsync(Token)), "baseline", ct: Token)).Success);
        var crashing = new DeploymentExecutor(db, blobs, hashing, game, (step, _) => { if (step == fault) throw new SimulatedCrashException("test crash"); });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => new UpdateMigrationService(db, planner, crashing).UpgradeAsync("old", "new", Token));
        await normal.RecoverIncompleteAsync(Token);
        var snapshot = await db.LoadPlannerSnapshotAsync(Token);
        Assert.Equal(committed, snapshot.Mods.Single(m => m.Id == "old").IsSuperseded);
        Assert.Equal(committed ? "new" : "old", snapshot.CurrentManifest[@"nativePC\a.tex"].ProviderModId);
        Assert.Equal(committed ? "new" : "old", await File.ReadAllTextAsync(normal.Destination(@"nativePC\a.tex"), Token));
    }

    [Fact]
    public async Task MigrationStaleMetadataRollsBackFilesWithoutOverwritingExternalEdit()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "old", true); await SeedAsync(db, "new");
        var game = Path.Combine(root, "stale-game"); Directory.CreateDirectory(game);
        var blobs = new BlobStore(Path.Combine(root, "stale-blobs"), db); var hashing = new HashingService();
        foreach (var id in new[] { "old", "new" })
        {
            var source = Path.Combine(root, id + ".bin"); await File.WriteAllTextAsync(source, id, Token);
            await db.ReplaceModFilesAsync(id, [new(id, @"nativePC\a.tex", await blobs.CaptureAsync(source, Token), null, id.Length, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        }
        await db.SetFamilyIdAsync("old", "original-family", Token);
        var planner = new DeploymentPlanner(new ConflictEngine()); var executor = new DeploymentExecutor(db, blobs, hashing, game);
        Assert.True((await executor.ApplyAsync(planner.Build(await db.LoadPlannerSnapshotAsync(Token)), "baseline", ct: Token)).Success);
        var preview = await new UpdateMigrationService(db, planner, executor).PreviewAsync("old", "new", Token);
        await db.SetFamilyIdAsync("new", "external-edit", Token);
        var result = await executor.ApplyWithMetadataAsync(preview.Plan, "stale migration", preview.Metadata, preview.Snapshot.Mods.ToDictionary(m => m.Id, m => (m.Enabled, m.Priority)), ct: Token);
        Assert.False(result.Success); Assert.True(result.RollbackCompleted);
        var snapshot = await db.LoadPlannerSnapshotAsync(Token);
        Assert.Equal("external-edit", snapshot.Mods.Single(m => m.Id == "new").FamilyId);
        Assert.False(snapshot.Mods.Single(m => m.Id == "old").IsSuperseded);
        Assert.Equal("old", await File.ReadAllTextAsync(executor.Destination(@"nativePC\a.tex"), Token));
    }

    [Fact]
    public async Task RecipeFamiliesRestoreOnlyAfterExplicitAction()
    {
        var source = await DatabaseAsync(); await SeedAsync(source, "a", true, "1"); await SeedAsync(source, "b", true, "2");
        await source.ChainManualFamilyAsync("a", ["a"], [["b"]], "Outfit", Token);
        var recipe = Path.Combine(root, "family.mhwrecipe"); await new CollectionRecipeService(source).ExportAsync(recipe, Token);
        var target = await DatabaseAsync(); await SeedAsync(target, "local-a", false, "1"); await SeedAsync(target, "local-b", false, "2");
        var importer = new CollectionRecipeService(target); await importer.ImportAsync(recipe, "family profile", Token);
        Assert.All(await target.GetModsAsync(Token), m => Assert.Null(m.FamilyId));
        Assert.Equal(1, await importer.RestoreFamiliesAsync(recipe, Token));
        var mods = await target.GetModsAsync(Token); Assert.Single(mods.Select(m => m.FamilyId).Distinct());
        Assert.Equal("Optional", mods.Single(m => m.Id == "local-b").FamilyRole);
        Assert.All(mods, m => Assert.False(m.Enabled));
    }

    [Fact]
    public async Task IncompatibilityBlocksDisjointPackages()
    {
        var db = await DatabaseAsync(); await SeedAsync(db, "a", true); await SeedAsync(db, "b", true);
        await db.ReplaceModFilesAsync("b", [new("b", @"nativePC\other.tex", new string('b', 64), null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], Token);
        await new RulesEditorService(db).SaveAsync(new("no", RuleKind.Incompatible, RuleScope.ModPair, "a", "b", null, null, "Known interaction", true, DateTimeOffset.UtcNow), Token);
        var plan = new DeploymentPlanner(new ConflictEngine()).Build(await db.LoadPlannerSnapshotAsync(Token));
        Assert.True(plan.IsBlocked); Assert.Empty(plan.Changes); Assert.Contains(plan.Conflicts, d => d.Blocking && d.RuleId == "no");
    }

    [Fact]
    public void AdapterRegistryKeepsUnknownGamesConservative()
    {
        var unknown = GameProfile.Generic("test", "Test", root, "game.exe", "Mods", adapterId: "unregistered");
        Assert.False(GameAdapters.Resolve(unknown).SupportsMhwConflictSemantics);
        Assert.True(GameAdapters.Resolve(GameProfile.MonsterHunterWorld(root)).SupportsMhwConflictSemantics);
        Assert.Throws<InvalidOperationException>(() => GameAdapters.Register(new MonsterHunterWorldAdapter()));
    }

}
