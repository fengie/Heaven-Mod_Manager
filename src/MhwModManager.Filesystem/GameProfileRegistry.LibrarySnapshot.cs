using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed partial class GameProfileRegistry
{
    private const int LibrarySnapshotByteLimit = 4 * 1024 * 1024;
    private sealed record LibrarySnapshot(int Version, string GameId, string GameRoot, string? SteamAppId,
        ExternalLibraryReference[] Rows);

    public IReadOnlyList<ExternalLibraryReference> LoadDiscoveredLibrary(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var file = new FileInfo(LibrarySnapshotPath(game));
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) return [];
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is <= 0 or > LibrarySnapshotByteLimit) return [];
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            var snapshot = JsonSerializer.Deserialize<LibrarySnapshot>(bytes);
            if (snapshot is not { Version: 1, Rows.Length: <= 16384 } || snapshot.GameId != game.Id ||
                snapshot.SteamAppId != game.SteamAppId || !PathRules.Comparer.Equals(snapshot.GameRoot, game.GameRoot)) return [];
            if (snapshot.Rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.ItemId) ||
                string.IsNullOrWhiteSpace(row.DisplayName) || string.IsNullOrWhiteSpace(row.OwnerLabel) ||
                row.Locations is not { Count: > 0 } || row.Locations.Any(string.IsNullOrWhiteSpace))) return [];
            return snapshot.Rows;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public IReadOnlyList<ExternalLibraryReference> ReconcileDiscoveredLibrary(GameProfile game,
        IReadOnlyList<ExternalLibraryReference> verified, IReadOnlyList<LocalModLocation> observations)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var previous = LoadDiscoveredLibrary(game);
        if (previous.Count == 0) return verified;
        var incomplete = observations.Where(row => row.Name.EndsWith("scan incomplete", StringComparison.OrdinalIgnoreCase)
                || row.Name == "Installation unavailable")
            .Select(row => Path.TrimEndingDirectorySeparator(row.Location)).Distinct(PathRules.Comparer).ToArray();
        var rootsAvailable = new Dictionary<string, bool>(PathRules.Comparer);
        var verifiedById = verified.ToLookup(row => row.ItemId, StringComparer.Ordinal);
        var rows = verified.ToList();
        foreach (var old in previous)
        {
            if (verifiedById[old.ItemId].Any(row => row.Locations.Intersect(old.Locations, PathRules.Comparer).Any())) continue;
            var offline = old.Locations.Where(location =>
            {
                if (!Path.IsPathFullyQualified(location)) return false;
                var root = Path.GetPathRoot(location)!;
                if (!rootsAvailable.TryGetValue(root, out var available))
                    rootsAvailable[root] = available = Directory.Exists(root);
                return !available || incomplete.Any(folder => PathRules.Comparer.Equals(folder, location)
                    || location.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            if (offline.Length > 0)
                rows.Add(old with { OwnerLabel = "Previously discovered · source unavailable", Locations = offline });
        }
        return rows;
    }

    public void SaveDiscoveredLibrary(GameProfile game, IReadOnlyList<ExternalLibraryReference> rows)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (rows.Count > 16384) return;
        var path = LibrarySnapshotPath(game);
        Directory.CreateDirectory(stateRoot);
        var snapshot = new LibrarySnapshot(1, game.Id, game.GameRoot, game.SteamAppId,
            rows.ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        if (bytes.Length > LibrarySnapshotByteLimit) return;
        AtomicWrite(path, bytes);
    }

    private string LibrarySnapshotPath(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game.Id)));
        return Path.Combine(stateRoot, $"discovered-library-{key}.json");
    }
}
