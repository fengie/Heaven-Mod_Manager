using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class AutomaticDuplicateVisibilityTests
{
    public static TheoryData<string> Profiles => new(DiscoveredGameProfiles.Load().Select(game => game.Id));

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task RefreshOnlyCollapsesCompleteExactPeersAndShowsChangedPeersAgain(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var workspace = Directory.CreateTempSubdirectory("manager-duplicate-visibility-");
        var db = new ManagerDatabase(Path.Combine(workspace.FullName, "state", "manager.db"));
        try
        {
            var game = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(p => p.Id == id), workspace.FullName);
            var root = GameModStorage.Resolve(workspace.FullName, game).InstalledRoot;
            await db.InitializeAsync(ct);
            var scanner = new ModScanner(db, new BlobStore(Path.Combine(workspace.FullName, "blobs"), db), new HashingService(), game);
            var catalog = new CatalogService(db, scanner, root);
            var file = game.AdapterId == "unreal-paks" ? "costume_P.pak" : "costume.bin";
            var names = new[] { "Amber", "Coral", "Different bytes", "Different notes" };
            foreach (var name in names)
            {
                var folder = Directory.CreateDirectory(Path.Combine(root, name));
                File.WriteAllBytes(Path.Combine(folder.FullName, file), name == "Different bytes" ? [1, 3] : [1, 2]);
                File.WriteAllText(Path.Combine(folder.FullName, "README.txt"), name == "Different notes" ? "B" : "A");
            }
            await catalog.RefreshFoldersAsync(ct);
            var visible = await db.GetModsAsync(ct);
            Assert.Equal(3, visible.Count);
            Assert.Contains(visible, row => row.DisplayName == "Different bytes");
            Assert.Contains(visible, row => row.DisplayName == "Different notes");
            var keeper = Assert.Single(visible, row => row.DisplayName is "Amber" or "Coral");
            var copy = keeper.DisplayName == "Amber" ? "Coral" : "Amber";
            var originalIds = (await db.GetModsAsync(true, ct)).Select(row => row.Id).Order().ToArray();
            Assert.Equal(4, originalIds.Length);
            await catalog.RefreshFoldersAsync(ct);
            Assert.Equal(visible.Select(row => row.Id), (await db.GetModsAsync(ct)).Select(row => row.Id));

            File.WriteAllText(Path.Combine(root, copy, "extra.txt"), "New author instructions");
            await catalog.RefreshFoldersAsync(ct);
            Assert.Equal(originalIds, (await db.GetModsAsync(ct)).Select(row => row.Id).Order().ToArray());
            Assert.All(names, name => Assert.True(File.Exists(Path.Combine(root, name, file))));
            Assert.False(Directory.Exists(game.GameRoot));
        }
        finally
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
            workspace.Delete(true);
        }
    }
}
