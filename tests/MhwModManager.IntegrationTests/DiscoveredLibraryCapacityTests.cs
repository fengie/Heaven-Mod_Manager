using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DiscoveredLibraryCapacityTests
{
    [Theory]
    [InlineData(16_385, 5)]
    [InlineData(2_100, 2_100)]
    public void Oversized_refresh_invalidates_only_the_previous_snapshot_and_allows_recovery(int rowCount, int titleLength)
    {
        var root = Directory.CreateTempSubdirectory("manager-discovery-limit-");
        try
        {
            var registry = new GameProfileRegistry(root.FullName);
            var first = GameProfile.Generic("first", "Same name", Path.Combine(root.FullName, "first"), "game.exe", "Mods");
            var second = first with { Id = "second", GameRoot = Path.Combine(root.FullName, "second") };
            var removed = new ExternalLibraryReference("removed", "Removed mod", "External", [first.GameRoot]);
            var other = new ExternalLibraryReference("other", "Still installed", "External", [second.GameRoot]);
            registry.SaveDiscoveredLibrary(first, [removed]);
            registry.SaveDiscoveredLibrary(second, [other]);

            var title = new string('x', titleLength);
            var replacement = Enumerable.Range(0, rowCount)
                .Select(i => new ExternalLibraryReference(i.ToString(System.Globalization.CultureInfo.InvariantCulture), title, "External", [first.GameRoot])).ToArray();
            registry.SaveDiscoveredLibrary(first, replacement);
            var reopened = new GameProfileRegistry(root.FullName);
            Assert.Empty(reopened.LoadDiscoveredLibrary(first));
            Assert.Equal(other.ItemId, Assert.Single(reopened.LoadDiscoveredLibrary(second)).ItemId);
            reopened.SaveDiscoveredLibrary(first, [replacement[0]]);
            Assert.Equal(replacement[0].ItemId, Assert.Single(new GameProfileRegistry(root.FullName).LoadDiscoveredLibrary(first)).ItemId);
        }
        finally { root.Delete(true); }
    }
}
