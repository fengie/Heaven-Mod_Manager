using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class AutoPopulateServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MhwAutoPopulateTests-" + Guid.NewGuid().ToString("N"));

    public AutoPopulateServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task KnownInternalOverlapIsAdvisoryWhileBadMountAndExactDuplicateAreHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = await CreateDbAsync("exports.db");
        var game = GameProfile.Generic("unreal", "Unreal", Path.Combine(root,"game"), "game.exe", "Project/Content/Paks", "unreal-paks");
        var store = new BlobStore(Path.Combine(root,"blobs"),db);
        var scanner = new ModScanner(db,store,new HashingService(),game);
        foreach (var id in new[] { "anchor", "exact-copy", "shared", "unknown" })
        {
            var mod = await AddModAsync(db,id,id,9);
            var stem = id == "exact-copy" ? "anchor" : id;
            await File.WriteAllTextAsync(Path.Combine(mod.SourcePath,stem+".pak"),stem,ct);
            await File.WriteAllTextAsync(Path.Combine(mod.SourcePath,stem+".ucas"),stem,ct);
            var toc = new byte[168];
            "-==--==--==--==-"u8.CopyTo(toc);
            toc[16]=2; toc[20]=144; toc[24]=2; toc[144]=77; toc[155]=2; toc[167]=10;
            if (id == "unknown") toc[24]=255;
            await File.WriteAllBytesAsync(Path.Combine(mod.SourcePath,stem+".utoc"),toc,ct);
        }
        await AddModAsync(db,"config","Wrong config",200);
        await db.ReplaceModFilesAsync("config",[ModFile("config",@"root\Project\Content\Paks\Engine.ini","hash",FileClass.GameData)],ct);
        var service = new AutoPopulateService(new PlannerSnapshotRepository(db),new DeploymentPlanner(new ConflictEngine(game),game),
            new DependencyDoctorService(db,game.GameRoot,game),game.GameRoot,game,store,scanner);
        var result = await service.BuildAsync(["anchor"],ct);
        Assert.Equal(3,result.EnabledMods);
        Assert.False(result.State["config"].Enabled);
        Assert.False(result.State["exact-copy"].Enabled);
        Assert.Contains("Exact installed copy", result.Decisions.Single(decision => decision.ModId == "exact-copy").Reason);
        Assert.Contains("Shares an Unreal export ID", result.Decisions.Single(decision => decision.ModId == "shared").Reason);
        Assert.Contains("coverage is unavailable", result.Decisions.Single(decision => decision.ModId == "unknown").Reason);
        Assert.Contains("2 selected package(s) share known",result.Summary);
        Assert.Collection(result.SharedExportIds.Order(StringComparer.Ordinal),id=>Assert.Equal("anchor",id),id=>Assert.Equal("shared",id));
    }

    [Theory]
    [InlineData("unreal-paks", "Test/Content/Paks", ".pak")]
    [InlineData("generic-files", "Mods", ".bin")]
    public async Task ExistingUnindexedPackagesRetainSourceAndExplicitChoice(string adapter, string mount, string extension)
    {
        var ct = TestContext.Current.CancellationToken;
        var db = await CreateDbAsync("catalog.db");
        var game = GameProfile.Generic("fixture", "Fixture", Path.Combine(root, "game"), "game.exe", mount, adapter);
        var shelf = await AddModAsync(db, "shelf", "Shelf", 100);
        foreach (var id in new[] { "keep", "choice-a", "choice-b", "manual-off", "archive", "partial" })
        {
            var source = Directory.CreateDirectory(Path.Combine(shelf.SourcePath,id)).FullName;
            await db.UpsertModAsync(new(id,id,id,source,false,10), ct);
            await File.WriteAllTextAsync(Path.Combine(source,id + (id == "archive" ? ".zip" :
                id == "partial" && adapter == "unreal-paks" ? ".ucas" : extension)),id,ct);
        }
        await db.ExecuteAsync("""
            INSERT INTO mod_families VALUES('family','Family','2026-11-20');
            INSERT INTO mod_family_members VALUES('family','choice-a','option','body'),('family','choice-b','option','body');
            INSERT INTO family_preferences VALUES('family','body','choice-b','2026-11-20');
            """, ct: ct);
        var store = new BlobStore(Path.Combine(root, "blobs"),db);
        var service = new AutoPopulateService(new PlannerSnapshotRepository(db),new DeploymentPlanner(new ConflictEngine(game),game),
            new DependencyDoctorService(db,game.GameRoot,game),game.GameRoot,game,store,new ModScanner(db,store,new HashingService(),game));
        for (var pass = 0; pass < 2; pass++)
        {
            var result = await service.BuildAsync([], ["manual-off"],ct);
            Assert.True(result.State["keep"].Enabled);
            Assert.True(result.State["choice-b"].Enabled);
            Assert.False(result.State["choice-a"].Enabled);
            Assert.False(result.State["manual-off"].Enabled);
            Assert.False(result.State["shelf"].Enabled);
            Assert.False(result.State["archive"].Enabled);
            Assert.Equal(adapter != "unreal-paks", result.State["partial"].Enabled);
            Assert.All(await db.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
            Assert.DoesNotContain(await db.GetModFilesAsync(ct), file => file.ModId == "shelf" || file.ModId == "archive");
        }
        Assert.False(Directory.Exists(game.GameRoot));
    }

    [Theory]
    [InlineData("4D524C000B000000A9BCBB5900000000", true, false)]
    [InlineData("4D524C000B000000A9BCBB5900000000", false, true)]
    [InlineData("4D524C000C0000005060845D00000000", true, true)]
    [InlineData("4D524C000C0000002A66075D00000000", true, true)]
    [InlineData("4D524C000B0000000000000000000000", true, true)]
    public async Task AutoPopulateChecksExactMaterialGeneration(string header, bool iceborne, bool eligible)
    {
        var ct = TestContext.Current.CancellationToken;
        var db = await CreateDbAsync("materials.db");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "chunk"));
        if (iceborne)
            await File.WriteAllBytesAsync(Path.Combine(gameRoot, "chunk", "chunkG0.bin"), [], ct);
        var mod = await AddModAsync(db, "armor", "Fixture armor", 100);
        var material = Path.Combine(root, "fixture.mrl3");
        await File.WriteAllBytesAsync(material, Convert.FromHexString(header), ct);
        var store = new MhwModManager.Filesystem.BlobStore(Path.Combine(root, "blobs"), db);
        var hash = await store.CaptureAsync(material, ct);
        await db.ReplaceModFilesAsync(mod.Id,
            [ModFile(mod.Id, @"nativePC\pl\f_equip\pl032_0010\body\fixture.mrl3", hash, FileClass.Structural)], ct);
        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var service = new AutoPopulateService(new PlannerSnapshotRepository(db),
            new DeploymentPlanner(new ConflictEngine(game), game), new DependencyDoctorService(db, gameRoot, game),
            gameRoot, game, store);
        var result = await service.BuildAsync(ct);
        Assert.Equal(eligible, result.State[mod.Id].Enabled);
        if (!eligible)
        {
            Assert.Contains("Iceborne", Assert.Single(result.Decisions).Reason);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildAsync([mod.Id], ct));
            Assert.Contains("Fixture armor", error.Message);
        }
    }

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

        var failed = Assert.Single(statuses, x => !x.Ready);
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

    [Fact]
    public async Task AutoPopulateRejectsIncompatibleDependencyVersion()
    {
        var db = await CreateDbAsync("version-requirement.db");
        var gameRoot = Path.Combine(root, "game-version");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "base", "Old Base", 50, nexusVersion: "1.5.0");
        var addon = await AddModAsync(db, "addon", "Needs New Base", 100);
        await File.WriteAllTextAsync(
            Path.Combine(addon.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":[{"id":"base","minVersion":"2.0.0"}]}""",
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("base",
            [ModFile("base", @"nativePC\version\base.bin", "base-v1", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("addon",
            [ModFile("addon", @"nativePC\version\addon.bin", "addon", FileClass.GameData)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.False(result.State["addon"].Enabled);
        Assert.True(result.State["base"].Enabled);
        Assert.Contains(result.Decisions, x => x.ModId == "addon" && x.Reason.Contains("2.0.0", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public async Task PrereleaseDoesNotSatisfyReleaseMinimumByGuessing()
    {
        var db = await CreateDbAsync("prerelease-requirement.db");
        var gameRoot = Path.Combine(root, "game-prerelease");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "base", "Prerelease Base", 50, nexusVersion: "2.0.0-beta");
        var addon = await AddModAsync(db, "addon", "Needs Stable Base", 100);
        await File.WriteAllTextAsync(
            Path.Combine(addon.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":[{"id":"base","minVersion":"2.0.0"}]}""",
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("base",
            [ModFile("base", @"nativePC\version\pre-base.bin", "pre", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("addon",
            [ModFile("addon", @"nativePC\version\stable-addon.bin", "addon", FileClass.GameData)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.False(result.State["addon"].Enabled);
        Assert.Contains(result.Decisions, x => x.ModId == "addon" && x.Reason.Contains("2.0.0", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public async Task OptionalDependencyDoesNotBlockWhenAbsent()
    {
        var db = await CreateDbAsync("optional-requirement.db");
        var gameRoot = Path.Combine(root, "game-optional");
        Directory.CreateDirectory(gameRoot);

        var addon = await AddModAsync(db, "addon", "Standalone With Optional Integration", 100);
        await File.WriteAllTextAsync(
            Path.Combine(addon.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":[{"id":"optional-integration","required":false,"minVersion":"1.0"}]}""",
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("addon",
            [ModFile("addon", @"nativePC\optional\addon.mod3", "addon", FileClass.Structural)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.True(result.State["addon"].Enabled);
        Assert.Equal(0, result.SkippedRequirements);
        var status = await dependencies.ScanStageAsync(
            new HashSet<string>(["addon"], StringComparer.OrdinalIgnoreCase),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(status, x => !x.Ready);
        Assert.Contains(status.SelectMany(x => x.Evidence), x => x.Contains("Optional dependency not active", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public async Task HardDependencyCycleIsDetectedAndValidatedAsAGroup()
    {
        var db = await CreateDbAsync("dependency-cycle.db");
        var gameRoot = Path.Combine(root, "game-cycle");
        Directory.CreateDirectory(gameRoot);

        var a = await AddModAsync(db, "a", "Cycle A", 100);
        var b = await AddModAsync(db, "b", "Cycle B", 90);
        await File.WriteAllTextAsync(Path.Combine(a.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":["b"]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(b.SourcePath, "mod-manager.requirements.json"),
            """{"dependencies":["a"]}""", TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("a",
            [ModFile("a", @"nativePC\cycle\a.bin", "a", FileClass.Other)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("b",
            [ModFile("b", @"nativePC\cycle\b.bin", "b", FileClass.Other)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.True(result.State["a"].Enabled);
        Assert.True(result.State["b"].Enabled);
        var enabled = result.State.Where(x => x.Value.Enabled).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshot = await snapshots.LoadAsync(TestContext.Current.CancellationToken);
        var staged = snapshot.Mods.Select(m => m with { Enabled = enabled.Contains(m.Id) }).ToArray();
        var plan = planner.Build(snapshot with { Mods = staged });
        var status = await dependencies.ScanStageAsync(enabled, plan, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(status, x => !x.Ready);
        Assert.Contains(status.SelectMany(x => x.Evidence), x => x.Contains("strongly connected group", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public async Task PluginIsRejectedWhenTrackedNativeLoaderIsOnlyPartial()
    {
        var db = await CreateDbAsync("partial-loader.db");
        var gameRoot = Path.Combine(root, "game-loader");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "plugin", "Native Plugin", 100);
        await AddModAsync(db, "partial-loader", "Incomplete Loader", 90);
        await db.ReplaceModFilesAsync("plugin",
            [ModFile("plugin", @"nativePC\plugins\feature.dll", "plugin", FileClass.Plugin)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("partial-loader",
            [ModFile("partial-loader", @"root\dinput8.dll", "proxy", FileClass.Plugin)],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.False(result.State["plugin"].Enabled);
        Assert.False(result.State["partial-loader"].Enabled);
        Assert.Contains(result.Decisions, x => x.ModId == "plugin" && x.Reason.Contains("loader", StringComparison.OrdinalIgnoreCase));
    }



    [Fact]
    public async Task PlanAwareDependencyRejectsMixedEffectiveLoaderProviders()
    {
        var db = await CreateDbAsync("mixed-effective-loader.db");
        var gameRoot = Path.Combine(root, "game-mixed-effective-loader");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "plugin", "Native Plugin", 300);
        await AddModAsync(db, "loader-a", "Loader A", 200);
        await AddModAsync(db, "loader-b", "Loader B", 100);
        await db.ReplaceModFilesAsync("plugin",
            [ModFile("plugin", @"nativePC\plugins\feature.dll", "plugin", FileClass.Plugin)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("loader-a",
            [
                ModFile("loader-a", @"root\dinput8.dll", "a-proxy", FileClass.Plugin),
                ModFile("loader-a", @"root\loader.dll", "a-core", FileClass.Plugin)
            ],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("loader-b",
            [
                ModFile("loader-b", @"root\dinput8.dll", "b-proxy", FileClass.Plugin),
                ModFile("loader-b", @"root\loader.dll", "b-core", FileClass.Plugin)
            ],
            TestContext.Current.CancellationToken);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var enabled = new HashSet<string>(["plugin", "loader-a", "loader-b"], StringComparer.OrdinalIgnoreCase);
        var snapshot = await snapshots.LoadAsync(TestContext.Current.CancellationToken);
        var staged = snapshot.Mods.Select(mod => mod with { Enabled = enabled.Contains(mod.Id) }).ToArray();
        var exactWinners = new Dictionary<string,string>(PathRules.Comparer)
        {
            [@"root\dinput8.dll"] = "loader-a",
            [@"root\loader.dll"] = "loader-b"
        };
        var plan = planner.Build(snapshot with { Mods = staged, ExactWinners = exactWinners });

        Assert.False(plan.IsBlocked);
        var status = await dependencies.ScanStageAsync(enabled, plan, TestContext.Current.CancellationToken);

        var plugin = Assert.Single(status, x => x.ModId == "plugin");
        Assert.False(plugin.Ready);
        Assert.Contains(plugin.Missing, x => x.Contains("mix bootstrap binaries", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(256)]
    public async Task SparseLibrariesUseOneFullPlannerPassRegardlessOfLibrarySize(int modCount)
    {
        var db = await CreateDbAsync($"sparse-perf-{modCount}.db");
        var gameRoot = Path.Combine(root, $"game-sparse-perf-{modCount}");
        Directory.CreateDirectory(gameRoot);

        for (var i = 0; i < modCount; i++)
        {
            var id = $"isolated-{i:D4}";
            await AddModAsync(db, id, $"Isolated {i:D4}", modCount - i);
            await db.ReplaceModFilesAsync(
                id,
                [ModFile(id, $@"nativePC\perf\{id}\payload.bin", $"hash-{i:D4}", FileClass.GameData)],
                TestContext.Current.CancellationToken);
        }

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);

        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(modCount, result.EnabledMods);
        Assert.Equal(modCount, result.PlannerFastPathCandidates);
        Assert.Equal(1, result.PlannerPasses);
        Assert.True(result.PlannerMilliseconds >= 0);
        Assert.Contains("used 1 full planner pass", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisjointExplicitIncompatibilityStillFallsBackToPlannerAndSkipsCandidate()
    {
        var db = await CreateDbAsync("disjoint-rule-perf.db");
        var gameRoot = Path.Combine(root, "game-disjoint-rule-perf");
        Directory.CreateDirectory(gameRoot);

        await AddModAsync(db, "preferred", "Preferred", 100);
        await AddModAsync(db, "incompatible", "Incompatible", 90);
        await db.ReplaceModFilesAsync(
            "preferred",
            [ModFile("preferred", @"nativePC\perf\preferred.bin", "preferred", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync(
            "incompatible",
            [ModFile("incompatible", @"nativePC\perf\incompatible.bin", "incompatible", FileClass.GameData)],
            TestContext.Current.CancellationToken);
        await db.ExecuteAsync(
            """
            INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
            VALUES('disjoint-incompatible','Incompatible','ModPair','preferred','incompatible',NULL,NULL,'Explicit incompatibility',1,$t)
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
            .BuildAsync(["preferred"], TestContext.Current.CancellationToken);

        Assert.True(result.State["preferred"].Enabled);
        Assert.False(result.State["incompatible"].Enabled);
        Assert.Equal(0, result.PlannerFastPathCandidates);
        Assert.True(result.PlannerPasses >= 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AutoPopulateSelectsEligibleTokenProviderAroundProtectedDependent(bool explicitlyExcluded)
    {
        var ct = TestContext.Current.CancellationToken;
        var db = await CreateDbAsync("eligible-dependency.db");
        var gameRoot = Path.Combine(root, "game-eligible");
        Directory.CreateDirectory(gameRoot);
        var high = await AddModAsync(db, "high", "Higher-ranked dependency", 1000);
        var eligible = await AddModAsync(db, "eligible", "Eligible dependency", 100);
        var dependent = await AddModAsync(db, "dependent", "Protected dependent", 200);
        await db.UpsertModAsync(high with { NexusModId = "12345" }, ct);
        await db.UpsertModAsync(eligible with { NexusModId = "12345" }, ct);
        await db.ReplaceModFilesAsync("high",
            [ModFile("high", @"nativePC\dependency\shared.bin", "high-hash", FileClass.GameData)], ct);
        await db.ReplaceModFilesAsync("eligible",
            [ModFile("eligible", @"nativePC\dependency\shared.bin", "eligible-hash", FileClass.GameData)], ct);
        await db.ReplaceModFilesAsync("dependent",
            [ModFile("dependent", @"nativePC\dependent\payload.bin", "dependent-hash", FileClass.GameData)], ct);
        await File.WriteAllTextAsync(Path.Combine(dependent.SourcePath, "mod-manager.requirements.json"),
            """{"mods":["nexus:12345"]}""", ct);
        if (!explicitlyExcluded)
            await File.WriteAllTextAsync(Path.Combine(high.SourcePath, "mod-manager.requirements.json"), "[]", ct);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var service = new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game);
        IReadOnlyCollection<string> exclusions = explicitlyExcluded ? ["high"] : [];

        var result = await service.BuildAsync(["dependent"], exclusions, ct);

        Assert.True(result.State["dependent"].Enabled);
        Assert.True(result.State["eligible"].Enabled);
        Assert.False(result.State["high"].Enabled);
        Assert.All(await db.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
    }

    [Theory]
    [InlineData("array")]
    [InlineData("scalar")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public async Task MalformedRequirementSidecarDoesNotAbortUnrelatedSelection(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var db = await CreateDbAsync("sidecar-admission.db");
        var gameRoot = Path.Combine(root, "game-sidecar");
        Directory.CreateDirectory(gameRoot);
        var bad = await AddModAsync(db, "bad", "Invalid requirements", 200);
        await AddModAsync(db, "good", "Independent mod", 100);
        await db.ReplaceModFilesAsync("bad",
            [ModFile("bad", @"nativePC\bad\payload.bin", "bad-hash", FileClass.GameData)], ct);
        await db.ReplaceModFilesAsync("good",
            [ModFile("good", @"nativePC\good\payload.bin", "good-hash", FileClass.GameData)], ct);
        var sidecar = kind switch
        {
            "array" => "[]",
            "scalar" => "42",
            "duplicate" => """{"mods":["missing"],"mods":[]}""",
            "oversized" => """{"mods":[]}""" + new string(' ', 256 * 1024),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await File.WriteAllTextAsync(Path.Combine(bad.SourcePath, "mod-manager.requirements.json"),
            sidecar, ct);

        var game = GameProfile.MonsterHunterWorld(gameRoot);
        var snapshots = new PlannerSnapshotRepository(db);
        var planner = new DeploymentPlanner(new ConflictEngine(game), game);
        var dependencies = new DependencyDoctorService(db, gameRoot, game);
        var result = await new AutoPopulateService(snapshots, planner, dependencies, gameRoot, game)
            .BuildAsync(ct);

        Assert.True(result.State["good"].Enabled);
        Assert.False(result.State["bad"].Enabled);
        Assert.Contains(result.Decisions, decision => decision.ModId == "bad" &&
            !decision.Enabled && decision.Reason.Contains("requirements sidecar", StringComparison.OrdinalIgnoreCase));
        Assert.All(await db.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
    }

    private async Task<ManagerDatabase> CreateDbAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name));
        await db.InitializeAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private async Task<ModDescriptor> AddModAsync(
        ManagerDatabase db,
        string id,
        string name,
        int priority,
        string? nexusVersion = null)
    {
        var source = Path.Combine(root, id);
        Directory.CreateDirectory(source);
        var mod = new ModDescriptor(id, name, name, source, false, priority, NexusVersion: nexusVersion);
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
