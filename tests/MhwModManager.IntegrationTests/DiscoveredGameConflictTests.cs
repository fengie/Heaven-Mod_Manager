using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DiscoveredGameConflictTests
{
    private static readonly string[] ExpectedSelectedParts = ["addon", "base", "red", "wide"];

    public static TheoryData<string> RegisteredGames => new(DiscoveredGameProfiles.Load().Select(p => p.Id));

    [Theory]
    [MemberData(nameof(RegisteredGames))]
    public void SavedRelationshipsKeepTheirOwnPartsWhenIdentifiersShareDisplayTokens(string id)
    {
        var game = DiscoveredGameProfiles.Load().Single(profile => profile.Id == id);
        var familyIds = new[] { "manual:外观", "manual:相机", "manual:camera/fixed", "manual:camera.fixed" };
        var mods = familyIds.SelectMany((family, index) => new[]
        {
            new ModDescriptor($"base-{index}", "Main", "Main", $"source/{index}/base", false, 1,
                FamilyId: family, FamilyRole: "Main"),
            new ModDescriptor($"extra-{index}", "Optional", "Optional", $"source/{index}/extra", false, 2,
                FamilyId: family, FamilyRole: "Optional")
        }).ToArray();

        var families = LogicalModFamilies.Build(mods, game: game);
        Assert.Equal(familyIds.Length, families.Count);
        foreach (var familyId in familyIds)
        {
            var expected = mods.Where(mod => mod.FamilyId == familyId).Select(mod => mod.Id).Order();
            var family = Assert.Single(families, row => row.Members.Any(mod => mod.FamilyId == familyId));
            Assert.Equal(expected, family.Members.Select(mod => mod.Id).Order());
        }
    }

    [Theory]
    [MemberData(nameof(RegisteredGames))]
    public async Task StoredChoiceGroupsStayIndependentFromOptionalPartsAndFromEachOther(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var game = DiscoveredGameProfiles.Load().Single(profile => profile.Id == id);
        var root = Directory.CreateTempSubdirectory("manager-family-choices-");
        var db = new ManagerDatabase(Path.Combine(root.FullName, "manager.db"));
        try
        {
            await db.InitializeAsync(ct);
            string[] ids = ["base", "addon", "blue", "red", "wide", "narrow"];
            foreach (var key in ids)
                await db.UpsertModAsync(new(key, key, key, Path.Combine(root.FullName, key), false, 1,
                    FamilyId: "fixture"), ct);

            // Seed the same saved family tables used by existing libraries, including two separate choice groups.
            await using (var connection = await db.OpenAsync(ct))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO mod_families VALUES ('fixture','Field equipment',CURRENT_TIMESTAMP);
                    INSERT INTO mod_family_members(family_id,mod_id,role,choice_group) VALUES
                    ('fixture','base','Main',NULL),('fixture','addon','Optional',NULL),
                    ('fixture','blue','Optional','color'),('fixture','red','Optional','color'),
                    ('fixture','wide','Optional','camera'),('fixture','narrow','Optional','camera');
                    """;
                await command.ExecuteNonQueryAsync(ct);
            }

            var snapshot = await new PlannerSnapshotRepository(db).LoadAsync(ct);
            var family = Assert.Single(LogicalModFamilies.Build(snapshot.Mods, game: game));
            var row = new ModRowViewModel(family, () => { });
            row.StagedEnabled = true;
            Assert.Null(row.StagedEnabled); // Additive parts enabled, both explicit choices still open.
            Assert.All(row.ExpandStage().Where(part => part.Key is not ("base" or "addon")),
                part => Assert.False(part.Value.enabled));
            row.StagedEnabled = false;
            row.SetMemberEnabled("addon", true);
            row.SetMemberEnabled("blue", true);
            row.SetMemberEnabled("wide", true);
            row.SetMemberEnabled("red", true);
            var active = row.ExpandStage().Where(part => part.Value.enabled).Select(part => part.Key).Order();
            Assert.Equal(ExpectedSelectedParts, active);
            Assert.Equal("Field equipment", family.DisplayName);

            var planner = new DeploymentPlanner(new ConflictEngine(game), game);
            var valid = WorkflowAnalysis.WithState(snapshot, row.ExpandStage());
            Assert.False(planner.Build(valid).IsBlocked);
            var invalid = valid with
            {
                Mods = valid.Mods.Select(mod => mod.Id == "blue" ? mod with { Enabled = true } : mod).ToArray()
            };
            Assert.True(planner.Build(invalid).IsBlocked);
            Assert.All(await db.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
        }
        finally
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(RegisteredGames))]
    public async Task ResolvingAConflictThenEnablingAFamilyAddonChangesOnlyItsDeployedWinner(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("manager-game-conflict-");
        var db = new ManagerDatabase(Path.Combine(root.FullName, "state", "manager.db"));
        try
        {
            var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(p => p.Id == id), root.FullName);
            Directory.CreateDirectory(game.LiveModRoot);
            var relative = game.AdapterId == "unreal-paks" && Path.GetFileName(game.LiveModRoot)
                .Equals("Paks", StringComparison.OrdinalIgnoreCase) ? Path.Combine("~mods", "collision.pak") : "collision.pak";
            var live = Path.Combine(game.LiveModRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            await File.WriteAllTextAsync(live, "original-game-copy", ct);
            var sentinel = Path.Combine(game.LiveModRoot, "unrelated.bin");
            await File.WriteAllTextAsync(sentinel, "unmanaged", ct);

            // None of the fake executables exists. Semantic MHW paths must stay opaque to other adapters.
            var adapter = GameAdapters.Resolve(game);
            Assert.Contains(adapter.Validate(game), issue => issue.Blocking && issue.Code == "missing-executable");
            var armor = adapter.ParseAsset(@"nativePC\pl\f_equip\pl032_0010\wst\mod\f_wst032_0010.mod3");
            if (game.IsMonsterHunterWorld) Assert.Equal(new SemanticAsset("pl032_0010", "waist"), armor);
            else Assert.Null(armor);

            await db.InitializeAsync(ct);
            var hashes = new HashingService();
            var blobs = new BlobStore(Path.Combine(root.FullName, "blobs"), db);
            var scanner = new ModScanner(db, blobs, hashes, game);
            var executor = new DeploymentExecutor(db, blobs, hashes, game.GameRoot);
            var planner = new DeploymentPlanner(new ConflictEngine(game), game);
            var snapshots = new PlannerSnapshotRepository(db);
            async Task<string> AddSource(string key, string name, string content, int priority)
            {
                var package = Path.Combine(root.FullName, "sources", name);
                var path = game.IsMonsterHunterWorld ? Path.Combine(package, "nativePC", relative) :
                    Path.Combine(package, "GameRoot", Path.GetRelativePath(game.GameRoot, live));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, ct);
                var mod = new ModDescriptor(key, name, name, package, true, priority);
                await db.UpsertModAsync(mod, ct);
                var captured = Assert.Single(await scanner.CaptureAsync(mod, ct));
                Assert.Equal(Path.GetFullPath(live), executor.Destination(captured.Path), ignoreCase: true);
                return path;
            }
            var first = await AddSource("amber", "Amber", "first-source", 10);
            var second = await AddSource("indigo", "Indigo", "second-source", 20);
            var snapshot = await snapshots.LoadAsync(ct);
            var blocked = planner.Build(snapshot);
            Assert.True(blocked.IsBlocked);
            Assert.True(Assert.Single(blocked.Conflicts).Blocking);
            Assert.False((await executor.ApplyAsync(blocked, "Unresolved collision", ct: ct)).Success);
            Assert.Equal("original-game-copy", await File.ReadAllTextAsync(live, ct));

            // The conflict chooser stages the selected package and disables its alternative.
            var separate = LogicalModFamilies.Build(snapshot.Mods, snapshot.Files, game);
            Assert.Equal(2, separate.Count);
            var choices = separate.Select(family => new ModRowViewModel(family, () => { })).ToArray();
            foreach (var row in choices)
                row.StagedEnabled = row.ContainsMember("amber");
            var selected = choices.SelectMany(row => row.ExpandStage()).ToDictionary(pair => pair.Key, pair => pair.Value);
            var primary = planner.Build(WorkflowAnalysis.WithState(snapshot, selected));
            Assert.False(primary.IsBlocked);
            Assert.True((await executor.ApplyAsync(primary, "Apply selected mod", selected, ct: ct)).Success);
            Assert.Equal("first-source", await File.ReadAllTextAsync(live, ct));

            var familyId = await db.ChainManualFamilyAsync("amber", ["amber"], [["indigo"]], "Amber setup", ct);
            snapshot = await snapshots.LoadAsync(ct);
            var family = Assert.Single(LogicalModFamilies.Build(snapshot.Mods, snapshot.Files, game));
            Assert.All(family.Members, m => Assert.Equal(familyId, m.FamilyId));
            Assert.Contains(family.Members, m => m.Id == "amber" && m.FamilyRole == "Main" && m.Enabled);
            Assert.Contains(family.Members, m => m.Id == "indigo" && m.FamilyRole == "Optional" && !m.Enabled);
            var familyRow = new ModRowViewModel(family, () => { });
            familyRow.SetMemberEnabled("indigo", true);
            var stage = familyRow.ExpandStage();
            var requested = WorkflowAnalysis.WithState(snapshot, stage);
            var winner = Assert.Single(WorkflowAnalysis.Explore(requested, planner));
            Assert.Equal("indigo", winner.WinnerId);
            Assert.False(winner.Blocking);
            Assert.True((await executor.ApplyAsync(planner.Build(requested), "Apply optional add-on", stage, ct: ct)).Success);
            Assert.Equal("second-source", await File.ReadAllTextAsync(live, ct));
            snapshot = await snapshots.LoadAsync(ct);
            Assert.Equal("indigo", Assert.Single(snapshot.CurrentManifest).Value.ProviderModId);
            Assert.All(snapshot.Mods, m => Assert.True(m.Enabled));
            Assert.True((await executor.UndoLastAsync(ct)).Success);
            Assert.Equal("first-source", await File.ReadAllTextAsync(live, ct));
            snapshot = await snapshots.LoadAsync(ct);
            Assert.Contains(snapshot.Mods, m => m.Id == "indigo" && !m.Enabled && m.FamilyId == familyId);
            Assert.Equal("amber", Assert.Single(snapshot.CurrentManifest).Value.ProviderModId);
            Assert.Equal("first-source", await File.ReadAllTextAsync(first, ct));
            Assert.Equal("second-source", await File.ReadAllTextAsync(second, ct));
            Assert.Equal("unmanaged", await File.ReadAllTextAsync(sentinel, ct));
        }
        finally
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            root.Delete(recursive: true);
        }
    }
}
