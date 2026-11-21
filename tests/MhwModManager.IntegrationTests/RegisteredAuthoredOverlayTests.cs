using Microsoft.Data.Sqlite;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class RegisteredAuthoredOverlayTests
{
    public static TheoryData<string> Games => new(DiscoveredGameProfiles.Load().Select(game => game.Id));

    [Theory]
    [MemberData(nameof(Games))]
    public async Task DeclaredGameRootAndTwoIndependentOverlaysDiscoverCaptureAndKeepDistinctWinners(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory("mod-family-roots-");
        var db = new ManagerDatabase(Path.Combine(temp.FullName, "manager.db"));
        try
        {
            var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(g => g.Id == id), temp.FullName);
            var installed = Path.Combine(temp.FullName, "installed");
            var family = Directory.CreateDirectory(Path.Combine(installed, "Expedition kit")).FullName;
            var notes = Path.Combine(family, "README.txt");
            await File.WriteAllTextAsync(notes, "Install the main file first. The optional files can be used together.", ct);
            var targetRoot = game.ModRootRelativePath;
            if (game.AdapterId == "unreal-paks") targetRoot = Path.Combine(targetRoot, "~mods");
            if (string.IsNullOrWhiteSpace(targetRoot)) targetRoot = "Data";
            var left = Path.Combine(targetRoot, "lamp.pak");
            var right = Path.Combine(targetRoot, "visor.pak");
            const string main = "Main File";
            const string lamp = "Optional File - Lamp";
            const string visor = "Optional File - Visor";
            string Write(string part, string relative)
            {
                var path = Path.Combine(family, part, "GameRoot", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, part);
                return path;
            }
            Write(main, left);
            Write(main, right);
            Write(lamp, left);
            Write(visor, right);

            await db.InitializeAsync(ct);
            var hashes = new HashingService();
            var blobs = new BlobStore(Path.Combine(temp.FullName, "blobs"), db);
            var scanner = new ModScanner(db, blobs, hashes, game);
            var catalog = new CatalogService(db, scanner, installed);
            await catalog.RefreshFoldersAsync(ct);
            await catalog.RefreshFoldersAsync(ct);
            var mods = await db.GetModsAsync(ct);
            Assert.Equal(3, mods.Count);
            var grouped = Assert.Single(LogicalModFamilies.Build(mods, game: game));
            Assert.Equal("Expedition kit", grouped.DisplayName);
            Assert.All(mods, mod => Assert.False(mod.Enabled));
            await catalog.EnsureCapturedAsync(mods, ct);

            var snapshot = await new PlannerSnapshotRepository(db).LoadAsync(ct);
            Assert.Equal(4, snapshot.Files.Count);
            var planner = new DeploymentPlanner(new ConflictEngine(game), game);
            var all = snapshot with { Mods = snapshot.Mods.Select(mod => mod with { Enabled = true }).ToArray() };
            var plan = planner.Build(all);
            Assert.False(plan.IsBlocked);
            Assert.Equal(2, plan.Changes.Count);
            var executor = new DeploymentExecutor(db, blobs, hashes, game.GameRoot);
            foreach (var (relative, owner) in new[] { (left, lamp), (right, visor) })
            {
                var target = Path.Combine(game.GameRoot, relative);
                var change = Assert.Single(plan.Changes, item => PathRules.Comparer.Equals(executor.Destination(item.Path), target));
                Assert.Equal(mods.Single(mod => mod.Name == owner).Id, change.ProviderAfter);
                Assert.Equal(owner, File.ReadAllText(blobs.PathFor(change.AfterBlobSha256!)));
                Assert.False(File.Exists(target));
            }

            // Same-target siblings or missing combination evidence must keep the package in review.
            var overlap = Write(visor, left);
            Assert.Empty(AuthoredPackageFamily.Read(family, game, ct));
            File.Delete(overlap);
            await File.WriteAllTextAsync(notes, "Install the main file first. Choose only one optional file.", ct);
            Assert.Empty(AuthoredPackageFamily.Read(family, game, ct));
        }
        finally
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None)) SqliteConnection.ClearPool(connection);
            temp.Delete(true);
        }
    }
}
