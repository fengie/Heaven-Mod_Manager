using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DiscoveredLibraryIncompleteScanTests
{
    [Fact]
    public void BudgetLimitedRefreshRetainsOnlyPriorEvidenceUnderTheIncompleteRootUntilACompleteRefresh()
    {
        var temp = Directory.CreateTempSubdirectory("manager-library-budget-");
        try
        {
            var gameRoot = Path.Combine(temp.FullName, "game");
            var siblingRoot = gameRoot + "-previous";
            var game = GameProfile.Generic("sample", "Sample", gameRoot, "game.exe", "Mods");
            var registry = new GameProfileRegistry(temp.FullName);
            var live = new ExternalLibraryReference("prior", "Prior", "External", [Path.Combine(gameRoot, "Mods", "prior.lua")]);
            var removed = new ExternalLibraryReference("removed", "Removed", "External", [Path.Combine(siblingRoot, "Mods", "removed.lua")]);
            var verified = new ExternalLibraryReference("current", "Current", "External", [Path.Combine(gameRoot, "Mods", "current.lua")]);
            registry.SaveDiscoveredLibrary(game, [live, removed]);
            LocalModLocation[] partial = [new(game.DisplayName, "Inventory scan limit reached", "Registered installation", gameRoot)];

            var result = registry.ReconcileDiscoveredLibrary(game, [verified], partial);
            Assert.Equal(2, result.Count);
            var retained = Assert.Single(result, row => row.ItemId == live.ItemId);
            Assert.Equal(live.Locations, retained.Locations);
            Assert.Contains("scan incomplete", retained.OwnerLabel, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(verified, result);
            registry.SaveDiscoveredLibrary(game, result);
            var reopened = new GameProfileRegistry(temp.FullName);
            Assert.Equal(2, reopened.LoadDiscoveredLibrary(game).Count);
            Assert.Equal([verified], reopened.ReconcileDiscoveredLibrary(game, [verified], []));
        }
        finally { temp.Delete(true); }
    }
}
