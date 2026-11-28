using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

var trial = Path.GetFullPath(args[0]);
var manager = Path.GetFullPath(args[1]);
var state = Path.Combine(trial, "State");
var input = Path.Combine(trial, "Corpus");
var catalogHome = Path.Combine(trial, "Library");
Environment.SetEnvironmentVariable("MHW_MASTER_DEBUG_ROOT", Path.Combine(trial, "Debug"));
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(70));
var ct = timeout.Token;
var archive = new ArchiveInspector();
var games = new GameProfileRegistry(Path.Combine(manager,"State")).Load().Select(game =>
    game with { GameRoot = Path.Combine(trial, "Games", game.Id) }).ToArray();
var routing = new GameModIntakeService(catalogHome, state, () => games, archive);
var firstClock = Stopwatch.StartNew();
var first = await routing.ImportPathsAsync([input], ct: ct);
var firstMs = firstClock.Elapsed.TotalMilliseconds;
var inspectResults = new List<object>();
foreach (var file in Directory.GetFiles(input).Order())
{
    try
    {
        var info = await archive.InspectAsync(file,ct);
        inspectResults.Add(new { extension=Path.GetExtension(file), bytes=new FileInfo(file).Length, opened=true, entries=info.Entries.Count });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        inspectResults.Add(new { extension=Path.GetExtension(file), bytes=new FileInfo(file).Length, opened=false, error=ex.GetType().Name });
    }
}
var inbox = new List<object>();
foreach (var group in first.Items.Where(item => item.Status is GameModIntakeStatus.Routed or GameModIntakeStatus.AlreadyPresent).GroupBy(item => item.GameId))
{
    var game = games.Single(game => game.Id == group.Key);
    var target = GameModStorage.Resolve(catalogHome, game);
    GameModStorage.EnsureDirectories(target);
    var dbPath = Path.Combine(state, game.Id, "manager.db");
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    var db = new ManagerDatabase(dbPath);
    await db.InitializeAsync(ct);
    var repository = new PlannerSnapshotRepository(db);
    var scanner = new ModScanner(db, new BlobStore(Path.Combine(state,"CAS",game.Id),db),new HashingService(),game);
    var catalog = new CatalogService(db,scanner,target.InstalledRoot,target.LegacyInstalledRoot,
        (dir,token) => routing.SelectableManagedFolders(dir,game,token),(dir,token) => routing.InspectManagedShelf(dir,game,token));
    var processor = new SmartInboxService(db,archive,catalog,new NexusMetadataService(db,repository,state,game),
        new AutoCategoryService(db,game),target.InboxRoot,target.InstalledRoot,legacyInboxRoot:target.LegacyInboxRoot);
    var clock = Stopwatch.StartNew();
        InboxRunResult? processed=null;
    var canceled=false;
    try { processed=await processor.ProcessAsync(null,refreshMetadata:false,ct:ct); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { canceled=true; }
    var elapsedMs = clock.Elapsed.TotalMilliseconds;
    var result = await new PlannerSnapshotRepository(new ManagerDatabase(dbPath)).LoadAsync(CancellationToken.None);
    inbox.Add(new { game.Id, elapsedMs, processed, canceled, rows=result.Mods.Count, enabled=result.Mods.Count(x=>x.Enabled),
        deployed=result.CurrentManifest.Count, indexes=result.Files.Count });
}
var replayClock = Stopwatch.StartNew();
using var replayTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
var replay = await routing.ImportPathsAsync([input], ct: replayTimeout.Token);
Console.WriteLine(JsonSerializer.Serialize(new { firstMs, replayMs=replayClock.Elapsed.TotalMilliseconds, first,replay,inbox,inspectResults }));
