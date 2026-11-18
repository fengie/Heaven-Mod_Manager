using System.Diagnostics;
using MhwModManager.Core;
using System.Globalization;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public enum SmartInboxFaultPoint
{
    AfterPublishBeforeSourceArchive
}

public sealed record InboxRunProgress(int Completed, int Total, string CurrentItem, string Stage,
    int ExtractedFiles = 0, int TotalFiles = 0, long ExtractedBytes = 0, long TotalBytes = 0);

public sealed class SmartInboxService(ManagerDatabase db, ArchiveInspector archive, CatalogService catalog, NexusMetadataService nexus, AutoCategoryService categories, string inboxRoot, string modsRoot, StartupDiagnosticSession? startupDiagnostics = null, Action<SmartInboxFaultPoint,string>? faultInjector = null, string? legacyInboxRoot = null)
{
    public Task<InboxRunResult> ProcessAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ProcessAsync(null, ct);
    }

    public Task<InboxRunResult> ProcessAsync(IProgress<InboxRunProgress>? progress, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ProcessAsync(progress, refreshMetadata: true, ct);
    }

    /// <summary>
    /// During bootstrap, defer optional remote metadata lookup until the Mods page requests it.
    /// Local import, indexing, hash capture and category assignment remain mandatory.
    /// </summary>
    public async Task<InboxRunResult> ProcessAsync(IProgress<InboxRunProgress>? progress, bool refreshMetadata, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("smart-inbox");
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();
        var activePhase = "Enumerating Inbox";
        Directory.CreateDirectory(inboxRoot); Directory.CreateDirectory(modsRoot);
        var workspaceRoot = ImportPublicationWorkspace.RootFor(modsRoot);
        var entries = new[]{inboxRoot,legacyInboxRoot}
            .Where(root=>!string.IsNullOrWhiteSpace(root)&&Directory.Exists(root))
            .Select(root=>Path.GetFullPath(root!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(root=>Directory.EnumerateFileSystemEntries(root))
            .Where(x=>!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x),"Processed"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        startupDiagnostics?.Info("startup.automation.inbox.entries", $"InboxRoot={inboxRoot}; Entries={entries.Length}");
        UnifiedDebugLog.Write("INBOX", $"Scan InboxRoot={inboxRoot}; Entries={entries.Length}");
        var completed = 0;
        void ReportStage(int processed, string stage, string? item = null)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            UnifiedDebugLog.Write("INBOX-PHASE",
                $"phase={activePhase}; elapsedMs={phaseTimer.Elapsed.TotalMilliseconds:F1}; completed={completed}/{entries.Length}");
            activePhase = stage;
            phaseTimer.Restart();
            progress?.Report(new InboxRunProgress(processed, entries.Length,
                item is null ? string.Empty : Path.GetFileName(item), stage));
        }
        ReportStage(0, "Preparing packages");
        var results = new List<InboxItemResult>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var itemTimer = Stopwatch.StartNew();
            ReportStage(completed, "Preparing package", entry);
            string? staging = null;
            string? publishedDestination = null;
            try
            {
                startupDiagnostics?.Info("startup.automation.inbox.item.begin", entry);
                UnifiedDebugLog.Write("INBOX", $"BEGIN item={entry}");
                var name = Directory.Exists(entry) ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry);
                var destination = Unique(Path.Combine(modsRoot, name));
                if (Directory.Exists(entry))
                {
                    ReportStage(completed, "Copying folder", entry);
                    staging = ImportPublicationWorkspace.Allocate(modsRoot, "inbox");
                    var copyProgress = progress is null ? null : new ImmediateProgress<ArchiveExtractionProgress>(p =>
                        progress.Report(new InboxRunProgress(completed, entries.Length, Path.GetFileName(entry),
                            "Copying folder", p.CompletedFiles, p.TotalFiles, p.CompletedBytes, p.TotalBytes)));
                    await CopyDirectoryAsync(entry, staging, ct, copyProgress);
                }
                else
                {
                    var ext = Path.GetExtension(entry).ToLowerInvariant();
                    if (ext is not ".zip" and not ".7z" and not ".rar") { results.Add(new(entry, null, false, AutomationCategory.Unknown, "Unsupported inbox item; left untouched.")); continue; }
                    ReportStage(completed, "Inspecting archive", entry);
                    var info = await archive.InspectAsync(entry, ct);
                    if (info.HasSuspiciousPaths) { results.Add(new(entry, null, false, AutomationCategory.Unknown, "Unsafe archive path detected; left untouched.")); continue; }
                    ReportStage(completed, "Extracting archive", entry);
                    staging = ImportPublicationWorkspace.Allocate(modsRoot, "inbox");
                    var extractProgress = progress is null ? null : new ImmediateProgress<ArchiveExtractionProgress>(p =>
                        progress.Report(new InboxRunProgress(completed, entries.Length, Path.GetFileName(entry),
                            "Extracting archive", p.CompletedFiles, p.TotalFiles, p.CompletedBytes, p.TotalBytes)));
                    await archive.ExtractSafelyAsync(entry, staging, workspaceRoot,
                        new ArchiveExtractionLimits(ArchiveInspector.MaxExpandedBytes, ArchiveInspector.MaxExpandedBytes),
                        extractProgress, ct);
                    ReportStage(completed, "Normalizing package", entry);
                    NormalizeWrapper(staging);
                }
                if (FomodInstallerService.HasInstaller(staging))
                {
                    results.Add(new(entry, null, false, AutomationCategory.Unknown,
                        "Installer choices required. Use Import archive and select this Inbox archive; source left untouched."));
                    continue;
                }
                ReportStage(completed, "Classifying files", entry);
                var category = categories.Classify(SafeRecursiveTraversal.Snapshot(staging, ct).Files.Select(x => Path.GetRelativePath(staging, x)));
                ReportStage(completed, "Publishing package", entry);
                ImportPublicationWorkspace.Publish(modsRoot, staging, destination);
                staging = null;
                publishedDestination = destination;
                faultInjector?.Invoke(SmartInboxFaultPoint.AfterPublishBeforeSourceArchive, destination);
                ReportStage(completed, "Archiving original in Inbox", entry);
                MoveToProcessed(entry);
                publishedDestination = null;
                results.Add(new(entry, destination, true, category, "Imported automatically."));
                startupDiagnostics?.Info("startup.automation.inbox.item.imported", $"Source={entry}; Destination={destination}; Category={category}");
                UnifiedDebugLog.Write("INBOX", $"IMPORTED Source={entry}; Destination={destination}; Category={category}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                var rollbackFailure = ImportPublicationWorkspace.RollbackPublished(modsRoot, publishedDestination);
                if (rollbackFailure is not null)
                    throw new IOException(
                        $"Smart Inbox could not archive source '{entry}' and rollback of published package '{publishedDestination}' also failed.",
                        new AggregateException(ex, rollbackFailure));
                ct.ThrowIfCancellationRequested();
                startupDiagnostics?.RecordFailure("startup.automation.inbox.item.recoverable-failure", ex, entry);
                UnifiedDebugLog.Write("INBOX", $"RECOVERABLE FAILURE item={entry}", ex);
                results.Add(new(entry, null, false, AutomationCategory.Unknown, ex.Message));
            }
            catch (OperationCanceledException ex)
            {
                var rollbackFailure = ImportPublicationWorkspace.RollbackPublished(modsRoot, publishedDestination);
                if (rollbackFailure is not null)
                    throw new OperationCanceledException(
                        $"Smart Inbox cancellation could not roll back published package '{publishedDestination}'.",
                        new AggregateException(ex, rollbackFailure),
                        ex.CancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                var rollbackFailure = ImportPublicationWorkspace.RollbackPublished(modsRoot, publishedDestination);
                if (rollbackFailure is not null)
                    throw new IOException(
                        $"Smart Inbox failed and rollback of published package '{publishedDestination}' also failed.",
                        new AggregateException(ex, rollbackFailure));
                throw;
            }
            finally
            {
                ImportPublicationWorkspace.Cleanup(modsRoot, staging);
                if (results.Count > completed)
                {
                    completed++;
                    ReportStage(completed, "Finished item", entry);
                    UnifiedDebugLog.Write("INBOX-ITEM",
                        $"item={entry}; outcome={(results[^1].Imported ? "imported" : "skipped")}; " +
                        $"elapsedMs={itemTimer.Elapsed.TotalMilliseconds:F1}; completed={completed}/{entries.Length}");
                }
            }
        }
        if (results.Any(x => x.Imported))
        {
            ReportStage(completed, "Indexing catalog");
            await catalog.RefreshFoldersAsync(ct);
            var importedPaths=results.Where(x=>x.Imported&&!string.IsNullOrWhiteSpace(x.Destination)).Select(x=>x.Destination!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ReportStage(completed, "Capturing imported mods");
            var importedMods=(await db.GetModsAsync(ct)).Where(m=>importedPaths.Contains(m.SourcePath)).ToArray();
            await catalog.EnsureCapturedAsync(importedMods,ct);
            if (refreshMetadata)
            {
                ReportStage(completed, "Refreshing metadata");
                await nexus.RefreshAsync(ct);
            }
            else
            {
                UnifiedDebugLog.Write("INBOX-PHASE", "Optional network metadata refresh deferred until requested by the Mods page.");
            }
            ReportStage(completed, "Assigning categories");
            await categories.AssignMissingAsync(ct);
        }
        ReportStage(completed, "Finished");
        UnifiedDebugLog.Write("INBOX", $"FINISHED elapsedMs={totalTimer.Elapsed.TotalMilliseconds:F1}; " +
            $"total={entries.Length}; imported={results.Count(x=>x.Imported)}; skipped={results.Count(x=>!x.Imported)}");
        return new(entries.Length, results.Count(x=>x.Imported), results.Count(x=>!x.Imported), results);
    }

    private void MoveToProcessed(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Archive the source in the inbox it came from; never relocate old user-owned
        // inbox data just because new installs use the game-scoped library.
        var sourceRoot=Path.GetDirectoryName(Path.GetFullPath(source))
            ?? throw new InvalidDataException("Inbox entry has no owning directory.");
        if(!StringComparer.OrdinalIgnoreCase.Equals(sourceRoot,Path.GetFullPath(inboxRoot))&&
           (string.IsNullOrWhiteSpace(legacyInboxRoot)||
            !StringComparer.OrdinalIgnoreCase.Equals(sourceRoot,Path.GetFullPath(legacyInboxRoot))))
            throw new InvalidDataException("Inbox source escaped the configured game inbox roots.");
        var processed = Path.Combine(sourceRoot, "Processed"); Directory.CreateDirectory(processed);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var dest = Unique(Path.Combine(processed, stamp + "-" + Path.GetFileName(source)));
        if (Directory.Exists(source)) Directory.Move(source, dest); else File.Move(source, dest);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken ct,
        IProgress<ArchiveExtractionProgress>? progress = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snapshot = SafeRecursiveTraversal.Snapshot(source, ct);
        Directory.CreateDirectory(destination);
        foreach (var dir in snapshot.Directories) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        var copied = 0;
        var progressTimer = Stopwatch.StartNew();
        progress?.Report(new ArchiveExtractionProgress(0, snapshot.Files.Count, 0, 0));
        foreach (var file in snapshot.Files)
        {
            ct.ThrowIfCancellationRequested(); var dest = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024*1024, FileOptions.Asynchronous|FileOptions.SequentialScan);
            await using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024*1024, FileOptions.Asynchronous|FileOptions.WriteThrough);
            await input.CopyToAsync(output, 1024*1024, ct);
            copied++;
            if(progress is not null && (copied == snapshot.Files.Count || progressTimer.ElapsedMilliseconds >= 250))
            {
                progress.Report(new ArchiveExtractionProgress(copied, snapshot.Files.Count, 0, 0));
                progressTimer.Restart();
            }
        }
    }
    private sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            callback(value);
        }
    }

    private static void NormalizeWrapper(string root) {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dirs=Directory.GetDirectories(root); var files=Directory.GetFiles(root); if(files.Length==0&&dirs.Length==1){var child=dirs[0];foreach(var entry in Directory.GetFileSystemEntries(child)){var dest=Path.Combine(root,Path.GetFileName(entry));if(Directory.Exists(entry))Directory.Move(entry,dest);else File.Move(entry,dest);}Directory.Delete(child,true);} }
    private static string Unique(string path){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!Directory.Exists(path)&&!File.Exists(path))return path;for(var i=2;;i++){var p=path+" ("+i.ToString(CultureInfo.InvariantCulture)+")";if(!Directory.Exists(p)&&!File.Exists(p))return p;}}
}
