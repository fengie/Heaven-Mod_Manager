using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MhwModManager.Core;
using MhwModManager.Storage;
using MhwModManager.Filesystem;
using MhwModManager.Mhw;
using MhwModManager.Diagnostics;
using MhwModManager.Automation;
using MhwModManager.Updater;
using Serilog;

namespace MhwModManager.App;

public sealed partial class App:Application, IDisposable
{
    public static AppServices Services {get;private set;}=null!;
    private DispatcherWatchdog? watchdog;
    private FileChangeHintService? changeHints;
    private bool disposed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var bootstrapRoot=ResolveDiagnosticRoot();
        UnifiedDebugLog.Configure(bootstrapRoot);
        MasterDebugLog.InstallGlobalExceptionHooks();
        WpfMasterTracing.Install();
        MasterDebugLog.Write("TRACE-COVERAGE", "v8.8.6 call verification enabled: method scopes, first-chance exceptions, WPF internals, processes, database transactions, filesystem/deployment activity, ViewModel changes, runtime telemetry, startup/build/test logs.");
        UnifiedDebugLog.Section("APP-BOOTSTRAP", $"ENTER OnStartup v8.8.6 | PID={Environment.ProcessId} | BaseDirectory={AppContext.BaseDirectory}");
        try
        {
            base.OnStartup(e);
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
        }
        catch(Exception ex)
        {
            UnifiedDebugLog.Write("APP-BOOTSTRAP", "base.OnStartup failed", ex);
            throw;
        }

