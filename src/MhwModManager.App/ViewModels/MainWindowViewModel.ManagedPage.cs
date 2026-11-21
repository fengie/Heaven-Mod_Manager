using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dispatcher viewDispatcher = Dispatcher.CurrentDispatcher;
    private IReadOnlyList<ModDescriptor> observedCatalog = [];
    private DateTime shelfCheckedUtc = DateTime.UtcNow;
    private long lastShelfScan = Stopwatch.GetTimestamp();
    private int shelfDirty;
    private bool managedReadDeferred;
    private bool managedVisitRequested;
    private Task managedPageRead = Task.CompletedTask;

    private void OnManagedLibraryHint(FileChangeHint hint)
    {
        using var trace = MasterDebugLog.BeginMethod();
        bool UnderRoot(string path, string root) => !string.IsNullOrEmpty(root) &&
            (PathRules.Comparer.Equals(path,root) ||
             path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        if (!hint.WatcherOverflowed && !hint.Paths.Any(path =>
            UnderRoot(path,s.Paths.ModsRoot) || UnderRoot(path,s.Paths.LegacyModsRoot))) return;

        if (Interlocked.Exchange(ref shelfDirty,1)!=0 || viewDispatcher.HasShutdownStarted) return;
        viewDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!disposed && SelectedTab==1) _ = ReconcileManagedPageAsync(backgroundCts.Token);
        }));
    }

    private Task ReconcileManagedPageAsync(CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        if (disposed) return Task.CompletedTask;
        if (BusyVisibility==Visibility.Visible) managedReadDeferred=true;
        else if (managedPageRead.IsCompleted)
        {
            managedReadDeferred=false;
            managedPageRead=ReadManagedPageAsync(ct);
        }
        return managedPageRead;
    }

    private async Task ReadManagedPageAsync(CancellationToken ct)
    {
        using var trace = MasterDebugLog.BeginMethod();
        try
        {
            do
            {
                managedVisitRequested=false;
                var changed=Interlocked.Exchange(ref shelfDirty,0)!=0;
                var since=shelfCheckedUtc;
                var started=DateTime.UtcNow;
                var previous=observedCatalog;
                var shelfRead=await Task.Run(async () =>
                {
                    // Parent directory times cover a newly copied package before watcher debounce.
                    // A bounded full reconciliation recovers nested changes if watcher hints were lost.
                    var rescan=changed || Stopwatch.GetElapsedTime(lastShelfScan)>=TimeSpan.FromMinutes(5) ||
                        Directory.GetLastWriteTimeUtc(s.Paths.ModsRoot)>since ||
                        (!string.IsNullOrEmpty(s.Paths.LegacyModsRoot) && Directory.GetLastWriteTimeUtc(s.Paths.LegacyModsRoot)>since);
                    if (rescan) await s.Catalog.RefreshFoldersAsync(ct);
                    var catalog=await s.Database.GetModsAsync(ct);
                    return (rescan,rowsChanged:!previous.SequenceEqual(catalog));
                },ct);
                if (shelfRead.rescan)
                {
                    shelfCheckedUtc=started;
                    lastShelfScan=Stopwatch.GetTimestamp();
                }
                ct.ThrowIfCancellationRequested();
                if (SelectedTab!=1) return;
                if (BusyVisibility==Visibility.Visible)
                {
                    managedReadDeferred=true;
                    return;
                }
                if (shelfRead.rowsChanged || shelfRead.rescan)
                {
                    await ReloadMods(ct,preserveStagedChanges:true);
                    await RefreshAnalysis(ct);
                }
            }
            while ((managedVisitRequested || Volatile.Read(ref shelfDirty)!=0) && SelectedTab==1 && BusyVisibility!=Visibility.Visible);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) {}
        catch(Exception ex)
        {
            Interlocked.Exchange(ref shelfDirty,1);
            s.Log.Warning(ex,"Could not refresh the managed library. The next visit will retry.");
        }
    }
}
