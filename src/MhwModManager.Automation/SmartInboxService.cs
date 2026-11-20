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
    public Task<IReadOnlySet<string>> RecoverInterruptedPublicationsAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new InboxPublicationJournalService(inboxRoot, modsRoot, legacyInboxRoot).RecoverAsync(ct);
    }

    private IEnumerable<string> EnumerateInboxEntries()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new[]{inboxRoot,legacyInboxRoot}
            .Where(root=>!string.IsNullOrWhiteSpace(root)&&Directory.Exists(root))
            .Select(root=>Path.GetFullPath(root!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(root=>Directory.EnumerateFileSystemEntries(root))
            .Where(x=>!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x),"Processed"));
    }

    private static bool IsInboxArchive(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".7z" or ".rar";
    }

    public bool HasPendingPackages()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return EnumerateInboxEntries().Any(entry =>
        (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0 &&
        (Directory.Exists(entry) ? Directory.EnumerateFileSystemEntries(entry).Any() : IsInboxArchive(entry)));
    }

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
    public Task<InboxRunResult> ProcessAsync(IProgress<InboxRunProgress>? progress, bool refreshMetadata, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ProcessAsync(progress, refreshMetadata, shouldStopAfterCurrentItem: null, ct);
    }

    /// <summary>
    /// A user may defer the remaining automatic Inbox queue only at a safe package
    /// boundary. Previously published packages still complete catalog indexing
    /// and authoritative hash capture before returning.
    /// </summary>
    public async Task<InboxRunResult> ProcessAsync(
        IProgress<InboxRunProgress>? progress,
        bool refreshMetadata,
        Func<bool>? shouldStopAfterCurrentItem,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("smart-inbox");
        var totalTimer = Stopwatch.StartNew();
        var phaseTimer = Stopwatch.StartNew();
        var activePhase = "Enumerating Inbox";
        Directory.CreateDirectory(inboxRoot); Directory.CreateDirectory(modsRoot);
        var journal = new InboxPublicationJournalService(inboxRoot, modsRoot, legacyInboxRoot);
        var blockedSources = await journal.RecoverAsync(ct);
        var workspaceRoot = ImportPublicationWorkspace.RootFor(modsRoot);
        var entries = EnumerateInboxEntries().Order(StringComparer.OrdinalIgnoreCase).ToArray();
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
            if (shouldStopAfterCurrentItem?.Invoke() == true)
            {
                ReportStage(completed, "Deferred remaining packages");
                UnifiedDebugLog.Write("INBOX",
                    $"Deferred remaining Inbox packages after {completed}/{entries.Length} safe item(s); completing indexing for published items.");
                break;
            }
            var itemTimer = Stopwatch.StartNew();
            ReportStage(completed, "Preparing package", entry);
            string? staging = null;
            string? publishedDestination = null;
            string? journalPath = null;
            string? intendedDestination = null;
            string? intendedProcessed = null;
            try
            {
                if (blockedSources.Contains(entry))
                {
                    const string reason = "Interrupted Inbox publication needs manual review; both copies were preserved.";
                    startupDiagnostics?.Info("startup.automation.inbox.item.pending-review", entry);
                    UnifiedDebugLog.Write("INBOX-RECOVERY", $"SKIP item={entry}; {reason}");
                    results.Add(new(entry, null, false, AutomationCategory.Unknown, reason));
                    continue;
                }
                startupDiagnostics?.Info("startup.automation.inbox.item.begin", entry);
                UnifiedDebugLog.Write("INBOX", $"BEGIN item={entry}");
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Inbox links must be reviewed at their source.");
                var name = ModDisplayNames.StripTransportDigest(Directory.Exists(entry) ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry));
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
                    if (!IsInboxArchive(entry)) { results.Add(new(entry, null, false, AutomationCategory.Unknown, "Unsupported inbox item; left untouched.")); continue; }
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
                    catalog.NormalizeStagedArchive(staging);
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
                var processedDestination = PlanProcessedDestination(entry);
                journalPath = await journal.BeginAsync(entry, destination, processedDestination, ct);
                intendedDestination = destination;
                intendedProcessed = processedDestination;
                ImportPublicationWorkspace.Publish(modsRoot, staging, destination);
                staging = null;
                publishedDestination = destination;
                faultInjector?.Invoke(SmartInboxFaultPoint.AfterPublishBeforeSourceArchive, destination);
                ReportStage(completed, "Archiving original in Inbox", entry);
                MoveToProcessed(entry, processedDestination);
                publishedDestination = null;
                // Intent cleanup is best-effort after both copies reach their durable
                // destinations; restart recovery safely completes it if interrupted.
                try { journal.Complete(journalPath); }
                catch (IOException ex) { UnifiedDebugLog.Write("INBOX-RECOVERY", "Post-publication intent cleanup deferred.", ex); }
                catch (UnauthorizedAccessException ex) { UnifiedDebugLog.Write("INBOX-RECOVERY", "Post-publication intent cleanup deferred.", ex); }
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
                journal.CompleteIfRolledBack(journalPath, entry, intendedDestination, intendedProcessed);
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
                journal.CompleteIfRolledBack(journalPath, entry, intendedDestination, intendedProcessed);
                throw;
            }
            catch (Exception ex)
            {
                var rollbackFailure = ImportPublicationWorkspace.RollbackPublished(modsRoot, publishedDestination);
                if (rollbackFailure is not null)
                    throw new IOException(
                        $"Smart Inbox failed and rollback of published package '{publishedDestination}' also failed.",
                        new AggregateException(ex, rollbackFailure));
                journal.CompleteIfRolledBack(journalPath, entry, intendedDestination, intendedProcessed);
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

    private string PlanProcessedDestination(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Keep originals in their owning Inbox, including legacy Inbox paths.
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(source))
            ?? throw new InvalidDataException("Inbox entry has no owning directory.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(sourceRoot, Path.GetFullPath(inboxRoot)) &&
            (string.IsNullOrWhiteSpace(legacyInboxRoot) ||
             !StringComparer.OrdinalIgnoreCase.Equals(sourceRoot, Path.GetFullPath(legacyInboxRoot))))
            throw new InvalidDataException("Inbox source escaped the configured game inbox roots.");
        var processed = Path.Combine(sourceRoot, "Processed");
        // Do not create Processed until after publication. The existing rollback
        // contract must still exercise a failed destination-directory creation.
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Unique(Path.Combine(processed, stamp + "-" + Path.GetFileName(source)));
    }

    private static void MoveToProcessed(string source, string destination)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var processedRoot = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Processed Inbox destination has no parent.");
        Directory.CreateDirectory(processedRoot);
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken ct,
        IProgress<ArchiveExtractionProgress>? progress = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snapshot = SafeRecursiveTraversal.Snapshot(source, ct);
        if (snapshot.Files.Count == 0)
            throw new InvalidDataException("Inbox folder has no files to import.");
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

    private static string Unique(string path){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!Directory.Exists(path)&&!File.Exists(path))return path;for(var i=2;;i++){var p=path+" ("+i.ToString(CultureInfo.InvariantCulture)+")";if(!Directory.Exists(p)&&!File.Exists(p))return p;}}
}
