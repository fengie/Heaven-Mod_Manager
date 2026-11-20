using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DiscoveredLibraryTests
{
    [Fact]
    public void VerifiedWorkshopTitlesRequireMatchingIdentityAndRetainIndependentItemIds()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var first = Path.Combine(root.FullName, "one");
            var other = Path.Combine(root.FullName, "two");
            var game = GameProfile.Generic("fixture", "Fixture", Path.Combine(first, "steamapps", "common", "Fixture"),
                "Game.exe", "Mods", steamAppId: "123");
            var content = Path.Combine(first, "steamapps", "workshop", "content", "123");
            var second = Path.Combine(other, "steamapps", "workshop", "content", "123");
            Directory.CreateDirectory(game.GameRoot);
            void Item(string parent, string id, string? metadata)
            {
                var dir = Directory.CreateDirectory(Path.Combine(parent, id));
                if (metadata is not null) File.WriteAllText(Path.Combine(dir.FullName, "data.json"), metadata);
            }
            static void Install(string source, params string[] ids)
            {
                var keys = string.Join(" ", ids.Select(id => $"\"{id}\" {{}}"));
                File.WriteAllText(Path.Combine(source, "steamapps", "workshop", "appworkshop_123.acf"),
                    $"\"AppWorkshop\" {{ \"appid\" \"123\" \"WorkshopItemsInstalled\" {{ {keys} }} }}");
            }

            Item(content, "10", """{"id":10,"folderName":"10","title":"第一名皮肤"}""");
            Item(content, "11", """{"id":"11","title":"第一名皮肤"}""");
            Item(content, "12", """{"id":13,"title":"Wrong item"}""");
            Item(content, "13", """{"id":13,"folderName":"14","title":"Conflicting folder"}""");
            Item(content, "14", "invalid");
            Item(content, "15", null);
            Item(content, "16", """{"id":16,"title":"One title"}""");
            Item(second, "16", """{"id":16,"title":"Other title"}""");
            Item(content, "17", """{"id":17,"title":"Not installed"}""");
            Item(content, "18", """{"id":18,"title":"spoof\u202Ename"}""");
            Item(content, "19", JsonSerializer.Serialize(new { id = 19, title = "Large", extra = new string('a', 20_000) }));
            Item(content, "20", """{"id":20,"id":20,"title":"Ambiguous descriptor"}""");
            Item(content, "21", """{"id":21,"title":"invalid\uD800"}""");
            Install(first, "21", "10", "11", "12", "13", "14", "15", "16", "18", "19", "20");
            Install(other, "16");
            var inventory = LocalModInventory.Scan([game], [other, first], [], ct: TestContext.Current.CancellationToken);
            var rows = LocalModInventory.WorkshopLibrary(inventory, "123", []).ToDictionary(row => row.ItemId);
            Assert.Equal(11, rows.Count);
            Assert.Equal("第一名皮肤", rows["10"].DisplayName);
            Assert.Equal("第一名皮肤", rows["11"].DisplayName);
            foreach (var id in new[] { "12", "13", "14", "15", "16", "18", "19", "20", "21" })
                Assert.Equal($"Workshop item {id}", rows[id].DisplayName);
            Assert.Equal(2, rows["16"].Locations.Count);
            Assert.False(rows.ContainsKey("17"));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void ReopeningRestoresOnlyTheSameProfilesSnapshotAndReconciliationCanClearIt()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var game = GameProfile.Generic("sample", "Sample", Path.Combine(root.FullName, "game"), "game.exe", "Mods",
                steamAppId: "123");
            var registry = new GameProfileRegistry(root.FullName);
            Assert.Empty(registry.LoadDiscoveredLibrary(game));
            var original = new WorkshopLibraryReference("10", [game.GameRoot], "第一名皮肤");
            registry.SaveDiscoveredLibrary(game, [original]);
            var reopened = new GameProfileRegistry(root.FullName);
            var saved = Assert.Single(reopened.LoadDiscoveredLibrary(game));
            Assert.Equal(original.ItemId, saved.ItemId);
            Assert.Equal(original.DisplayName, saved.DisplayName);
            Assert.Equal(original.SourceSummary, saved.SourceSummary);
            Assert.Empty(reopened.LoadDiscoveredLibrary(game with { Id = "other" }));
            Assert.Empty(reopened.LoadDiscoveredLibrary(game with { SteamAppId = "321" }));
            Assert.Empty(reopened.LoadDiscoveredLibrary(game with { GameRoot = Path.Combine(root.FullName, "moved") }));
            var pending = Assert.Single(reopened.ReconcileDiscoveredLibrary(game, [],
                [new LocalModLocation("Sample", "Installation unavailable", "", game.GameRoot)]));
            Assert.Contains("source unavailable", pending.OwnerLabel);
            var moved = new WorkshopLibraryReference("10", [Path.Combine(root.FullName, "new", "10")], original.DisplayName);
            var unavailable = new LocalModLocation("Sample", "Installation unavailable", "", game.GameRoot);
            Assert.Equal(moved, Assert.Single(reopened.ReconcileDiscoveredLibrary(game, [moved], [unavailable])));
            var plugin = new ExternalLibraryReference("10", "Unrelated plugin", "BepInEx", moved.Sources);
            Assert.Equal(2, reopened.ReconcileDiscoveredLibrary(game, [plugin], [unavailable]).Count);
            Assert.Empty(reopened.ReconcileDiscoveredLibrary(game, [], []));
            reopened.SaveDiscoveredLibrary(game, Enumerable.Repeat(original, 16385).ToArray());
            Assert.Equal(original.DisplayName, Assert.Single(reopened.LoadDiscoveredLibrary(game)).DisplayName);
            reopened.SaveDiscoveredLibrary(game, []);
            Assert.Empty(registry.LoadDiscoveredLibrary(game));
            var file = Assert.Single(root.GetFiles("discovered-library-*.json"));
            File.WriteAllText(file.FullName, "broken");
            Assert.Empty(registry.LoadDiscoveredLibrary(game));
        }
        finally { root.Delete(true); }
    }
}
