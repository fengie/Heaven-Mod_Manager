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

    public static void Publish(string staging, string destination)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stagingFull = Path.GetFullPath(staging);
        var destinationFull = Path.GetFullPath(destination);
        if (!Directory.Exists(stagingFull))
            throw new DirectoryNotFoundException($"Import staging directory is missing: {stagingFull}");
        if (Directory.Exists(destinationFull) || File.Exists(destinationFull))
            throw new IOException($"Import destination already exists: {destinationFull}");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetPathRoot(stagingFull), Path.GetPathRoot(destinationFull)))
            throw new IOException("Import staging and destination must be on the same volume.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFull)!);
        Directory.Move(stagingFull, destinationFull);
    }

    public static void Cleanup(string? staging)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(staging) || !Directory.Exists(staging)) return;
        try
        {
            var attributes = File.GetAttributes(staging);
            Directory.Delete(staging, (attributes & FileAttributes.ReparsePoint) == 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write(
                "IMPORT-CLEANUP",
                $"Failed to remove catalog-invisible import workspace '{staging}'. It remains outside ModsRoot and cannot be cataloged.",
                ex);
        }
    }
}
