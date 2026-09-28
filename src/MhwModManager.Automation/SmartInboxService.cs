using MhwModManager.Core;
using System.Globalization;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class SmartInboxService(ManagerDatabase db, ArchiveInspector archive, CatalogService catalog, NexusMetadataService nexus, AutoCategoryService categories, string inboxRoot, string modsRoot, StartupDiagnosticSession? startupDiagnostics = null)
{
    public async Task<InboxRunResult> ProcessAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("smart-inbox");
        Directory.CreateDirectory(inboxRoot); Directory.CreateDirectory(modsRoot);
        var entries = Directory.EnumerateFileSystemEntries(inboxRoot).Where(x => !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x), "Processed")).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        startupDiagnostics?.Info("startup.automation.inbox.entries", $"InboxRoot={inboxRoot}; Entries={entries.Length}");
        UnifiedDebugLog.Write("INBOX", $"Scan InboxRoot={inboxRoot}; Entries={entries.Length}");
        var results = new List<InboxItemResult>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                startupDiagnostics?.Info("startup.automation.inbox.item.begin", entry);
                UnifiedDebugLog.Write("INBOX", $"BEGIN item={entry}");
                var name = Directory.Exists(entry) ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry);
                var destination = Unique(Path.Combine(modsRoot, name));
                if (Directory.Exists(entry)) await CopyDirectoryAsync(entry, destination, ct);
                else
                {
                    var ext = Path.GetExtension(entry).ToLowerInvariant();
                    if (ext is not ".zip" and not ".7z" and not ".rar") { results.Add(new(entry, null, false, AutomationCategory.Unknown, "Unsupported inbox item; left untouched.")); continue; }
                    var info = await archive.InspectAsync(entry, ct);
                    if (info.HasSuspiciousPaths) { results.Add(new(entry, null, false, AutomationCategory.Unknown, "Unsafe archive path detected; left untouched.")); continue; }
                    await archive.ExtractSafelyAsync(entry, destination, modsRoot, ct); NormalizeWrapper(destination);
                }
                var category = categories.Classify(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(destination, x)));
                results.Add(new(entry, destination, true, category, "Imported automatically."));
                startupDiagnostics?.Info("startup.automation.inbox.item.imported", $"Source={entry}; Destination={destination}; Category={category}");
                UnifiedDebugLog.Write("INBOX", $"IMPORTED Source={entry}; Destination={destination}; Category={category}");
                MoveToProcessed(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                startupDiagnostics?.RecordFailure("startup.automation.inbox.item.recoverable-failure", ex, entry);
                UnifiedDebugLog.Write("INBOX", $"RECOVERABLE FAILURE item={entry}", ex);
                results.Add(new(entry, null, false, AutomationCategory.Unknown, ex.Message));
            }
        }
        if (results.Any(x => x.Imported))
        {
            await catalog.RefreshFoldersAsync(ct);
            var importedPaths=results.Where(x=>x.Imported&&!string.IsNullOrWhiteSpace(x.Destination)).Select(x=>x.Destination!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var importedMods=(await db.GetModsAsync(ct)).Where(m=>importedPaths.Contains(m.SourcePath)).ToArray();
            await catalog.EnsureCapturedAsync(importedMods,ct);
            await nexus.RefreshAsync(ct); await categories.AssignMissingAsync(ct);
        }
        return new(entries.Length, results.Count(x=>x.Imported), results.Count(x=>!x.Imported), results);
    }

    private void MoveToProcessed(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var processed = Path.Combine(inboxRoot, "Processed"); Directory.CreateDirectory(processed);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var dest = Unique(Path.Combine(processed, stamp + "-" + Path.GetFileName(source)));
        if (Directory.Exists(source)) Directory.Move(source, dest); else File.Move(source, dest);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested(); var dest = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024*1024, FileOptions.Asynchronous|FileOptions.SequentialScan);
            await using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024*1024, FileOptions.Asynchronous|FileOptions.WriteThrough);
            await input.CopyToAsync(output, 1024*1024, ct);
        }
    }
    private static void NormalizeWrapper(string root) {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dirs=Directory.GetDirectories(root); var files=Directory.GetFiles(root); if(files.Length==0&&dirs.Length==1){var child=dirs[0];foreach(var entry in Directory.GetFileSystemEntries(child)){var dest=Path.Combine(root,Path.GetFileName(entry));if(Directory.Exists(entry))Directory.Move(entry,dest);else File.Move(entry,dest);}Directory.Delete(child,true);} }
    private static string Unique(string path){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!Directory.Exists(path)&&!File.Exists(path))return path;for(var i=2;;i++){var p=path+" ("+i.ToString(CultureInfo.InvariantCulture)+")";if(!Directory.Exists(p)&&!File.Exists(p))return p;}}
}
