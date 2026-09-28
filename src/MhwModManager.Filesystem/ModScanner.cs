using System.Collections.Concurrent;
using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed class ModScanner(ManagerDatabase db, BlobStore blobs, HashingService hashing, GameProfile? game = null)
{
    private readonly int maxParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    public async Task<IReadOnlyList<ModFileDescriptor>> CaptureAsync(ModDescriptor mod, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}; source={mod.SourcePath}");
        var cache = await LoadCacheAsync(mod.Id, ct);
        var candidates = await Task.Run(() => EnumerateCandidates(mod, cache, ct), ct);
        var output = new ConcurrentBag<ModFileDescriptor>();

        await Parallel.ForEachAsync(candidates,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct },
            async (item, token) =>
            {
                // Metadata alone is not treated as authoritative. A fast XXH3 pass catches
                // same-size/same-timestamp edits without paying SHA-256 + CAS copy cost.
                if (item.Cached is not null && item.Cached.FastHash is not null
                    && File.Exists(blobs.PathFor(item.Cached.BlobSha256)))
                {
                    var fast = await hashing.HashFileAsync(item.File, authoritative:false, ct:token);
                    if (StringComparer.OrdinalIgnoreCase.Equals(fast.Xxh3,item.Cached.FastHash) &&
                        fast.Length == item.Cached.Length && fast.LastWriteUtc.UtcDateTime == item.Cached.LastWriteUtc.UtcDateTime)
                    {
                        output.Add(item.Cached);
                        return;
                    }
                }

                var captured = await blobs.CaptureWithHashAsync(item.File, registerInDatabase:false, ct:token);
                output.Add(new(mod.Id, item.Key, captured.Sha256, captured.Xxh3, captured.Length, captured.LastWriteUtc, PathRules.ClassifyFile(item.Key)));
            });

        var ordered = output.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length == 0) throw new InvalidDataException($"No deployable files were found in '{mod.SourcePath}'.");
        await db.ReplaceModFilesAsync(mod.Id, ordered, ct);
        return ordered;
    }

    private List<ScanCandidate> EnumerateCandidates(ModDescriptor mod, Dictionary<string,ModFileDescriptor> cache, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SafeRecursiveTraversal.EnsureRootIsNotReparse(mod.SourcePath);
        var result = new List<ScanCandidate>();
        foreach (var (diskRoot, prefix) in ResolveRoots(mod.SourcePath, game))
        {
            foreach (var file in SafeRecursiveTraversal.Snapshot(diskRoot, ct).Files)
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(file);
                if (IsIgnored(fi)) continue;
                var rel = Path.GetRelativePath(diskRoot, file).Replace('/', '\\');
                if ((game is null || game.IsMonsterHunterWorld) &&
                    PathRules.Comparer.Equals(diskRoot, mod.SourcePath) &&
                    (rel.StartsWith("GameRoot\\", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("nativePC\\", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var key = PathRules.Normalize(prefix + rel);
                cache.TryGetValue(key, out var cached);
                if (cached is not null && (cached.Length != fi.Length || cached.LastWriteUtc.UtcDateTime != fi.LastWriteTimeUtc)) cached = null;
                result.Add(new(file,key,cached));
            }
        }
        return result;
    }

    private async Task<Dictionary<string, ModFileDescriptor>> LoadCacheAsync(string modId, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new Dictionary<string, ModFileDescriptor>(StringComparer.OrdinalIgnoreCase);
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files WHERE mod_id=$m";
        cmd.Parameters.AddWithValue("$m", modId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var item = new ModFileDescriptor(modId, r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3), DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture), Enum.Parse<FileClass>(r.GetString(5)));
            result[item.Path] = item;
        }
        return result;
    }

    private static List<(string root, string prefix)> ResolveRoots(string folder, GameProfile? gameProfile)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Mod source folder is missing: {folder}");
        var list = new List<(string, string)>();
        if (gameProfile is null || gameProfile.IsMonsterHunterWorld)
        {
            var native = Path.Combine(folder, "nativePC");
            var gameRoot = Path.Combine(folder, "GameRoot");
            if (Directory.Exists(native)) list.Add((native, "nativePC\\"));
            else list.Add((folder, "nativePC\\"));
            if (Directory.Exists(gameRoot)) list.Add((gameRoot, "root\\"));
            return list;
        }

        var explicitRoot=Path.Combine(folder,"GameRoot");
        if(Directory.Exists(explicitRoot))
        {
            list.Add((explicitRoot,"root\\"));
            return list;
        }

        var target=gameProfile.ModRootRelativePath;
        if(string.IsNullOrWhiteSpace(target))
        {
            list.Add((folder,"root\\"));
            return list;
        }

        var targetInPackage=Path.Combine(folder,target);
        if(Directory.Exists(targetInPackage))
        {
            // Package already mirrors the game root (for example Mods\Foo or Data\Foo).
            list.Add((folder,"root\\"));
            return list;
        }

        list.Add((folder,"root\\"+GameProfile.NormalizeRelative(target,true)+"\\"));
        return list;
    }

    private static bool IsIgnored(FileInfo f) =>
        f.Name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
        f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
        f.Name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase) ||
        f.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
        f.FullName.Contains(Path.DirectorySeparatorChar + "__MACOSX" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        f.FullName.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed record ScanCandidate(string File,string Key,ModFileDescriptor? Cached);
}
