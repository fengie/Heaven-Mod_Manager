using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using System.IO.Compression;

namespace MhwModManager.IntegrationTests;

public sealed class GenericNestedPackagesTests
{
    [Fact]
    public void Collection_wrappers_do_not_hide_independent_project_mounts()
    {
        var result = ModBoundaryDiscovery.Discover([
            "Mods/Shelf/First/Quartz/Content/Paks/~mods/a.pak",
            "Mods/Shelf/First/Quartz/Content/Paks/~mods/a.sig",
            "Mods/Shelf/Second/Quartz/Content/Paks/b.pak",
            "Mods/Shelf/Second/Quartz/Content/Paks/b.utoc",
            "Mods/Shelf/Second/Quartz/Content/Paks/b.ucas"]);
        Assert.False(result.NeedsReview);
        Assert.Equal((string[])["Mods/Shelf/First", "Mods/Shelf/Second"],
            result.Candidates.Select(c => c.RelativePath));
    }

    [Fact]
    public void Incomplete_companions_across_folders_cannot_form_a_package()
    {
        Assert.True(ModBoundaryDiscovery.Discover([
            "Quartz/Content/Paks/A/a.pak", "Quartz/Content/Paks/A/a.utoc",
            "Quartz/Content/Paks/B/a.ucas"]).NeedsReview);
    }

    [Fact]
    public void Loose_companions_stay_together_and_multiple_anonymous_chunks_need_review()
    {
        var result = ModBoundaryDiscovery.Discover(["Mods/Shelf/A/x.pak", "Mods/Shelf/A/x.sig",
            "Mods/Shelf/B/y.utoc", "Mods/Shelf/C/z.pak", "Mods/Shelf/C/q.pak"]);
        Assert.Equal("Mods/Shelf/A", Assert.Single(result.Candidates, c => c.Kind == ModBoundaryKind.Folder).RelativePath);
        Assert.Equal(2, result.Candidates.Count(c => c.Kind == ModBoundaryKind.Review));
    }

    [Fact]
    public void Exact_registered_mount_identifies_game_and_conflicts_require_review()
    {
        var game = GameProfile.Generic("test-game", "Test Game", Path.GetTempPath(),
            "Demo.exe", @"Quartz\Content\Paks");
        var other = game with { Id = "another-game", ModRootRelativePath = @"Other\Content\Paks" };
        string[] paths = ["Quartz/Content/Paks/~mods/costume.pak"];
        Assert.Equal(game.Id, GameModIntakeClassifier.Classify([game, other], "anonymous", paths).GameId);
        Assert.Null(GameModIntakeClassifier.Classify([game, other], "anonymous", paths, other.Id).GameId);
        Assert.Null(GameModIntakeClassifier.Classify([game, other with { ModRootRelativePath = game.ModRootRelativePath }],
            "anonymous", paths).GameId);
        Assert.Null(GameModIntakeClassifier.Classify([game], "anonymous", ["Content/Paks/item.pak"]).GameId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nested_input_routes_both_packages_and_keeps_originals(bool archive)
    {
        var ct = TestContext.Current.CancellationToken;
        var home = Directory.CreateTempSubdirectory();
        try
        {
            var source = Path.Combine(home.FullName, "incoming");
            var manager = Path.Combine(home.FullName, "manager");
            var game = GameProfile.Generic("fixture", "Fixture", Path.Combine(home.FullName, "game"),
                "Demo.exe", @"Quartz\Content\Paks");
            foreach (var name in new[] { "First", "Second" })
            {
                var parent = Directory.CreateDirectory(Path.Combine(source, "Mods", "shelf", name,
                    "Quartz", "Content", "Paks", "~mods"));
                File.WriteAllText(Path.Combine(parent.FullName, name + ".pak"), name);
            }
            var input = source;
            if (archive)
            {
                input = source + ".zip";
                ZipFile.CreateFromDirectory(source, input);
            }
            var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => [game]);
            var result = await intake.ImportPathAsync(input, ct);
            Assert.Equal(2, result.Routed);
            Assert.Equal(2, Directory.GetFileSystemEntries(Path.Combine(manager, "Mods by Game", game.Id, "Inbox")).Length);
            var originals = Directory.GetFiles(source, "*.pak", SearchOption.AllDirectories);
            Assert.Equal(2, originals.Length);
            Assert.All(originals,
                file => Assert.Equal(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
            Assert.False(Directory.Exists(game.GameRoot));
        }
        finally { home.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("costume.pak", @"root\Quartz\Content\Paks\~mods\costume.pak")]
    [InlineData("Quartz/Content/Paks/~mods/costume.pak", @"root\Quartz\Content\Paks\~mods\costume.pak")]
    [InlineData("Engine.ini", null)]
    [InlineData("Other/Content/Paks/costume.pak", null)]
    [InlineData("orphan.ucas", null)]
    public async Task Scanner_maps_the_registered_loader_once_or_refuses_the_package(string path, string? expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var home = Directory.CreateTempSubdirectory();
        try
        {
            var source = Path.Combine(home.FullName, "incoming");
            var file = Path.Combine(source, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "source fixture");
            var game = GameProfile.Generic("fixture", "Fixture", Path.Combine(home.FullName, "game"),
                "Demo.exe", @"Quartz\Content\Paks") with { AdapterId = "unreal-paks" };
            var db = new ManagerDatabase(Path.Combine(home.FullName, "state", "manager.db"));
            await db.InitializeAsync(ct);
            var mod = new ModDescriptor("mod", "Package", "Package", source, false, 0);
            await db.UpsertModAsync(mod, ct);
            var blobs = new BlobStore(Path.Combine(home.FullName, "blobs"), db);
            var hashing = new HashingService();
            var scanner = new ModScanner(db, blobs, hashing, game);
            if (expected is null)
                await Assert.ThrowsAsync<InvalidDataException>(() => scanner.CaptureAsync(mod, ct));
            else
            {
                var captured = Assert.Single(await scanner.CaptureAsync(mod, ct));
                Assert.Equal(expected, captured.Path);
                var plan = new DeploymentPlan("fixture-apply", DateTimeOffset.UtcNow,
                    [new(1, ChangeKind.Add, captured.Path, null, captured.BlobSha256, null, mod.Id, null, null)], [], []);
                Directory.CreateDirectory(game.GameRoot);
                var executor = new DeploymentExecutor(db, blobs, hashing, game.GameRoot);
                Assert.True((await executor.ApplyAsync(plan, "loader path fixture", ct: ct)).Success);
                var destination = Path.Combine(game.GameRoot, expected[5..]);
                Assert.Equal("source fixture", File.ReadAllText(destination));
                Assert.True((await executor.UndoLastAsync(ct)).Success);
                Assert.False(File.Exists(destination));
            }
            Assert.Equal("source fixture", File.ReadAllText(file));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); home.Delete(recursive: true); }
    }
}
