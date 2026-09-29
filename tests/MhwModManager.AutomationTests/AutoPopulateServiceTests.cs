using System.Text.Json;
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
            JsonSerializer.Serialize(new
            {
                mods = new[] { "base" },
                textures = new[] { @"nativePC\shared\skin.tex" }
            }),
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
