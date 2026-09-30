using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class AutoPopulateServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MhwAutoPopulateTests-" + Guid.NewGuid().ToString("N"));

    public AutoPopulateServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task AutoPopulateEnablesRequiredModAndTextureProviderAndSkipsConflictingAlternative()
    {
        var db = await CreateDbAsync("autopopulate.db");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "base", "Required Base", 800);
        var addon = await AddModAsync(db, "addon", "Armor Addon", 1000);
        await AddModAsync(db, "texture", "Correct Body Texture", 900);
        await AddModAsync(db, "conflict", "Conflicting Armor", 100);

        await File.WriteAllTextAsync(
            Path.Combine(addon.SourcePath, "mod-manager.requirements.json"),
            """{"mods":["base"],"textures":["nativePC\\shared\\skin.tex"]}""",
            TestContext.Current.CancellationToken);

        await db.ReplaceModFilesAsync("base",
            [ModFile("base", @"nativePC\armor\base.mod3", "base", FileClass.Structural)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("addon",
            [ModFile("addon", @"nativePC\armor\body.bin", "addon", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("texture",
            [ModFile("texture", @"nativePC\shared\skin.tex", "skin", FileClass.Texture)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("conflict",
            [ModFile("conflict", @"nativePC\armor\body.bin", "other", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ExecuteAsync(
            """
            INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
            VALUES('auto-populate-hard-conflict','Incompatible','ModPair','addon','conflict',NULL,NULL,'Auto Populate regression hard incompatibility',1,$t)
            """,
            new Dictionary<string,object?>
            {
                ["$t"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            },
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.True(result.State["addon"].Enabled);
        Assert.True(result.State["base"].Enabled);
        Assert.True(result.State["texture"].Enabled);
        Assert.False(result.State["conflict"].Enabled);
        Assert.True(result.SkippedConflicts >= 1);

        var status = await dependencies.ScanStageAsync(
            result.State.Where(x => x.Value.Enabled).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(status, x => !x.Ready);

        var snapshot = await snapshots.LoadAsync(TestContext.Current.CancellationToken);
        var staged = snapshot.Mods.Select(m => m with { Enabled = result.State[m.Id].Enabled }).ToArray();
        Assert.False(planner.Build(snapshot with { Mods = staged }).IsBlocked);
    }

    [Fact]
    public async Task AutoPopulateDoesNotEnableIndependentTextureReplacersTogether()
    {
        var db = await CreateDbAsync("texture-conflict.db");
        var gameRoot = Path.Combine(root, "game-texture");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "first", "Azure Skin", 500);
        await AddModAsync(db, "second", "Crimson Skin", 400);
        var path = @"nativePC\pl\f_equip\mod_shared\skin.tex";
        await db.ReplaceModFilesAsync("first", [ModFile("first", path, "first-hash", FileClass.Texture)], TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("second", [ModFile("second", path, "second-hash", FileClass.Texture)], TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.State.Count(x => x.Value.Enabled));
        Assert.True(result.State["first"].Enabled);
        Assert.False(result.State["second"].Enabled);
        Assert.Equal(1, result.SkippedConflicts);
    }

    [Fact]
    public async Task AutoPopulateSkipsModWithMissingRequiredPackage()
    {
        var db = await CreateDbAsync("missing-requirement.db");
        var gameRoot = Path.Combine(root, "game-missing");
        Directory.CreateDirectory(gameRoot);

        var addon = await AddModAsync(db, "addon-only", "Addon Without Base", 100);
        await File.WriteAllTextAsync(
            Path.Combine(addon.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":["not-installed"]}""",
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("addon-only",
            [ModFile("addon-only", @"nativePC\armor\addon.mod3", "addon", FileClass.Structural)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.False(result.State["addon-only"].Enabled);
        Assert.Equal(1, result.SkippedRequirements);
        Assert.Contains(result.Decisions, x => x.ModId == "addon-only" && !x.Enabled && x.Reason.Contains("not-installed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AutoPopulateFillsAroundExplicitStagedPreference()
    {
        var db = await CreateDbAsync("staged-preference.db");
        var gameRoot = Path.Combine(root, "game-staged-preference");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "preferred", "My Preferred Texture", 100);
        await AddModAsync(db, "higher-priority", "Higher Priority Alternative", 1000);
        var path = @"nativePC\pl\f_equip\preferred\skin.tex";
        await db.ReplaceModFilesAsync(
            "preferred",
            [ModFile("preferred", path, "preferred-hash", FileClass.Texture)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync(
            "higher-priority",
            [ModFile("higher-priority", path, "alternative-hash", FileClass.Texture)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(["preferred"], TestContext.Current.CancellationToken);

        Assert.True(result.State["preferred"].Enabled);
        Assert.False(result.State["higher-priority"].Enabled);
        Assert.Contains(
            result.Decisions,
            x => x.ModId == "preferred" &&
                 x.Enabled &&
                 x.Reason.Contains("protected preference", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("preserved 1 selected package", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoPopulateNeverSilentlyDropsUnsafeStagedPreference()
    {
        var db = await CreateDbAsync("unsafe-staged-preference.db");
        var gameRoot = Path.Combine(root, "game-unsafe-staged-preference");
        Directory.CreateDirectory(gameRoot);

        var preferred = await AddModAsync(db, "preferred-addon", "Preferred Addon", 100);
        await File.WriteAllTextAsync(
            Path.Combine(preferred.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":["missing-base"]}""",
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync(
            "preferred-addon",
            [ModFile("preferred-addon", @"nativePC\armor\preferred.mod3", "preferred", FileClass.Structural)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var service = new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BuildAsync(["preferred-addon"], TestContext.Current.CancellationToken));

        Assert.Contains("selected mod 'Preferred Addon'", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left unchanged", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StagedDependencyDoesNotCountManagedLiveFileThatWillBeRemoved()
    {
        var db = await CreateDbAsync("managed-live-dependency.db");
        var gameRoot = Path.Combine(root, "game-managed-live");
        var liveDirectory = Path.Combine(gameRoot, "nativePC", "shared");
        Directory.CreateDirectory(liveDirectory);
        await File.WriteAllTextAsync(Path.Combine(liveDirectory, "required.bin"), "currently-live", TestContext.Current.CancellationToken);

        await AddModAsync(db, "provider", "Current Provider", 100);
        var dependent = await AddModAsync(db, "dependent", "Dependent", 200);
        await db.ReplaceModFilesAsync("provider",
            [ModFile("provider", @"nativePC\shared\required.bin", "provider-hash", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("dependent",
            [ModFile("dependent", @"nativePC\dependent\body.mod3", "dependent-hash", FileClass.Structural)],
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(dependent.SourcePath, "mod-manager.requirements.json"),
            """{"files":["nativePC\\shared\\required.bin"]}""",
            TestContext.Current.CancellationToken);

        await db.ExecuteAsync(
            """
            INSERT INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at)
            VALUES($p,$m,$b,$e,NULL,$t)
            """,
            new Dictionary<string,object?>
            {
                ["$p"] = @"nativePC\shared\required.bin",
                ["$m"] = "provider",
                ["$b"] = "provider-hash",
                ["$e"] = "provider-hash",
                ["$t"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            },
            TestContext.Current.CancellationToken);

        var statuses = await new DependencyDoctorService(db, gameRoot, GameProfile.MonsterHunterWorld(gameRoot))
            .ScanStageAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dependent" }, TestContext.Current.CancellationToken);

        var failed = Assert.Single(statuses.Where(x => !x.Ready));
        Assert.Equal("dependent", failed.ModId);
        Assert.Contains(failed.Missing, x => x.Contains("managed provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StagedDependencyRejectsMissingOrSupersededIdentityInsteadOfDroppingIt()
    {
        var db = await CreateDbAsync("stale-stage.db");
        var gameRoot = Path.Combine(root, "game-stale-stage");
        Directory.CreateDirectory(gameRoot);

        var statuses = await new DependencyDoctorService(db, gameRoot, GameProfile.MonsterHunterWorld(gameRoot))
            .ScanStageAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "no-longer-installed" }, TestContext.Current.CancellationToken);

        var failed = Assert.Single(statuses);
        Assert.False(failed.Ready);
        Assert.Equal("no-longer-installed", failed.ModId);
    }

    private async Task<ManagerDatabase> CreateDbAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name));
        await db.InitializeAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private async Task<ModDescriptor> AddModAsync(ManagerDatabase db, string id, string name, int priority)
    {
        var source = Path.Combine(root, id);
        Directory.CreateDirectory(source);
        var mod = new ModDescriptor(id, name, name, source, false, priority);
        await db.UpsertModAsync(mod, TestContext.Current.CancellationToken);
        return mod;
    }

    private static ModFileDescriptor ModFile(string mod, string path, string sha, FileClass fileClass) =>
        new(mod, path, sha, null, 1, DateTimeOffset.UtcNow, fileClass);

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
