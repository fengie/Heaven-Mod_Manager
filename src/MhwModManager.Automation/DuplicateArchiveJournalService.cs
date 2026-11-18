using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

/// <summary>
/// Process-crash recovery for source directories moved into the manager-owned duplicate
/// archive. The intent is durable before the move; a restart reconciles it against the
/// authoritative SQLite mod row before catalog discovery can mutate that row.
/// </summary>
internal sealed class DuplicateArchiveJournalService(ManagerDatabase db, string archiveRoot)
{
    private const int FormatVersion = 1;
    private const string JournalPattern = ".duplicate-archive-*.pending.json";
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record ArchiveIntent(int Version, string ModId, string SourcePath, string ArchivePath);

    public async Task<string> BeginAsync(string modId, string sourcePath, string archivedPath, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivedPath);
        ct.ThrowIfCancellationRequested();

        var root = EnsureArchiveRoot();
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(archivedPath);
        ValidateArchiveDestination(root, destination);
        RejectReparseAncestors(source);
        if (!Directory.Exists(source) || File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Duplicate-archive journal requires an existing source and an unoccupied archive destination.");

        var journal = Path.Combine(root, $".duplicate-archive-{Guid.NewGuid():N}.pending.json");
        var temporary = journal + ".tmp";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ArchiveIntent(FormatVersion, modId, source, destination));
        var createdTemporary = false;
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
                createdTemporary = true;
                await output.WriteAsync(payload, ct);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            // Only our own temporary file becomes a visible intent. A crash before this
            // rename cannot move the source because the caller waits for this method.
            File.Move(temporary, journal);
            return journal;
        }
        finally
        {
            // Never delete an unknown/published directory or mod file here.
            if (createdTemporary && File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public async Task<int> RecoverAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = EnsureArchiveRoot();
        var pending = Directory.EnumerateFiles(root, JournalPattern, SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pending.Length == 0) return 0;

        // SQLite mod IDs use binary equality; case variants are independent records.
        var mods = (await db.GetModsAsync(ct)).ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        var restored = 0;
        foreach (var path in pending)
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Duplicate-archive intent is a reparse point: {path}");

            ArchiveIntent entry;
            try
            {
                entry = JsonSerializer.Deserialize<ArchiveIntent>(await File.ReadAllTextAsync(path, ct))
                    ?? throw new InvalidDataException($"Duplicate-archive intent is empty: {path}");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Duplicate-archive intent is malformed: {path}", ex);
            }

            if (entry.Version != FormatVersion || string.IsNullOrWhiteSpace(entry.ModId)
                || string.IsNullOrWhiteSpace(entry.SourcePath)
                || string.IsNullOrWhiteSpace(entry.ArchivePath))
                throw new InvalidDataException($"Duplicate-archive intent has invalid fields: {path}");

            var source = Path.GetFullPath(entry.SourcePath);
            var destination = Path.GetFullPath(entry.ArchivePath);
            ValidateArchiveDestination(root, destination);
            RejectReparseAncestors(source);
            RejectReparseAncestors(destination);
            if (File.Exists(source) || File.Exists(destination))
                throw new InvalidDataException($"Duplicate-archive intent points at a non-directory path: {path}");

            var sourcePresent = Directory.Exists(source);
            var archivePresent = Directory.Exists(destination);
            if (sourcePresent && archivePresent)
                throw new InvalidDataException($"Both duplicate-archive source and destination exist; refusing overwrite: {path}");

            if (mods.TryGetValue(entry.ModId, out var mod))
            {
                if (!string.Equals(source, Path.GetFullPath(mod.SourcePath), PathComparison))
                    throw new InvalidDataException($"Duplicate-archive intent does not match SQLite source identity: {path}");

                if (!sourcePresent && !archivePresent)
                    throw new InvalidDataException($"Duplicate-archive intent lost both source and archive; no automatic deletion: {path}");

                if (!sourcePresent)
                {
                    // A crashed move left the SQLite row pointing to the original source.
                    // Restore exactly that path; never overwrite a newer source directory.
                    Directory.Move(destination, source);
                    restored++;
                    MasterDebugLog.Write("DUPLICATE-RECOVERY",
                        $"Restored interrupted archive for ModId={entry.ModId}; Source={source}; Archive={destination}");
                }
            }
            else
            {
                // Retirement committed before interruption: keep the archived payload.
                // Missing or conflicting paths are ambiguous and must fail closed.
                if (!archivePresent || sourcePresent)
                    throw new InvalidDataException($"Committed duplicate-archive intent has conflicting filesystem state: {path}");
            }

            Complete(path);
        }
        return restored;
    }

    public void Complete(string journalPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = EnsureArchiveRoot();
        var path = Path.GetFullPath(journalPath);
        if (!string.Equals(Path.GetDirectoryName(path), root, PathComparison)
            || !Path.GetFileName(path).StartsWith(".duplicate-archive-", StringComparison.Ordinal)
            || !path.EndsWith(".pending.json", StringComparison.Ordinal))
            throw new InvalidDataException("Refusing to remove a file outside the duplicate-archive intent namespace.");
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Refusing to delete a reparse duplicate-archive intent: {path}");
        File.Delete(path);
    }

    private string EnsureArchiveRoot()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var fullRoot = Path.GetFullPath(archiveRoot);
        RejectReparseAncestors(fullRoot);
        Directory.CreateDirectory(fullRoot);
        RejectReparseAncestors(fullRoot);
        return fullRoot;
    }

    private static void ValidateArchiveDestination(string root, string destination)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.Equals(Path.GetDirectoryName(destination), root, PathComparison))
            throw new InvalidDataException("Duplicate archive destination must be a direct child of the manager-owned Archive root.");
    }

    private static void RejectReparseAncestors(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        for (DirectoryInfo? dir = new(Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
        {
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Duplicate archive refuses a reparse directory component: {dir.FullName}");
        }
    }
}
