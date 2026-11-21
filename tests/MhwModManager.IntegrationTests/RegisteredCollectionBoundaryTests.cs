using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class RegisteredCollectionBoundaryTests
{
    public static TheoryData<string> UnrealProfiles => new(DiscoveredGameProfiles.Load()
        .Where(game => game.AdapterId == "unreal-paks").Select(game => game.Id));

    [Theory]
    [MemberData(nameof(UnrealProfiles))]
    public async Task NamedPackagesRetainTheirOwnCompanionsWhenCollectionHasAnOrphan(string id)
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory("registered-mod-packages-");
        try
        {
            var games = DiscoveredGameProfiles.Load().Select(game => DiscoveredGameProfiles.Isolate(game, temp.FullName)).ToArray();
            var owner = games.Single(game => game.Id == id);
            var manager = Path.Combine(temp.FullName, "Manager");
            var source = Directory.CreateDirectory(Path.Combine(temp.FullName, "~mods")).FullName;
            var cloak = Directory.CreateDirectory(Path.Combine(source, id + " Amber Cloak")).FullName;
            var mask = Directory.CreateDirectory(Path.Combine(source, id + " Jade Mask")).FullName;
            string[] cloakNames = ["cloth_P.pak", "cloth_P.sig"];
            string[] maskNames = ["mask_P.pak", "mask_P.ucas", "mask_P.utoc"];
            var originals = cloakNames.Select(name => Path.Combine(cloak, name))
                .Concat(maskNames.Select(name => Path.Combine(mask, name)))
                .Append(Path.Combine(source, "unattributed.pak")).ToArray();
            foreach (var path in originals) File.WriteAllText(path, Path.GetFileName(path));

            var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => games);
            var first = await intake.ImportPathAsync(source, ct);
            Assert.Equal(2, first.Routed);
            Assert.Equal(1, first.NeedsReview);
            Assert.All(first.Items.Where(item => item.Status == GameModIntakeStatus.Routed),
                item => Assert.Equal(id, item.GameId));
            var inbox = GameModStorage.Resolve(manager, owner).InboxRoot;
            var packages = Directory.GetDirectories(inbox);
            Assert.Equal(2, packages.Length);
            foreach (var expected in new[] { cloakNames, maskNames })
            {
                var found = Assert.Single(packages, folder => File.Exists(Path.Combine(folder, expected[0])));
                Assert.Equal(expected.Order(), Directory.GetFiles(found, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(found, path)).Order());
                foreach (var name in expected) Assert.Equal(name, File.ReadAllText(Path.Combine(found, name)));
            }
            var repeat = await intake.ImportPathAsync(source, ct);
            Assert.Equal(0, repeat.Routed);
            Assert.Equal(2, repeat.AlreadyPresent);
            Assert.Equal(1, repeat.NeedsReview);
            Assert.All(originals, path => Assert.Equal(Path.GetFileName(path), File.ReadAllText(path)));
        }
        finally { temp.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AGameNamedCollectionInsideMixedInputKeepsItsScopeForEachPhysicalOrArchivedChild(bool zip)
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory("nested-game-collections-");
        try
        {
            var games = DiscoveredGameProfiles.Load().Select(game => DiscoveredGameProfiles.Isolate(game, temp.FullName)).ToArray();
            var manager = Path.Combine(temp.FullName, "Manager");
            var batch = Directory.CreateDirectory(Path.Combine(temp.FullName, "Batch")).FullName;
            string[] owners = ["sifu", "wuchang-fallen-feathers"];
            foreach (var owner in owners)
            {
                var collection = Directory.CreateDirectory(Path.Combine(batch, owner, "~mods")).FullName;
                foreach (var title in new[] { "Cloak", "Mask" })
                {
                    var folder = Directory.CreateDirectory(Path.Combine(collection, title));
                    File.WriteAllText(Path.Combine(folder.FullName, title + ".pak"), owner + title);
                }
                File.WriteAllText(Path.Combine(collection, "unclaimed.pak"), "unclaimed");
            }
            var input = batch;
            if (zip)
            {
                input = Path.Combine(temp.FullName, "Batch.zip");
                ZipFile.CreateFromDirectory(batch, input);
            }
            var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => games);
            var result = await intake.ImportPathAsync(input, ct);
            Assert.Equal(4, result.Routed);
            Assert.Equal(2, result.NeedsReview);
            foreach (var owner in owners)
            {
                var inbox = GameModStorage.Resolve(manager, games.Single(game => game.Id == owner)).InboxRoot;
                var installed = Directory.GetFiles(inbox, "*.pak", SearchOption.AllDirectories);
                Assert.Equal(new[] { owner + "Cloak", owner + "Mask" }, installed.Select(File.ReadAllText).Order().ToArray());
            }
        }
        finally { temp.Delete(true); }
    }
}
