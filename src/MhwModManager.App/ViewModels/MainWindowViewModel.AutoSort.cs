using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private GameModIntakeService? gameIntake;
    private readonly List<FileSystemWatcher> intakeWatchers = [];
    private readonly SemaphoreSlim intakeScanGate = new(1, 1);
    private readonly object intakeScheduleGate = new();
    private CancellationTokenSource? intakeScheduleCts;
    private bool autoSortStarted;
    private bool scanSourcesRequested;
    private bool inboxWaitingForForeground;

    private IEnumerable<string> ActiveInboxRoots
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new[] { s.Paths.InboxRoot, s.Paths.LegacyInboxRoot }
        .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    public ObservableRangeCollection<string> AutoSortFolders { get; } = [];
    [ObservableProperty] private string? selectedAutoSortFolder;
    [ObservableProperty] private string autoSortStatusText =
        "Choose a downloads/mods folder once; new packages will be matched to registered games automatically.";

    /// <summary>
    /// Called only after the initial library is rendered. No foreign-folder scans or
    /// file watching run on the critical application startup/splash path.
    /// </summary>
    public void StartAutoSorting()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (autoSortStarted || disposed) return;
        autoSortStarted = true;
        gameIntake = new GameModIntakeService(s.Paths.ToolRoot, s.Paths.StateRoot, s.GameRegistry.Load, s.Archive);
        try
        {
            RefreshAutoSortFolders();
            RefreshAutoSortWatchers();
            ScheduleAutoSortScan();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            s.Log.Warning(ex, "Automatic game mod intake configuration could not start.");
            AutoSortStatusText = "Auto-sort configuration could not be loaded: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddAutoSortFolder()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var picker = new OpenFolderDialog
        {
            Title = "Choose a folder containing downloaded or unpacked mods"
        };
        if (picker.ShowDialog() != true) return;
        try
        {
            EnsureGameIntake();
            gameIntake!.AddSource(picker.FolderName);
            RefreshAutoSortFolders();
            RefreshAutoSortWatchers();
            await ScanAutoSortAsync(backgroundCts.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            AutoSortStatusText = "Cannot watch this folder: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportModFolder()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var picker = new OpenFolderDialog
        {
            Title = "Select a folder containing ZIP/RAR/7z mod archives or unpacked mods"
        };
        if (picker.ShowDialog() == true)
            await ImportModPathsAsync([picker.FolderName]);
    }

    /// <summary>
    /// Shared entry for folder picker, multi-file picker, and Windows Explorer drop.
    /// All sources remain untouched; high-confidence mods are staged per registered game.
    /// </summary>
    public async Task ImportModPathsAsync(IEnumerable<string> inputPaths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(inputPaths);
        var paths = inputPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0 || disposed) return;

        GameModIntakeScan? report = null;
        GameModIntakeService? sorter = null;
        await RunBusy("mod-intake.batch", "Importing Mod Collection",
            "Checking selected archives and folders; originals will not be changed…",
            true, async ct =>
            {
                BusyDetail = "Waiting for other mod scans to finish…";
                await intakeScanGate.WaitAsync(ct);
                try
                {
                    EnsureGameIntake();
                    sorter = gameIntake!;
                    var updates = new Progress<GameModIntakeProgress>(update =>
                {
                    if (BusyVisibility != Visibility.Visible) return;
                    var shortName = Path.GetFileName(update.CurrentPath
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    BusyDetail = $"{update.Stage} — {update.Completed}/{update.Total}: {shortName}";
                });
                report = await Task.Run(() => sorter.ImportPathsAsync(paths, updates, ct), ct);
                var completion = await FinishManualModIntakeAsync(report, ct);
                AutoSortStatusText = $"Bulk import: {completion} " +
                    $"{report.AlreadyPresent} already present, {report.NeedsReview} need review. " +
                    $"Details: {sorter.ReportPath}";
                StatusText = $"{completion} {report.AlreadyPresent} already present; " +
                    $"{report.NeedsReview} need game assignment or other manual review.";
                }
                finally
                {
                    intakeScanGate.Release();
                }
            });

        if (report is not null && report.NeedsReview > 0 && sorter is not null && !disposed)
        {
            var review = new ModIntakeReviewWindow(report)
            {
                Owner = Application.Current.MainWindow
            };
            review.EnableManualAssignment(s.GameRegistry.Load(), (candidate, gameId) =>
                Task.Run(() => sorter.AssignReviewedPackageAsync(candidate.SourcePath, gameId,
                    backgroundCts.Token), backgroundCts.Token));
            review.ShowDialog();
        }
    }

    /// <summary>
    /// A manual multi-archive/folder or multi-mod-pack import must finish the
    /// currently selected game's already-safe Inbox pipeline. Other games remain
    /// staged in their own Inboxes; never write to a foreign session's database.
    /// This also preserves the explicit review boundary for ambiguous packages.
    /// </summary>
    private async Task<string> FinishManualModIntakeAsync(GameModIntakeScan report, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(report);
        ct.ThrowIfCancellationRequested();
        var activeId = s.Paths.Game.Id;
        var activeRouted = report.Items.Count(item =>
            item.Status == GameModIntakeStatus.Routed &&
            StringComparer.OrdinalIgnoreCase.Equals(item.GameId, activeId));
        var queuedForOtherGames = report.Routed - activeRouted;
        if (activeRouted == 0)
        {
            MasterDebugLog.Write("MOD-INTAKE-UI",
                $"No active-game package to install. activeGame={activeId}; queuedOtherGames={queuedForOtherGames}; review={report.NeedsReview}");
            return queuedForOtherGames > 0
                ? $"Queued {queuedForOtherGames} package(s) for other registered games; they are not yet installed."
                : "No newly approved packages were routed.";
        }

        BusyDetail = $"Installing {activeRouted} approved package(s) into {s.Paths.Game.DisplayName}…";
        var inbox = await ProcessInboxWithProgressAsync(ct);
        var archived = 0;
        if (inbox.Imported > 0)
        {
            BusyDetail = "Checking safe duplicates in this game's library…";
            BusyItemProgressVisibility = Visibility.Collapsed;
            BusyProgressIndeterminate = true;
            archived = await Task.Run(() => s.Duplicates.ArchiveSafeAsync(ct), ct);
            BusyDetail = "Refreshing visible installed mods…";
            await ReloadMods(ct);
            BusyDetail = "Recomputing compatibility after import…";
            await RefreshAnalysis(ct);
        }
        MasterDebugLog.Write("MOD-INTAKE-UI",
            $"Manual import completed. activeGame={activeId}; activeRouted={activeRouted}; " +
            $"inboxImported={inbox.Imported}; inboxSkipped={inbox.Skipped}; duplicatesArchived={archived}; queuedOtherGames={queuedForOtherGames}; review={report.NeedsReview}");
        return $"Installed {inbox.Imported} package(s) into {s.Paths.Game.DisplayName}; " +
            $"{inbox.Skipped} Inbox package(s) skipped, {archived} safe duplicate(s) archived. " +
            $"{queuedForOtherGames} package(s) queued for other registered games.";
    }

    [RelayCommand]
    private void RemoveAutoSortFolder()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(SelectedAutoSortFolder)) return;
        try
        {
            EnsureGameIntake();
            gameIntake!.RemoveSource(SelectedAutoSortFolder);
            RefreshAutoSortFolders();
            RefreshAutoSortWatchers();
            AutoSortStatusText = "Removed the folder from monitoring. Existing source and sorted packages were not deleted.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AutoSortStatusText = "Could not remove monitored folder: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ScanAutoSortFolders()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureGameIntake();
        await ScanAutoSortAsync(backgroundCts.Token);
    }

    private void EnsureGameIntake()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        gameIntake ??= new GameModIntakeService(s.Paths.ToolRoot, s.Paths.StateRoot, s.GameRegistry.Load, s.Archive);
    }

    private void RefreshAutoSortFolders()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EnsureGameIntake();
        var folders = gameIntake!.LoadSources();
        AutoSortFolders.Clear();
        foreach (var path in folders) AutoSortFolders.Add(path);
        SelectedAutoSortFolder = folders.Count == 0 ? null : folders[0];
        if (folders.Count == 0)
            AutoSortStatusText = "No folders monitored. Add a source once to auto-sort future downloads.";
        else
            AutoSortStatusText = $"Monitoring {folders.Count} source folder(s). Unknown/ambiguous packages stay in place for review.";
    }

    private void RefreshAutoSortWatchers()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var watcher in intakeWatchers) watcher.Dispose();
        intakeWatchers.Clear();
        foreach (var folder in ActiveInboxRoots.Concat(AutoSortFolders).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                        | NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = false
                };
                watcher.Created += OnAutoSortFilesystemChange;
                watcher.Changed += OnAutoSortFilesystemChange;
                watcher.Renamed += OnAutoSortFilesystemChange;
                watcher.Error += OnAutoSortWatcherError;
                watcher.EnableRaisingEvents = true;
                intakeWatchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                s.Log.Warning(ex, "Could not monitor designated intake folder {Folder}", folder);
                AutoSortStatusText = "One monitored folder is unavailable. Use Scan Folders after reconnecting it.";
            }
        }
    }

    private void OnAutoSortFilesystemChange(object sender, FileSystemEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var inbox = sender is FileSystemWatcher watcher && ActiveInboxRoots.Contains(watcher.Path, StringComparer.OrdinalIgnoreCase);
        if (inbox && e.Name is { } name && (name.Equals("Processed", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Processed" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return;
        ScheduleAutoSortScan(scanSources: !inbox);
    }

    private void OnAutoSortWatcherError(object sender, ErrorEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        s.Log.Warning(e.GetException(), "Mod-intake filesystem watcher requires authoritative rescan.");
        ScheduleAutoSortScan(scanSources: sender is not FileSystemWatcher watcher ||
            !ActiveInboxRoots.Contains(watcher.Path, StringComparer.OrdinalIgnoreCase));
    }

    private void ScheduleAutoSortScan(bool scanSources = true)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed || backgroundCts.IsCancellationRequested) return;
        CancellationTokenSource trigger;
        lock (intakeScheduleGate)
        {
            scanSourcesRequested |= scanSources;
            intakeScheduleCts?.Cancel();
            trigger = CancellationTokenSource.CreateLinkedTokenSource(backgroundCts.Token);
            intakeScheduleCts = trigger;
        }
        _ = RunDelayedAutoSortScanAsync(trigger);
    }

    private async Task RunDelayedAutoSortScanAsync(CancellationTokenSource trigger)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            // FileSystemWatcher fires repeatedly while browsers download archives.
            // Debounce until writers go quiet rather than copying partial packages.
            await Task.Delay(TimeSpan.FromSeconds(4), trigger.Token);
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                bool scanSources;
                lock (intakeScheduleGate)
                {
                    if (trigger.IsCancellationRequested || disposed) return;
                    scanSources = scanSourcesRequested;
                    scanSourcesRequested = false;
                }
                // New file events debounce future work without cancelling a publication already in progress.
                var ct = backgroundCts.Token;
                if (scanSources && AutoSortFolders.Count > 0)
                    await ScanAutoSortAsync(ct);
                await DrainActiveInboxAsync(ct);
            }).Task.Unwrap();
        }
        catch (OperationCanceledException) when (trigger.IsCancellationRequested) { }
        catch (Exception ex)
        {
            s.Log.Warning(ex, "Automatic mod intake background scan failed.");
            if (!disposed)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    AutoSortStatusText = "Auto-sort scan failed: " + ex.Message);
            }
        }
        finally
        {
            lock (intakeScheduleGate)
            {
                if (ReferenceEquals(intakeScheduleCts, trigger)) intakeScheduleCts = null;
            }
            trigger.Dispose();
        }
    }

    partial void OnBusyVisibilityChanged(Visibility value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // A drop during Preview, Apply or another foreground operation remains in the durable Inbox.
        if (value == Visibility.Collapsed && inboxWaitingForForeground && !disposed)
        {
            inboxWaitingForForeground = false;
            ScheduleAutoSortScan(scanSources: false);
        }
    }

    private async Task DrainActiveInboxAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed || ct.IsCancellationRequested) return;
        if (BusyVisibility == Visibility.Visible)
        {
            inboxWaitingForForeground = true;
            return;
        }
        var pending = await Task.Run(s.Inbox.HasPendingPackages, ct);
        if (!pending || disposed || ct.IsCancellationRequested) return;

        bool started = false;
        await RunBusy("automation.inbox.auto", "Importing Inbox mods", "Reading packages for the current game…", true, async operationToken =>
        {
            started = true;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, operationToken);
            var result = await Task.Run(() => s.Inbox.ProcessAsync(null, refreshMetadata: false, lifetime.Token), lifetime.Token);
            if (result.Imported > 0)
            {
                await ReloadMods(lifetime.Token, preserveStagedChanges: true);
                await RefreshAnalysis(lifetime.Token);
            }
            StatusText = $"Inbox: {result.Imported} installed, {result.Skipped} skipped.";
        });
        if (!started && BusyVisibility == Visibility.Visible) inboxWaitingForForeground = true;
    }

    private async Task ScanAutoSortAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed) return;
        EnsureGameIntake();
        await intakeScanGate.WaitAsync(ct);
        try
        {
            var sorter = gameIntake!;
            var report = await Task.Run(() => sorter.ScanAsync(ct), ct);
            if (disposed || ct.IsCancellationRequested) return;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AutoSortStatusText =
                    $"Game auto-sort: {report.Routed} newly routed, {report.AlreadyPresent} already sorted, " +
                    $"{report.NeedsReview} need review, {report.Skipped} skipped. " +
                    $"Review details: {sorter.ReportPath}";
                if (report.Routed > 0)
                    StatusText = $"Sorted {report.Routed} mod package(s) into their registered games' Inboxes. " +
                        "Packages are imported safely when their game is active.";
            });
        }
        finally
        {
            intakeScanGate.Release();
        }
    }

    private void DisposeAutoSorting()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        lock (intakeScheduleGate)
        {
            intakeScheduleCts?.Cancel();
            intakeScheduleCts = null;
        }
        foreach (var watcher in intakeWatchers) watcher.Dispose();
        intakeWatchers.Clear();
    }
}
