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
        foreach (var folder in AutoSortFolders)
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
        ScheduleAutoSortScan();
    }

    private void OnAutoSortWatcherError(object sender, ErrorEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        s.Log.Warning(e.GetException(), "Mod-intake filesystem watcher requires authoritative rescan.");
        ScheduleAutoSortScan();
    }

    private void ScheduleAutoSortScan()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed || backgroundCts.IsCancellationRequested || AutoSortFolders.Count == 0) return;
        CancellationTokenSource trigger;
        lock (intakeScheduleGate)
        {
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
            await ScanAutoSortAsync(trigger.Token);
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