        DispatcherUnhandledException += (_,args) =>
        {
            UnifiedDebugLog.Write("UNHANDLED-DISPATCHER", "DispatcherUnhandledException", args.Exception);
            args.Handled=true;
            try
            {
                MessageBox.Show(args.Exception+"\n\nMASTER DEBUG LOG (send this file):\n"+UnifiedDebugLog.FilePath,
                    "Universal Mod Manager unexpected UI failure",MessageBoxButton.OK,MessageBoxImage.Error);
            }
            catch{}
            Shutdown(-10);
        };
        AppDomain.CurrentDomain.UnhandledException += (_,args) => UnifiedDebugLog.Write("UNHANDLED-APPDOMAIN", $"IsTerminating={args.IsTerminating}", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_,args) =>
        {
            UnifiedDebugLog.Write("UNOBSERVED-TASK", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        var startup=StartupDiagnosticSession.Start(bootstrapRoot);
        startup.Info("startup.begin", $"Arguments: {string.Join(" ",e.Args)}; MasterLog={UnifiedDebugLog.FilePath}");

        var splash=new StartupWindow();
        splash.Show();
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            splash.SetDetail("Locating the active game and its isolated state database…");
            var paths=startup.Run("bootstrap.paths.discover",AppPaths.Discover);
            startup.Info("bootstrap.paths.resolved", $"ToolRoot={paths.ToolRoot}; ModsRoot={paths.ModsRoot}; StateRoot={paths.StateRoot}; GameRoot={paths.GameRoot}; Database={paths.DatabasePath}; MasterLog={UnifiedDebugLog.FilePath}");
            startup.Run("bootstrap.state-directories",()=>Directory.CreateDirectory(paths.NextStateRoot));

            var db=startup.Run("services.database.construct",()=>new ManagerDatabase(paths.DatabasePath));
            await startup.RunAsync("database.initialize",ct=>db.InitializeAsync(ct));
            var plannerSnapshots=startup.Run("services.planner-snapshots",()=>new PlannerSnapshotRepository(db));
            var logger=startup.Run("services.logging",()=>AppLogging.Create(paths.StateRoot));
            var telemetry=startup.Run("services.telemetry",()=>new DiagnosticTelemetry(db,logger));
            var hash=startup.Run("services.hashing",()=>new HashingService());
            var blobs=startup.Run("services.blob-store",()=>new BlobStore(paths.BlobRoot,db));
            var gameRegistry=startup.Run("services.game-registry",()=>new GameProfileRegistry(paths.StateRoot));
            var scanner=startup.Run("services.mod-scanner",()=>new ModScanner(db,blobs,hash,paths.Game));
            var catalog=startup.Run("services.catalog",()=>new CatalogService(db,scanner,paths.ModsRoot));
            var planner=startup.Run("services.deployment-planner",()=>new DeploymentPlanner(new ConflictEngine(paths.Game),paths.Game));
            var executor=startup.Run("services.deployment-executor",()=>new DeploymentExecutor(db,blobs,hash,paths.GameRoot));
            var guard=startup.Run("services.game-process-guard",()=>new GameProcessGuard(paths.Game));
            var health=startup.Run("services.health",()=>new HealthService(db,plannerSnapshots,hash,blobs,executor.Destination,paths.Game));
            var profiles=startup.Run("services.profiles",()=>new ProfileRepository(db));
            var presentationReads=startup.Run("services.presentation-reads",()=>new PresentationReadRepository(db));
            var migrator=startup.Run("services.legacy-migrator",()=>new LegacyV7Migrator(db,paths.ToolRoot,paths.BlobRoot));
            var archive=startup.Run("services.archive-inspector",()=>new ArchiveInspector());
            var support=startup.Run("services.support-bundle",()=>new SupportBundleService(db,paths.StateRoot,telemetry));
            var nexus=startup.Run("services.nexus-metadata",()=>new NexusMetadataService(db,plannerSnapshots,paths.NextStateRoot,paths.Game));
            var gameBuild=startup.Run("services.game-build-monitor",()=>new GameBuildMonitor(db,plannerSnapshots,paths.Game));
            var adoption=startup.Run("services.unmanaged-adoption",()=>new UnmanagedAdoptionService(db,plannerSnapshots,hash,paths.ModsRoot,paths.Game));
            var previews=startup.Run("services.texture-preview",()=>new TexturePreviewService(Path.Combine(paths.NextStateRoot,"PreviewCache")));
            var visuals=startup.Run("services.mod-visuals",()=>new ModVisualService(db));
            var timeline=startup.Run("services.timeline",()=>new ChangeTimelineService(db));
            var backups=startup.Run("services.save-backups",()=>new SaveBackupService(db,paths.NextStateRoot,paths.Game));
            var lastGood=startup.Run("services.last-known-good",()=>new LastKnownGoodService(db));
            var categories=startup.Run("services.auto-category",()=>new AutoCategoryService(db,paths.Game,startup));
            var dependencies=startup.Run("services.dependency-doctor",()=>new DependencyDoctorService(db,paths.GameRoot,paths.Game));
            var duplicates=startup.Run("services.duplicate-cleanup",()=>new DuplicateCleanupService(db,paths.ModsArchiveRoot));
            var recipe=startup.Run("services.collection-recipes",()=>new CollectionRecipeService(db,paths.Game));
            var trust=startup.Run("services.mod-trust",()=>new ModTrustService(db));
            var issues=startup.Run("services.mod-issue-fallback",()=>new ModIssueFallbackService(db,timeline,trust));
            var updateDiff=startup.Run("services.update-diff",()=>new UpdateDiffService(db));
            var inspector=startup.Run("services.effective-inspector",()=>new EffectiveInspectorService(plannerSnapshots,planner));
            var presets=startup.Run("services.outfit-presets",()=>new OutfitPresetService());
            var gameImpact=startup.Run("services.game-update-impact",()=>new GameUpdateImpactService(plannerSnapshots));
            var importer=startup.Run("services.archive-import",()=>new ArchiveImportService(archive,catalog,paths.ModsRoot));
            var inbox=startup.Run("services.smart-inbox",()=>new SmartInboxService(db,archive,catalog,nexus,categories,paths.InboxRoot,paths.ModsRoot,startup));
            var launchGate=startup.Run("services.launch-health-gate",()=>new LaunchHealthGateService(plannerSnapshots,health,adoption,dependencies,planner,paths.Game));
            var automation=startup.Run("services.automation-coordinator",()=>new AutomationCoordinator(db,backups,lastGood,timeline,updateDiff,inbox,duplicates,categories,dependencies,launchGate,issues,adoption,paths.GameRoot,paths.Game,startup));
            var bisector=startup.Run("services.crash-bisector",()=>new CrashBisectorEngine());
            var installRoot=startup.Run("services.updater-install-root",UpdateClientService.GetInstallRoot);
            var buildIdentity=startup.Run("services.updater-build-identity",()=>UpdateBuildIdentity.Load(installRoot));
            var updater=startup.Run("services.updater",()=>new UpdateClientService(
                log:message=>logger.Information("Program updater: {UpdaterMessage}",message)));
            changeHints=startup.Run("services.file-change-hints",()=>new FileChangeHintService(paths.ModsRoot,paths.LiveModRoot));
            changeHints.HintsAvailable+=hint=>
            {
                if(hint.WatcherOverflowed)logger.Warning("FileSystemWatcher overflowed; authoritative reconciliation is required before the next deployment");
                else logger.Debug("Filesystem invalidation hints received for {Count} path(s)",hint.Paths.Count);
                _=telemetry.RecordSignalAsync(hint.WatcherOverflowed?"watcher.overflow":"watcher.hints",null,null,
                    new Dictionary<string,object?>{{"count",hint.Paths.Count},{"overflow",hint.WatcherOverflowed}});
            };

            Services=startup.Run("services.container",()=>new AppServices(paths,gameRegistry,db,plannerSnapshots,logger,telemetry,hash,blobs,scanner,catalog,planner,executor,guard,health,support,profiles,presentationReads,migrator,archive,changeHints,nexus,gameBuild,adoption,previews,visuals,
                timeline,backups,lastGood,categories,dependencies,duplicates,recipe,trust,issues,updateDiff,inspector,presets,gameImpact,importer,inbox,launchGate,automation,bisector,updater,buildIdentity,e.Args.ToArray()));

            splash.SetDetail(paths.Game.IsMonsterHunterWorld?"Validating/migrating legacy MHW state without touching nativePC…":"Validating the isolated game workspace…");
            var migration=paths.Game.IsMonsterHunterWorld
                ? await startup.RunAsync("startup.migration",async ct=>await telemetry.TrackAsync("startup.migration",async(_,innerCt)=>
                    await Task.Run(()=>migrator.MigrateIfNeededAsync(innerCt),innerCt),ct:ct))
                : new MigrationResult(false,true,"Legacy MHW migration is not applicable to this game.",string.Empty);
            startup.Info("startup.migration.result", $"Performed={migration.Performed}; Success={migration.Success}; Message={migration.Message}; Report={migration.ReportPath}");
            if(migration.Performed&&!migration.Success)
            {
                startup.Complete(false,"Legacy migration reported failure and startup stopped safely.");
                splash.Close();
                MessageBox.Show(migration.Message+"\n\n"+migration.ReportPath+"\n\nMASTER DEBUG LOG (send this file):\n"+UnifiedDebugLog.FilePath+"\n\nStartup diagnostics:\n"+startup.TextLogPath+"\n"+startup.JsonReportPath,"Migration stopped safely",MessageBoxButton.OK,MessageBoxImage.Error);
                Shutdown(-2);
                return;
            }

            splash.SetDetail(paths.Game.IsMonsterHunterWorld?"Refreshing the local catalog and armor index…":"Refreshing the local mod catalog…");
            await startup.RunAsync("startup.catalog.refresh",async ct=>await telemetry.TrackAsync("startup.catalog.refresh",async(_,innerCt)=>await catalog.RefreshFoldersAsync(innerCt),ct:ct));
            if(paths.Game.IsMonsterHunterWorld)
                await startup.RunAsync("startup.armor-index.import",async ct=>await telemetry.TrackAsync("startup.armor-index.import",async(_,innerCt)=>await ArmorCatalogLoader.ImportAsync(db,Path.Combine(AppContext.BaseDirectory,"data","Armor Database.csv"),innerCt),ct:ct));

            splash.SetDetail("Learning mod lineage and checking the game build…");
            await startup.RunAsync("startup.intelligence.nexus",async ct=>await telemetry.TrackAsync("startup.intelligence.nexus",async(_,innerCt)=>await nexus.RefreshAsync(innerCt),ct:ct));
            var buildResult=await startup.RunAsync("startup.intelligence.game-build",async ct=>await telemetry.TrackAsync("startup.intelligence.game-build",async(_,innerCt)=>await gameBuild.CheckAsync(innerCt),ct:ct));
            startup.Info("startup.intelligence.game-build.result",$"Changed={buildResult.Changed}");
            var impact=await startup.RunAsync("startup.intelligence.game-impact",ct=>gameImpact.BuildAsync(buildResult.Changed,ct));
            if(buildResult.Changed)await startup.RunAsync("startup.intelligence.timeline",ct=>timeline.RecordAsync("game.update",AutomationSeverity.Warning,impact.Message,impact,ct));

            splash.SetDetail("Checking for an interrupted transaction…");
            await startup.RunAsync("startup.recovery",async ct=>await telemetry.TrackAsync("startup.recovery",async(_,innerCt)=>await executor.RecoverIncompleteAsync(innerCt),ct:ct));

            splash.SetDetail("Running automatic inbox/category/dependency maintenance…");
            var maintenance=await startup.RunAsync("startup.automation",async ct=>await telemetry.TrackAsync("startup.automation",async(_,innerCt)=>await automation.RunStartupMaintenanceAsync(innerCt),ct:ct));
            logger.Information("Startup automation: {Summary}",maintenance.Summary);
            startup.Info("startup.automation.summary",maintenance.Summary);

            splash.SetDetail("Preparing the main window and loading indexed state…");
            var window=startup.Run("ui.main-window.construct",()=>new MainWindow());
            MainWindow=window;
            await startup.RunAsync("ui.main-window.initialize",_=>window.InitializeAsync());
            startup.Run("ui.main-window.show",window.Show);
            ShutdownMode=ShutdownMode.OnMainWindowClose;
            splash.Close();
            watchdog=startup.Run("services.dispatcher-watchdog",()=>new DispatcherWatchdog(window.Dispatcher,telemetry,logger));
            startup.Complete(true,"Main window initialized and displayed successfully.");
            try
            {
                await UpdateHealthProtocol.AcknowledgeIfRequestedAsync(
                    e.Args,
                    buildIdentity,
                    message=>logger.Information("Program updater: {UpdaterMessage}",message),
                    CancellationToken.None);
            }
            catch(Exception ex)
            {
                logger.Error(ex,"Program updater startup-health acknowledgement failed; normal application startup remains available.");
            }
            window.StartProgramUpdater();
        }
        catch(Exception ex)
        {
            startup.RecordFailure("startup.fatal",ex);
            startup.Complete(false,ex.Message);
            try{splash.Close();}catch{}
            MessageBox.Show(ex+"\n\nMASTER DEBUG LOG (send this file):\n"+UnifiedDebugLog.FilePath+"\n\nStartup diagnostics:\n"+startup.TextLogPath+"\n"+startup.JsonReportPath,"Universal Mod Manager startup failed",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static string ResolveDiagnosticRoot()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var explicitRoot=Environment.GetEnvironmentVariable("MOD_MANAGER_DEBUG_ROOT");
        if(string.IsNullOrWhiteSpace(explicitRoot))explicitRoot=Environment.GetEnvironmentVariable("MHW_MASTER_DEBUG_ROOT");
        if(!string.IsNullOrWhiteSpace(explicitRoot))return Path.GetFullPath(explicitRoot);

        var managerHome=Environment.GetEnvironmentVariable("MOD_MANAGER_HOME");
        if(string.IsNullOrWhiteSpace(managerHome))managerHome=Environment.GetEnvironmentVariable("MHW_MANAGER_HOME");
        if(!string.IsNullOrWhiteSpace(managerHome))return Path.GetFullPath(managerHome);

        var baseRoot=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        try
        {
            var versionDir=new DirectoryInfo(baseRoot);
            var releaseDir=versionDir.Parent;
            var projectRoot=releaseDir?.Parent;
            if(releaseDir is not null
               && projectRoot is not null
               && string.Equals(releaseDir.Name,"release",StringComparison.OrdinalIgnoreCase)
               && File.Exists(Path.Combine(projectRoot.FullName,"Build.bat")))
            {
                return projectRoot.FullName;
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            UnifiedDebugLog.Write("APP-BOOTSTRAP","Could not auto-resolve project-root master log; using application directory.",ex);
        }
        return baseRoot;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UnifiedDebugLog.Write("APP", $"OnExit invoked. ApplicationExitCode={e.ApplicationExitCode}");
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(disposed)return;
        UnifiedDebugLog.Write("APP", "Dispose begin");
        disposed=true;
        watchdog?.Dispose();
        watchdog=null;
        changeHints?.Dispose();
        changeHints=null;
        Services?.Updater.Dispose();
        if(Services?.Log is IDisposable disposable)disposable.Dispose();
        UnifiedDebugLog.Write("APP", "Dispose complete");
        GC.SuppressFinalize(this);
    }
}

public sealed record AppServices(
    AppPaths Paths,
    GameProfileRegistry GameRegistry,
    ManagerDatabase Database,
    PlannerSnapshotRepository PlannerSnapshots,
    ILogger Log,
    DiagnosticTelemetry Telemetry,
    HashingService Hashing,
    BlobStore Blobs,
    ModScanner Scanner,
    CatalogService Catalog,
    DeploymentPlanner Planner,
    DeploymentExecutor Executor,
    GameProcessGuard ProcessGuard,
    HealthService Health,
    SupportBundleService Support,
    ProfileRepository Profiles,
    PresentationReadRepository PresentationReads,
    LegacyV7Migrator Migrator,
    ArchiveInspector Archive,
    FileChangeHintService ChangeHints,
    NexusMetadataService Nexus,
    GameBuildMonitor GameBuild,
    UnmanagedAdoptionService Adoption,
    TexturePreviewService TexturePreviews,
    ModVisualService Visuals,
    ChangeTimelineService Timeline,
    SaveBackupService Backups,
    LastKnownGoodService LastGood,
    AutoCategoryService Categories,
    DependencyDoctorService Dependencies,
    DuplicateCleanupService Duplicates,
    CollectionRecipeService Recipe,
    ModTrustService Trust,
    ModIssueFallbackService Issues,
    UpdateDiffService UpdateDiff,
    EffectiveInspectorService Inspector,
    OutfitPresetService Presets,
    GameUpdateImpactService GameImpact,
    ArchiveImportService Importer,
    SmartInboxService Inbox,
    LaunchHealthGateService LaunchGate,
    AutomationCoordinator Automation,
    CrashBisectorEngine Bisector,
    UpdateClientService Updater,
    UpdateBuildIdentity BuildIdentity,
    IReadOnlyList<string> StartupArguments);

public sealed record AppPaths(
    string ToolRoot,
    string WorkspaceRoot,
    string ModsRoot,
    string InboxRoot,
    string ModsArchiveRoot,
    string StateRoot,
    string NextStateRoot,
    string DatabasePath,
    string BlobRoot,
    string GameRoot,
    GameProfile Game)
{
    public string ExecutablePath=>Game.ExecutablePath;
    public string LiveModRoot=>Game.LiveModRoot;

    public static AppPaths Discover()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var tool=Environment.GetEnvironmentVariable("MOD_MANAGER_HOME");
        if(string.IsNullOrWhiteSpace(tool))tool=Environment.GetEnvironmentVariable("MHW_MANAGER_HOME");
        if(string.IsNullOrWhiteSpace(tool))tool=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        tool=Path.GetFullPath(tool);
        var state=Path.Combine(tool,"State");
        var registry=new GameProfileRegistry(state);
        var active=registry.GetActive();

        if(active is null)
        {
            string? mhw=Environment.GetEnvironmentVariable("MOD_MANAGER_GAME_ROOT");
            if(string.IsNullOrWhiteSpace(mhw))mhw=Environment.GetEnvironmentVariable("MHW_GAME_ROOT");
            var legacy=Path.Combine(state,"V2","state.json");
            if(string.IsNullOrWhiteSpace(mhw)&&File.Exists(legacy))
            {
                try{using var doc=JsonDocument.Parse(File.ReadAllText(legacy));if(doc.RootElement.TryGetProperty("gameRoot",out var g))mhw=g.GetString();}catch(JsonException){}
            }
            mhw??=GameLocator.Find();
            if(!string.IsNullOrWhiteSpace(mhw)&&File.Exists(Path.Combine(mhw,"MonsterHunterWorld.exe")))
            {
                active=registry.EnsureMonsterHunterWorld(mhw);
                registry.SetActive(active.Id);
            }
            else
            {
                var dialog=new OpenFileDialog{Title="Select a game executable",Filter="Windows games (*.exe)|*.exe",CheckFileExists=true,Multiselect=false};
                if(dialog.ShowDialog()!=true)throw new DirectoryNotFoundException("No active game is configured. Select a game executable to create a generic profile.");
                active=registry.AddGenericFromExecutable(dialog.FileName);
            }
        }

        if(!Directory.Exists(active.GameRoot)||!File.Exists(active.ExecutablePath))
        {
            var repair=new OpenFileDialog
            {
                Title=$"Relink {active.DisplayName}",
                Filter="Windows games (*.exe)|*.exe",
                CheckFileExists=true,
                Multiselect=false
            };
            var choice=MessageBox.Show(
                $"The active game '{active.DisplayName}' appears to have moved.\n\nExpected: {active.ExecutablePath}\n\nChoose Yes to locate its executable and preserve this game's mod library/history, or No to stop safely.",
                "Game installation moved",MessageBoxButton.YesNo,MessageBoxImage.Warning);
            if(choice!=MessageBoxResult.Yes||repair.ShowDialog()!=true)
                throw new DirectoryNotFoundException($"The active game profile '{active.DisplayName}' is missing or moved. Expected executable: {active.ExecutablePath}.");
            active=registry.RepairFromExecutable(active,repair.FileName);
        }

        string workspace,mods,inbox,archive,next;
        if(active.IsMonsterHunterWorld)
        {
            workspace=tool;mods=Path.Combine(tool,"Mods");inbox=Path.Combine(tool,"Inbox");archive=Path.Combine(tool,"Mods Archive");next=Path.Combine(state,"Next");
        }
        else
        {
            if(!GameProfile.IsCanonicalId(active.Id))throw new InvalidDataException("The active game profile ID is not a canonical storage identifier.");
            var gamesRoot=Path.GetFullPath(Path.Combine(tool,"Games"));
            var stateGamesRoot=Path.GetFullPath(Path.Combine(state,"Games"));
            workspace=Path.GetFullPath(Path.Combine(gamesRoot,active.Id));
            next=Path.GetFullPath(Path.Combine(stateGamesRoot,active.Id,"Next"));
            var gamesPrefix=gamesRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var stateGamesPrefix=stateGamesRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!workspace.StartsWith(gamesPrefix,StringComparison.OrdinalIgnoreCase)||!next.StartsWith(stateGamesPrefix,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The active game profile workspace escaped the manager-owned game roots.");
            mods=Path.Combine(workspace,"Mods");inbox=Path.Combine(workspace,"Inbox");archive=Path.Combine(workspace,"Mods Archive");
        }
        Directory.CreateDirectory(mods);Directory.CreateDirectory(inbox);Directory.CreateDirectory(archive);Directory.CreateDirectory(next);
        return new(tool,workspace,mods,inbox,archive,state,next,Path.Combine(next,"manager.db"),Path.Combine(next,"Blobs"),active.GameRoot,active);
    }
}
