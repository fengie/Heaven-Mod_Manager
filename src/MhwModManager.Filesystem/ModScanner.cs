using System.Collections.Concurrent;
using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

public sealed class ModScanner(ManagerDatabase db, BlobStore blobs, HashingService hashing, GameProfile? game = null)
{
    private readonly int maxParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
    private static readonly string[] KnownAdditionalLoaderRoots =
        [@"BepInEx\plugins", @"Content\Paks", @"reframework\plugins", @"reframework\autorun"];

    internal bool IsArchiveTargetRoot(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (PathRules.Comparer.Equals(name, "GameRoot")) return true;
        var target = game?.ModRootRelativePath ?? "";
        var separator = target.IndexOfAny(['/', '\\']);
        // For a one-component mount the scanner reconstructs the whole target from the
        // profile. Preserve the existing flattened source layout for those packages.
        return separator > 0 && PathRules.Comparer.Equals(name, target[..separator]);
    }

    public Task<IReadOnlyList<ModFileDescriptor>> CaptureAsync(ModDescriptor mod, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return CaptureAsync(mod, false, false, ct);
    }

    public Task<IReadOnlyList<ModFileDescriptor>> CaptureForAutoPopulateAsync(ModDescriptor mod, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return CaptureAsync(mod, false, true, ct);
    }

    internal Task<IReadOnlyList<ModFileDescriptor>> CaptureShelfPackageAsync(ModDescriptor mod, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return CaptureAsync(mod, true, false, ct);
    }

    internal bool ShelfIndexMatchesSource(ModDescriptor mod, IReadOnlyList<ModFileDescriptor> files, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var cache = files.ToDictionary(file => file.Path, PathRules.Comparer);
        var observed = EnumerateCandidates(mod, cache, true, ct);
        return observed.Count == files.Count && observed.All(item =>
            item.Cached is not null && File.Exists(blobs.PathFor(item.Cached.BlobSha256)));
    }

