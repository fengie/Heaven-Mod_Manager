using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Automation;

/// <summary>
/// Owns the archive-to-library filesystem workflow. UI code chooses a file; this service validates,
/// stages, normalizes and atomically publishes the source folder before refreshing the catalog.
/// Live game files are never touched by this workflow.
/// </summary>
public sealed record FomodImportPreparation(
    string StagingPath,
    string DestinationPath,
    string DisplayName,
    FomodInstallerService Installer);

public sealed class ArchiveImportService(ArchiveInspector archive, CatalogService catalog, string modsRoot)
{
    public async Task<ArchiveImportResult> ImportAsync(string archivePath, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={Path.GetFileName(archivePath)}");
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path is required.", nameof(archivePath));

        var info = await archive.InspectAsync(archivePath, ct);
        if (info.HasSuspiciousPaths)
            throw new InvalidDataException("Archive contains unsafe absolute/traversal/device paths.");

        var name = Path.GetFileNameWithoutExtension(archivePath);
        var destination = UniqueDirectory(Path.Combine(modsRoot, name));
        var staging = NewStagingPath("archive");
        try
        {
            await archive.ExtractSafelyAsync(archivePath, staging, modsRoot, ct);
            await Task.Run(() => NormalizeSingleWrapper(staging), ct);
            Directory.Move(staging, destination);
            await catalog.RefreshFoldersAsync(ct);
            return new(destination, Path.GetFileName(destination));
        }
        catch
        {
            TryDeleteOwnedStaging(staging);
            throw;
        }
    }

    public async Task<FomodImportPreparation> PrepareFomodAsync(string archivePath, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={Path.GetFileName(archivePath)}");
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path is required.", nameof(archivePath));
        var info = await archive.InspectAsync(archivePath, ct);
        if (info.HasSuspiciousPaths) throw new InvalidDataException("Archive contains unsafe absolute/traversal/device paths.");

        var displayName = Path.GetFileNameWithoutExtension(archivePath);
        var destination = UniqueDirectory(Path.Combine(modsRoot, displayName));
        var staging = NewStagingPath("fomod");
        try
        {
            await archive.ExtractSafelyAsync(archivePath, staging, modsRoot, ct);
            await Task.Run(() => NormalizeSingleWrapper(staging), ct);
            if (!FomodInstallerService.HasInstaller(staging))
                throw new InvalidDataException("Archive does not contain exactly one supported FOMOD installer.");
            return new(staging, destination, Path.GetFileName(destination), new FomodInstallerService(staging));
        }
        catch
        {
            TryDeleteOwnedStaging(staging);
            throw;
        }
    }

    public async Task<ArchiveImportResult> CommitFomodAsync(
        FomodImportPreparation preparation,
        IReadOnlySet<string> selected,
        GameProfile game,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"destination={preparation.DestinationPath}");
        try
        {
            await preparation.Installer.InstallAsync(selected, game, preparation.DestinationPath, ct);
            await catalog.RefreshFoldersAsync(ct);
            return new(preparation.DestinationPath, preparation.DisplayName);
        }
        finally
        {
            TryDeleteOwnedStaging(preparation.StagingPath);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ArchiveImportService is intentionally consumed as an injectable instance workflow service.")]
    public Task CancelFomodAsync(FomodImportPreparation preparation)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"staging={preparation.StagingPath}");
        TryDeleteOwnedStaging(preparation.StagingPath);
        return Task.CompletedTask;
    }

    private string NewStagingPath(string kind)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stagingRoot = Path.Combine(modsRoot, CatalogService.ImportStagingDirectoryName);
        Directory.CreateDirectory(stagingRoot);
        return Path.Combine(stagingRoot, $"{kind}-{Guid.NewGuid():N}");
    }

    private static void TryDeleteOwnedStaging(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private static string UniqueDirectory(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Directory.Exists(path)) return path;
        for (var i = 2; ; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    private static void NormalizeSingleWrapper(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dirs = Directory.GetDirectories(root);
        var files = Directory.GetFiles(root);
        if (files.Length != 0 || dirs.Length != 1) return;

        var child = dirs[0];
        foreach (var entry in Directory.GetFileSystemEntries(child))
        {
            var destination = Path.Combine(root, Path.GetFileName(entry));
            if (Directory.Exists(entry)) Directory.Move(entry, destination);
            else File.Move(entry, destination);
        }
        Directory.Delete(child, true);
    }
}
