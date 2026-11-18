using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Automation;

/// <summary>
/// A durable intent for the gap between publishing an Inbox package and archiving
/// its original. Recovery never guesses that two existing directories are identical:
/// ambiguous published/import sources remain in place for manual review.
/// </summary>
internal sealed class InboxPublicationJournalService(
    string inboxRoot, string modsRoot, string? legacyInboxRoot = null)
{
    private const int FormatVersion = 1;
    private const string JournalPattern = ".inbox-publish-*.pending.json";
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record PublicationIntent(
        int Version, string SourcePath, string DestinationPath, string ProcessedPath);

    public async Task<string> BeginAsync(
        string sourcePath, string destinationPath, string processedPath, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ct.ThrowIfCancellationRequested();
        var (source, destination, processed) = ValidatePaths(sourcePath, destinationPath, processedPath);
        if (!Exists(source) || Exists(destination) || Exists(processed))
            throw new IOException("Inbox publication requires an existing source and unoccupied destinations.");

        var root = EnsureJournalRoot();
        var journal = Path.Combine(root, $".inbox-publish-{Guid.NewGuid():N}.pending.json");
        var temporary = journal + ".tmp";
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new PublicationIntent(FormatVersion, source, destination, processed));
        try
        {
            await using (var output = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            }))
            {
                await output.WriteAsync(payload, ct);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, journal);
            return journal;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// Return source paths that must be skipped instead of re-imported. This operation
    /// does not move or delete any source, published package, or processed original.
    /// </summary>
    public async Task<IReadOnlySet<string>> RecoverAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = EnsureJournalRoot();
        var blockedSources = new HashSet<string>(PathComparer);
        foreach (var journal in Directory.EnumerateFiles(root, JournalPattern, SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            RejectReparsePath(journal);
            PublicationIntent entry;
            try
            {
                entry = JsonSerializer.Deserialize<PublicationIntent>(
                    await File.ReadAllTextAsync(journal, ct))
                    ?? throw new InvalidDataException($"Inbox publication intent is empty: {journal}");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Inbox publication intent is malformed: {journal}", ex);
            }
            if (entry.Version != FormatVersion)
                throw new InvalidDataException($"Unsupported Inbox publication intent: {journal}");
            var (source, destination, processed) =
                ValidatePaths(entry.SourcePath, entry.DestinationPath, entry.ProcessedPath);

            var sourcePresent = Exists(source);
            var destinationPresent = Exists(destination);
            var processedPresent = Exists(processed);
            if (sourcePresent && !destinationPresent && !processedPresent)
            {
                // The intent preceded publication. The original can be imported again.
                Complete(journal);
            }
            else if (!sourcePresent && destinationPresent && processedPresent)
            {
                // Both published package and safely archived original survived.
                Complete(journal);
            }
            else if (sourcePresent && destinationPresent && !processedPresent)
            {
                // A crash occurred after publication, before source archival. Do not
                // delete one copy or import the source a second time without proof.
                blockedSources.Add(source);
                MasterDebugLog.Write("INBOX-RECOVERY",
                    $"Manual review required for interrupted Inbox import. Source={source}; Published={destination}; Intent={journal}");
            }
            else
            {
                // A changed/missing path may belong to a later user operation.
                // Preserve every file and the intent instead of guessing a recovery.
                throw new InvalidDataException(
                    $"Interrupted Inbox publication has conflicting paths; manual review is required: {journal}");
            }
        }
        return blockedSources;
    }

    public void Complete(string journalPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = EnsureJournalRoot();
        var path = Path.GetFullPath(journalPath);
        var file = Path.GetFileName(path);
        if (!string.Equals(Path.GetDirectoryName(path), root, PathComparison)
            || !file.StartsWith(".inbox-publish-", StringComparison.Ordinal)
            || !file.EndsWith(".pending.json", StringComparison.Ordinal))
            throw new InvalidDataException("Refusing to remove a file outside the Inbox publication intent namespace.");
        if (!File.Exists(path)) return;
        RejectReparsePath(path);
        File.Delete(path);
    }

    public void CompleteIfRolledBack(string? journalPath, string sourcePath,
        string? destinationPath, string? processedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (journalPath is null || destinationPath is null || processedPath is null) return;
        // An exception after a completed archive must not erase evidence or the published mod.
        if (Exists(sourcePath) && !Exists(destinationPath) && !Exists(processedPath))
            Complete(journalPath);
    }

    private (string Source, string Destination, string Processed) ValidatePaths(
        string sourcePath, string destinationPath, string processedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath)
            || string.IsNullOrWhiteSpace(processedPath))
            throw new InvalidDataException("Inbox publication intent has missing path fields.");

        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var processed = Path.GetFullPath(processedPath);
        var sourceRoot = Path.GetDirectoryName(source);
        var allowedRoots = new[] { inboxRoot, legacyInboxRoot }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!));
        if (!allowedRoots.Any(root => string.Equals(root, sourceRoot, PathComparison))
            || string.Equals(Path.GetFileName(source), "Processed", PathComparison)
            || !string.Equals(Path.GetDirectoryName(destination),
                Path.GetFullPath(modsRoot), PathComparison)
            || !string.Equals(Path.GetDirectoryName(processed),
                Path.Combine(sourceRoot!, "Processed"), PathComparison))
            throw new InvalidDataException("Inbox publication intent escaped its owning paths.");

        RejectReparsePath(source);
        RejectReparsePath(destination);
        RejectReparsePath(processed);
        return (source, destination, processed);
    }

    private string EnsureJournalRoot()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        RejectReparsePath(Path.GetFullPath(modsRoot));
        return ImportPublicationWorkspace.RootFor(modsRoot);
    }

    private static bool Exists(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return File.Exists(path) || Directory.Exists(path);
    }

    private static void RejectReparsePath(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var full = Path.GetFullPath(path);
        if (Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Inbox publication refuses a reparse path: {full}");
        for (DirectoryInfo? dir = new(Path.GetDirectoryName(full)!); dir is not null; dir = dir.Parent)
        {
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Inbox publication refuses a reparse ancestor: {dir.FullName}");
        }
    }
}