    private async Task<IReadOnlyList<ModFileDescriptor>> CaptureAsync(ModDescriptor mod, bool managedPackage, bool automatic, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}; source={mod.SourcePath}");
        var role = await db.ShelfRoleAsync(mod.Id, ct);
        if (!managedPackage && role is "review" or "wrapper")
            throw new InvalidDataException($"Review the package layout of '{mod.DisplayName}' before applying.");
        var packageOnly = managedPackage || role is "package" or "duplicate";
        var cache = await LoadCacheAsync(mod.Id, ct);
        List<ScanCandidate> candidates;
        try
        {
            candidates = await Task.Run(() => EnumerateCandidates(mod, cache, packageOnly, ct), ct);
        }
        catch (IOException ex) when (automatic)
        {
            throw new InvalidDataException($"Source could not be inspected: {ex.Message}", ex);
        }
        if (automatic)
        {
            if (candidates.Any(item => Path.GetExtension(item.File).ToLowerInvariant() is ".zip" or ".7z" or ".rar"))
                throw new InvalidDataException("Contains a transport archive; import its contents before activation.");
            if (game?.AdapterId == "unreal-paks")
            {
                var mount = "root" + Path.DirectorySeparatorChar + game.ModRootRelativePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var mounted = candidates.Where(item => item.Key.StartsWith(mount, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (mounted.Length > 0 && !UnrealPakLayout.Complete(mounted.Select(item => item.Key)))
                    throw new InvalidDataException("The loader folder contains incomplete or unrelated Unreal payloads.");
            }
        }
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

    private List<ScanCandidate> EnumerateCandidates(ModDescriptor mod, Dictionary<string,ModFileDescriptor> cache, bool packageOnly, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SafeRecursiveTraversal.EnsureRootIsNotReparse(mod.SourcePath);
        var result = new List<ScanCandidate>();
        var pakMount = game is null ? null : UnrealPakLayout.ProjectMount(game.ModRootRelativePath);
        if (game?.AdapterId == "unreal-paks" && pakMount is null &&
            (game.ModRootRelativePath.Equals(@"Content\Paks", StringComparison.OrdinalIgnoreCase) ||
             game.ModRootRelativePath.Equals(@"Content\Paks\~mods", StringComparison.OrdinalIgnoreCase)))
            pakMount = @"Content\Paks";
        foreach (var (diskRoot, prefix) in ResolveRoots(mod.SourcePath, game, ct))
        {
            var snapshot = SafeRecursiveTraversal.Snapshot(diskRoot, ct).Files;
            var bundledProject = pakMount is not null &&
                snapshot.Any(file => file.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) &&
                UnrealPakLayout.HasBundledEditorProject(snapshot.Select(file => Path.GetRelativePath(diskRoot, file)).ToArray());
            foreach (var file in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(file);
                if (IsIgnored(fi)) continue;
                var rel = Path.GetRelativePath(diskRoot, file).Replace('/', '\\');
                if ((packageOnly || bundledProject) && !UnrealPakLayout.IsMember(rel) &&
                    (ManagedCollectionNotes.IsNote(rel) || (bundledProject && rel.Contains(Path.DirectorySeparatorChar)))) continue;
                if (pakMount is not null && !rel.Contains('\\') &&
                    (ManagedCollectionNotes.IsNote(rel) || rel.Equals("mod-intake.json", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetExtension(rel).Equals(".md", StringComparison.OrdinalIgnoreCase))) continue;
                if ((game is null || game.IsMonsterHunterWorld) &&
                    PathRules.Comparer.Equals(diskRoot, mod.SourcePath) &&
                    (rel.StartsWith("GameRoot\\", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("nativePC\\", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var key = PathRules.Normalize(prefix + rel);
                if (game?.AdapterId == "unreal-paks" &&
                    prefix.Equals("root\\" + game.ModRootRelativePath + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    if (game.ModRootRelativePath.EndsWith(@"Content\Paks", StringComparison.OrdinalIgnoreCase) &&
                        !rel.Contains('\\') && UnrealPakLayout.IsMember(rel))
                        key = PathRules.Normalize(prefix + @"~mods\" + rel);
                    else if (game.ModRootRelativePath.EndsWith(@"Content\Paks\~mods", StringComparison.OrdinalIgnoreCase) &&
                        rel.StartsWith(@"~mods\", StringComparison.OrdinalIgnoreCase))
                        key = PathRules.Normalize(prefix + rel[6..]);
                }
                cache.TryGetValue(key, out var cached);
                if (cached is not null && (cached.Length != fi.Length || cached.LastWriteUtc.UtcDateTime != fi.LastWriteTimeUtc)) cached = null;
                result.Add(new(file,key,cached));
            }
        }
        if (game is not null && pakMount is not null && !Directory.Exists(Path.Combine(mod.SourcePath, "GameRoot")))
            ValidatePakTargets(result, mod.SourcePath, game, pakMount);
        return result;
    }

    private static void ValidatePakTargets(List<ScanCandidate> result, string sourceFolder,
        GameProfile profile, string pakMount)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mount = "root\\" + pakMount.Replace('/', '\\') + "\\";
        var members = result.Where(item => item.Key.StartsWith(mount, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (members.Length > 0 && !UnrealPakLayout.Complete(members.Select(item => item.Key)))
            throw new InvalidDataException("Incomplete or unrecognized Unreal Paks content. Review the package before applying.");
        var mirrored = Directory.Exists(Path.Combine(sourceFolder, profile.ModRootRelativePath));
        foreach (var item in result)
        {
            var rel = Path.GetRelativePath(sourceFolder, item.File).Replace('\\', '/');
            if (!item.Key.StartsWith(mount, StringComparison.OrdinalIgnoreCase))
            {
                if (!KnownAdditionalLoaderRoots.Any(loader => item.Key.StartsWith(
                    "root\\" + loader + "\\", StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(Path.Combine(profile.GameRoot, loader))))
                    throw new InvalidDataException($"'{rel}' is outside the configured loader. Review the package before applying.");
            }
            else if (!mirrored && rel.Contains('/') && !rel.StartsWith("~mods/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unrecognized Paks folder '{rel}'. Review its intended destination before applying.");
        }
        if (result.Select(item => item.Key).Distinct(PathRules.Comparer).Count() != result.Count)
            throw new InvalidDataException("Multiple package files map to the same destination. Review the package before applying.");
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

    private static List<(string root, string prefix)> ResolveRoots(
        string folder, GameProfile? gameProfile, CancellationToken ct)
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
        var targetIsMirrored=Directory.Exists(targetInPackage);

        // A game can have both a BepInEx plugin loader and an Unreal PAK
        // directory, even when its preferred profile target names just one.
        // Only use another loader root if that same directory is installed in
        // this game's own root. Otherwise refuse the package instead of quietly
        // nesting its payload under an unrelated loader path.
        var additional = KnownAdditionalLoaderRoots
            .Where(relative => !relative.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                // Never scan both a configured nested target and its ancestor
                // (e.g. Content\\Paks and Content\\Paks\\~mods) as separate providers.
                (!targetIsMirrored ||
                    (!target.StartsWith(relative + "\\", StringComparison.OrdinalIgnoreCase) &&
                     !relative.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase))) &&
                Directory.Exists(Path.Combine(folder, relative)))
            .ToArray();
        if (additional.Length > 0)
        {
            var mappedRoots = new List<string>(additional);
            if (targetIsMirrored) mappedRoots.Add(target);

            foreach (var relative in additional)
            {
                if (!Directory.Exists(Path.Combine(gameProfile.GameRoot, relative)))
                    throw new InvalidDataException(
                        $"Package targets '{relative}', but the registered game does not expose that loader root. Review the game/loader assignment.");
            }

            // Never silently discard a second required payload or deploy an
            // unrecognized companion at game root. The bounded safe traversal
            // validates all components (including reparse points) first.
            foreach (var file in SafeRecursiveTraversal.Snapshot(folder, ct).Files)
            {
                if (IsIgnored(new FileInfo(file))) continue;
                var relativeFile = Path.GetRelativePath(folder, file).Replace('/', '\\');
                if (!mappedRoots.Any(relative => relativeFile.StartsWith(
                        relative + "\\", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(
                        $"Package contains '{relativeFile}' outside recognized installed loader targets. Review the package layout before deployment.");
            }

            foreach (var relative in mappedRoots)
                list.Add((Path.Combine(folder, relative),
                    "root\\" + GameProfile.NormalizeRelative(relative, false) + "\\"));
            return list;
        }

        if (targetIsMirrored)
        {
            // Preserve the legacy game-relative package layout when no other
            // independently recognized loader root requires validation.
            list.Add((folder, "root\\"));
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
