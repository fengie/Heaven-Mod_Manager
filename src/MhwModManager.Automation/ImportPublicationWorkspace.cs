using MhwModManager.Core;

namespace MhwModManager.Automation;

/// <summary>
/// Keeps incomplete import work structurally outside the catalog-visible Mods root.
/// A completed package becomes discoverable only through the final same-volume directory move.
/// </summary>
internal static class ImportPublicationWorkspace
{
    public static string RootFor(string modsRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var fullModsRoot = Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullModsRoot)
            ?? throw new InvalidOperationException("Mods root must have a parent directory.");
        var leaf = Path.GetFileName(fullModsRoot);
        if (string.IsNullOrWhiteSpace(leaf))
            throw new InvalidOperationException("Mods root must have a directory name.");

        var workspace = Path.Combine(parent, $".{leaf}.import-work");
        Directory.CreateDirectory(workspace);
        if ((File.GetAttributes(workspace) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Import workspace cannot be a symbolic link or reparse point: {workspace}");
        return workspace;
    }

    public static string Allocate(string modsRoot, string kind)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Import workspace kind is required.", nameof(kind));
        return Path.Combine(RootFor(modsRoot), $"{kind}-{Guid.NewGuid():N}");
    }

    public static void Publish(string modsRoot, string staging, string destination)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var fullModsRoot = Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspaceRoot = Path.GetFullPath(RootFor(modsRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stagingFull = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destinationFull = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(stagingFull), workspaceRoot))
            throw new InvalidDataException("Import staging path is not a direct child of the manager-owned import workspace.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(destinationFull), fullModsRoot))
            throw new InvalidDataException("Import destination is not a direct child of ModsRoot.");
        if (!Directory.Exists(stagingFull))
            throw new DirectoryNotFoundException($"Import staging directory is missing: {stagingFull}");
        if ((File.GetAttributes(stagingFull) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Import staging directory cannot be a symbolic link or reparse point: {stagingFull}");
        if (Directory.Exists(destinationFull) || File.Exists(destinationFull))
            throw new IOException($"Import destination already exists: {destinationFull}");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetPathRoot(stagingFull), Path.GetPathRoot(destinationFull)))
            throw new IOException("Import staging and destination must be on the same volume.");

        Directory.CreateDirectory(fullModsRoot);
        Directory.Move(stagingFull, destinationFull);
    }

    public static void RollbackPublished(string modsRoot, string? destination)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var fullModsRoot = Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(candidate), fullModsRoot))
            {
                MasterDebugLog.Write("IMPORT-ROLLBACK", $"Refusing to delete non-ModsRoot published path: {candidate}");
                return;
            }
            if (!Directory.Exists(candidate)) return;
            if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                MasterDebugLog.Write("IMPORT-ROLLBACK", $"Refusing recursive rollback of reparse published path: {candidate}");
                return;
            }
            Directory.Delete(candidate, true);
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write(
                "IMPORT-ROLLBACK",
                $"Failed to roll back published import '{destination}' after source archival failed.",
                ex);
        }
    }

    public static void Cleanup(string modsRoot, string? staging)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(staging)) return;
        try
        {
            var workspaceRoot = Path.GetFullPath(RootFor(modsRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(candidate), workspaceRoot))
            {
                MasterDebugLog.Write("IMPORT-CLEANUP", $"Refusing to delete non-workspace import path: {candidate}");
                return;
            }
            if (!Directory.Exists(candidate)) return;
            if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                MasterDebugLog.Write("IMPORT-CLEANUP", $"Refusing recursive cleanup of reparse import workspace: {candidate}");
                return;
            }
            Directory.Delete(candidate, true);
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write(
                "IMPORT-CLEANUP",
                $"Failed to remove catalog-invisible import workspace '{staging}'. It remains outside ModsRoot and cannot be cataloged.",
                ex);
        }
    }
}
