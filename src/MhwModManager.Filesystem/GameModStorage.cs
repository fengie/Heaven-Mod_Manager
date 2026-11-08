using MhwModManager.Core;

namespace MhwModManager.Filesystem;

/// <summary>
/// Resolves the stable user-owned library for one registered game. The legacy roots stay readable
/// and are never relocated: SQLite mod source paths, snapshots and rollback records may refer to them.
/// The new top-level directory is deliberately a sibling of legacy MHW Mods, so older installed
/// clients cannot accidentally index a "By Game" directory as an MHW mod after a rollback.
/// </summary>
public sealed record GameModStoragePaths(
    string GameRoot,
    string InstalledRoot,
    string DownloadsRoot,
    string InboxRoot,
    string ArchiveRoot,
    string LegacyInstalledRoot,
    string LegacyInboxRoot);

public static class GameModStorage
{
    public static GameModStoragePaths Resolve(string managerHome,GameProfile game)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(managerHome);
        ArgumentNullException.ThrowIfNull(game);
        if(!GameProfile.IsCanonicalId(game.Id))
            throw new InvalidDataException("Game profile ID cannot be used as a storage directory.");

        var tool=Path.GetFullPath(managerHome);
        var parent=Path.GetFullPath(Path.Combine(tool,"Mods by Game"));
        var scoped=Path.GetFullPath(Path.Combine(parent,game.Id));
        var prefix=parent.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!scoped.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Game mod library escaped the manager-owned storage root.");

        var legacyBase=game.IsMonsterHunterWorld?tool:Path.Combine(tool,"Games",game.Id);
        return new(
            scoped,
            Path.Combine(scoped,"Installed"),
            Path.Combine(scoped,"Downloads"),
            Path.Combine(scoped,"Inbox"),
            Path.Combine(scoped,"Archive"),
            Path.Combine(legacyBase,"Mods"),
            Path.Combine(legacyBase,"Inbox"));
    }

    public static void EnsureDirectories(GameModStoragePaths paths)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(paths);
        // Never follow a pre-existing reparse-point component into unrelated user-owned data.
        var libraryParent=Path.GetDirectoryName(paths.GameRoot)
            ?? throw new InvalidDataException("Game mod library has no owning parent.");
        foreach(var directory in new[]{libraryParent,paths.GameRoot,paths.InstalledRoot,paths.InboxRoot,paths.ArchiveRoot})
        {
            if(File.Exists(directory)&&!Directory.Exists(directory))
                throw new IOException($"Game mod library path is not a directory: {directory}");
            if(Directory.Exists(directory)&&(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)
                throw new IOException($"Game mod library refuses a reparse-point directory: {directory}");
            Directory.CreateDirectory(directory);
        }
        // Downloads are created only by an explicit acquisition or Open Downloads action.
        // In particular, startup never hashes, enumerates or cleans user-owned downloads.
    }
}
