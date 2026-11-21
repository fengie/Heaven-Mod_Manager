using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class InventoryToLibraryProfileIsolationTests
{
    [Fact]
    public void EveryProfileCanReopenItsOwnVerifiedPackageAndRetireItWhenManaged()
    {
        var temp = Directory.CreateTempSubdirectory("manager-inventory-profile-");
        try
        {
            var games = DiscoveredGameProfiles.Load()
                .Select(game => DiscoveredGameProfiles.Isolate(game, temp.FullName)).ToArray();
            var registry = new GameProfileRegistry(temp.FullName);
            foreach (var game in games)
            {
                var directory = Directory.CreateDirectory(Path.Combine(game.GameRoot, "reframework", "autorun"));
                File.WriteAllText(Path.Combine(directory.FullName, "SameTitle.lua"), "-- " + game.Id);
                File.WriteAllText(Path.Combine(directory.FullName, "SameTitle.txt"), "documentation");
            }
            var inventory = LocalModInventory.Scan(games, [], [], ct: TestContext.Current.CancellationToken);
            foreach (var game in games)
            {
                var location = Path.Combine(game.GameRoot, "reframework", "autorun", "SameTitle.lua");
                var represented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var found = LocalModInventory.InstalledPackages(inventory, game, represented, TestContext.Current.CancellationToken);
                var row = Assert.Single(found);
                Assert.Equal("SameTitle", row.DisplayName);
                Assert.Equal(location, Assert.Single(row.Locations));
                registry.SaveDiscoveredLibrary(game, registry.ReconcileDiscoveredLibrary(game, found, inventory));
                var reopened = new GameProfileRegistry(temp.FullName);
                Assert.Equal(location, Assert.Single(Assert.Single(reopened.LoadDiscoveredLibrary(game)).Locations));
                represented.Add(location);
                var managed = LocalModInventory.InstalledPackages(inventory, game, represented, TestContext.Current.CancellationToken);
                Assert.Empty(managed);
                reopened.SaveDiscoveredLibrary(game, reopened.ReconcileDiscoveredLibrary(game, managed, inventory));
                Assert.Empty(new GameProfileRegistry(temp.FullName).LoadDiscoveredLibrary(game));
                Assert.Equal("-- " + game.Id, File.ReadAllText(location));
            }
        }
        finally { temp.Delete(true); }
    }
}

