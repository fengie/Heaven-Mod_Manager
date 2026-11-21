using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class AutomaticDuplicateVisibilityTests
{
    private static readonly string[] ManualArchiveNames = ["A","B"];
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

            // A new service reads the persisted NTFS proof. Matching timestamps + sizes alone are insufficient.
            var reopened = new CatalogService(db, scanner, root);
            await reopened.RefreshFoldersAsync(ct);
            if (new DriveInfo(Path.GetPathRoot(root)!).DriveFormat == "NTFS")
                Assert.Equal(0, reopened.LastDuplicateBytesHashed);
            var path = Path.Combine(root, copy, file);
            var original = File.ReadAllBytes(path);
            var modified = (byte[])original.Clone();
            modified[0] ^= 1;
            var time = File.GetLastWriteTimeUtc(path);
            File.WriteAllBytes(path, modified); File.SetLastWriteTimeUtc(path, time);
            await reopened.RefreshFoldersAsync(ct);
            Assert.Equal(4, (await db.GetModsAsync(ct)).Count);
            File.WriteAllBytes(path, original);
            await reopened.RefreshFoldersAsync(ct);
            Assert.Equal(3, (await db.GetModsAsync(ct)).Count);
            var away = Path.Combine(workspace.FullName, "keeper-offline");
            Directory.Move(keeper.SourcePath, away);
            await reopened.RefreshFoldersAsync(ct);
            Assert.Contains(await db.GetModsAsync(ct), m => m.DisplayName == copy);
            Directory.Move(away, keeper.SourcePath);
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

    [Fact]
    public Task SavedAndPendingChoicesSurviveShelfAndWholePackageProjection() =>
        IsolatedWpfSession.RunAsync(async (session, ct) =>
        {
            var db = session.Database;
            var a = Directory.CreateDirectory(Path.Combine(session.Paths.ModsRoot, "Original")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(session.Paths.ModsRoot, "Copy")).FullName;
            File.WriteAllText(Path.Combine(a, "mod.bin"), "same"); File.WriteAllText(Path.Combine(b, "mod.bin"), "same");
            await db.UpsertModAsync(new("active", "Original", "Original", a, true, 1), ct);
            await db.UpsertModAsync(new("copy", "Copy", "Copy", b, false, 2), ct);
            await db.ReconcileExactCopiesAsync([["active", "copy"]], ct);
            await db.SetShelfEntriesAsync("shelf", [("copy", "duplicate", "active")], ct);
            Assert.DoesNotContain(await db.GetModsAsync(ct), m => m.Id == "copy");
            db.SetPendingModIds(["copy"]);
            Assert.Contains(await db.GetModsAsync(ct), m => m.Id == "copy");
            db.SetPendingModIds([]);
            Assert.DoesNotContain(await db.GetModsAsync(ct), m => m.Id == "copy");
            await db.ExecuteAsync("INSERT INTO profiles(id,name,created_at,updated_at) VALUES('saved','Saved',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP); INSERT INTO profile_mods(profile_id,mod_id,enabled,priority) VALUES('saved','copy',1,2)", ct: ct);
            Assert.Contains(await db.GetModsAsync(ct), m => m.Id == "copy");
            await db.SetShelfEntriesAsync("shelf", [("copy", "wrapper", null)], ct);
            Assert.DoesNotContain(await db.GetModsAsync(ct), m => m.Id == "copy");
        });

    [Fact]
    public Task ExplicitArchiveStillFindsIndexedCopiesAndPreservesTheVisibleKeeper() =>
        IsolatedWpfSession.RunAsync(async (session, ct) =>
        {
            var db = session.Database;
            foreach (var name in ManualArchiveNames)
            {
                var folder = Directory.CreateDirectory(Path.Combine(session.Paths.ModsRoot, name));
                File.WriteAllText(Path.Combine(folder.FullName, "outfit.bin"), "identical payload");
                var mod = new ModDescriptor(name, name, name, folder.FullName, false, name == "A" ? 10 : 20);
                await db.UpsertModAsync(mod, ct);
                await session.Scanner.CaptureAsync(mod, ct);
            }
            await session.Catalog.RefreshFoldersAsync(ct);
            Assert.Equal("A", Assert.Single(await db.GetModsAsync(ct)).Id);
            Assert.True(Directory.Exists(Path.Combine(session.Paths.ModsRoot, "B")));
            var group = Assert.Single((await session.Duplicates.AnalyzeAsync(ct)).ExactDuplicates);
            Assert.Equal("A", group.RecommendedKeeperId);
            Assert.Equal(1, await session.Duplicates.ArchiveSafeAsync(ct));
            Assert.True(Directory.Exists(Path.Combine(session.Paths.ModsRoot, "A")));
            Assert.False(Directory.Exists(Path.Combine(session.Paths.ModsRoot, "B")));
            Assert.Equal("A", Assert.Single(await db.GetModsAsync(true, ct)).Id);
        });
}
