using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

var state = Path.GetFullPath(args[0]);
var owned = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(args[1]))!;
using var scanBudget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var registry = new GameProfileRegistry(state);
var game = registry.Load().Single(game => game.SteamAppId == "992300");
var managed = owned.GetValueOrDefault(game.Id, []);

var timer = Stopwatch.StartNew();
var inventory = registry.ScanLocalMods(scanBudget.Token);
var inventoryMs = timer.Elapsed.TotalMilliseconds;
timer.Restart();
var workshop = LocalModInventory.WorkshopLibrary(inventory, game.SteamAppId, managed);
var projectionMs = timer.Elapsed.TotalMilliseconds;
var packages = LocalModInventory.RegisteredPackageLibrary(inventory, game, managed, scanBudget.Token);
var catalog = registry.ReconcileDiscoveredLibrary(game, workshop.Cast<ExternalLibraryReference>().Concat(packages).ToArray(), inventory);
var initialCacheRows = registry.LoadDiscoveredLibrary(game).Count;
timer.Restart();
registry.SaveDiscoveredLibrary(game, catalog);
var saveMs = timer.Elapsed.TotalMilliseconds;
var cache = new GameProfileRegistry(state);
var cacheReads = new List<double>();
IReadOnlyList<ExternalLibraryReference> rows = [];
for (var sample = 0; sample < 11; sample++)
{
    timer.Restart();
    rows = cache.LoadDiscoveredLibrary(game);
    cacheReads.Add(timer.Elapsed.TotalMilliseconds);
    if (!rows.Select(row => (row.ItemId, row.DisplayName)).SequenceEqual(catalog.Select(row => (row.ItemId, row.DisplayName))))
        throw new InvalidOperationException("Cache round trip changed identities or display names.");
}
var titled = workshop.Where(row => row.DisplayName != $"Workshop item {row.ItemId}").ToArray();
var repeated = titled.GroupBy(row => row.DisplayName, StringComparer.Ordinal).OrderByDescending(group => group.Count()).First();
var warm = cacheReads.Skip(1).OrderBy(x => x).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new {
    inventoryMs, projectionMs, saveMs, initialCacheRows, catalogRows = catalog.Count, workshopRows = workshop.Count,
    uniqueWorkshopIds = workshop.Select(row => row.ItemId).Distinct().Count(),
    titledRows = titled.Length, fallbackRows = workshop.Count - titled.Length,
    distinctTitles = titled.Select(row => row.DisplayName).Distinct().Count(),
    repeatedTitleIds = repeated.Select(row => row.ItemId).Distinct().Count(),
    representative = workshop.Single(row => row.ItemId == "1870844476").DisplayName,
    cachedRows = rows.Count, firstCacheMs = cacheReads[0], warmMedianMs = (warm[4]+warm[5])/2,
    warmMaxMs = warm[^1], managedCount = managed.Length
}, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
