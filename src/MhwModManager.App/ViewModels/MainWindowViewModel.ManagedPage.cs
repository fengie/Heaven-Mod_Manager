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
    private long lastShelfScan = Stopwatch.GetTimestamp();
    private int shelfDirty;
    private bool managedReadDeferred;
    private bool managedVisitRequested;
    private Task managedPageRead = Task.CompletedTask;

    private Task pageVisits=Task.CompletedTask;
    private Task pageMetadata=Task.CompletedTask;
    private Task periodicMetadata=Task.CompletedTask;

    internal bool PageReadsInProgress
    {
        get
        {
            using var trace=MasterDebugLog.BeginMethod();
            return !pageVisits.IsCompleted || !managedPageRead.IsCompleted ||
                !automaticChangeWork.IsCompleted || !pageMetadata.IsCompleted || metadataGate.CurrentCount==0 || localInventoryScan is { IsCompleted:false };
        }
    }

    private void StartPageVisit(int tab)
    {
        using var trace=MasterDebugLog.BeginMethod();
        if(disposed || backgroundCts.IsCancellationRequested) return;
        if (tab == 9)
        {
            lastInventoryVisit=System.Diagnostics.Stopwatch.GetTimestamp();
            _ = RefreshLocalModInventoryAsync(backgroundCts.Token, fullVisit:true);
            return;
        }
        var visit=RefreshSelectedPageAsync(tab,backgroundCts.Token);
        pageVisits=pageVisits.IsCompleted ? visit : Task.WhenAll(pageVisits,visit);
    }

    internal async Task StopPageReadsAsync()
    {
        using var trace=MasterDebugLog.BeginMethod();
        if(disposed) return;
        backgroundCts.Cancel();
        // A visit can start a catalog or inventory read before completing.
        try { await pageVisits; }
        catch(OperationCanceledException) when(backgroundCts.IsCancellationRequested) { }
        finally
        {
            try { await Task.WhenAll(managedPageRead,pageMetadata,periodicMetadata,automaticChangeWork,localInventoryScan ?? Task.CompletedTask); }
            catch(OperationCanceledException) when(backgroundCts.IsCancellationRequested) { }
        }
    }

    private void OnManagedLibraryHint(FileChangeHint hint)
    {
        using var trace = MasterDebugLog.BeginMethod();
        bool UnderRoot(string path, string root)
        {
            using var scope = MasterDebugLog.BeginMethod();
            return !string.IsNullOrEmpty(root) &&
            (PathRules.Comparer.Equals(path,root) ||
             path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        }
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
                var previous=observedCatalog;
                var shelfRead=await Task.Run(async () =>
                {
                    // A shallow name check catches immediate copies even when parent times are preserved.
                    // A bounded full reconciliation recovers nested changes if watcher hints were lost.
                    var rescan=changed || Stopwatch.GetElapsedTime(lastShelfScan)>=TimeSpan.FromMinutes(5) ||
                        s.Catalog.TopLevelFoldersChanged();
                    if (rescan) await s.Catalog.RefreshFoldersAsync(ct);
                    var catalog=await s.Database.GetModsAsync(ct);
                    return (rescan,rowsChanged:!previous.SequenceEqual(catalog));
                },ct);
                if (shelfRead.rescan)
                    lastShelfScan=Stopwatch.GetTimestamp();
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
