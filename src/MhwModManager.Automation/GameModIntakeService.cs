using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Filesystem;

namespace MhwModManager.Automation;

public enum GameModIntakeStatus { Routed, AlreadyPresent, NeedsReview, Skipped }

public sealed record GameModIntakeItem(
    string SourcePath, string? GameId, GameModIntakeStatus Status, string Reason);

public sealed record GameModIntakeScan(IReadOnlyList<GameModIntakeItem> Items)
{
    public int Routed { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.Routed); } }
    public int AlreadyPresent { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.AlreadyPresent); } }
    public int NeedsReview { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.NeedsReview); } }
    public int Skipped { get { using var __mhwTrace = MasterDebugLog.BeginMethod(); return Items.Count(item => item.Status == GameModIntakeStatus.Skipped); } }
}

/// <summary>
/// Safe, repeatable intake from explicitly designated directories, independent of the
/// currently active game. Copies high-confidence packages into the owning game's Inbox;
/// SmartInbox remains the sole importer and no source or live game files are moved/deleted.
/// </summary>
public sealed class GameModIntakeService
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
    private const int MaxSources = 12;
    private const int MaxEntriesPerSource = 2048;
    private const int MaxFolderFiles = 512;
    private const int MaxContainerDepth = 4;
    private const long MaxArchiveBytes = 8L * 1024 * 1024 * 1024;
    private const int IoBufferSize = 128 * 1024;
    private readonly string managerHome;
    private readonly string stateRoot;
    private readonly string settingsPath;
    private readonly Func<IReadOnlyList<GameProfile>> loadGames;
    private readonly ArchiveInspector archive;

    public string ReportPath
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return Path.Combine(stateRoot, "last-mod-intake-report.json");
        }
    }

    public GameModIntakeService(
        string managerHome, string stateRoot, Func<IReadOnlyList<GameProfile>> loadGames,
        ArchiveInspector? archive = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(managerHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentNullException.ThrowIfNull(loadGames);
        this.managerHome = Path.GetFullPath(managerHome);
        this.stateRoot = Path.GetFullPath(stateRoot);
        this.loadGames = loadGames;
        this.archive = archive ?? new ArchiveInspector();
        settingsPath = Path.Combine(this.stateRoot, "mod-intake-folders.json");
    }

    public IReadOnlyList<string> LoadSources()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!File.Exists(settingsPath)) return [];
        RefuseReparseComponents(settingsPath);
        using var reader = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (reader.Length > 16 * 1024)
            throw new InvalidDataException("Auto-sort source configuration exceeds its 16 KiB limit.");
        using var textReader = new StreamReader(reader, Encoding.UTF8, true, 1024, leaveOpen: true);
        string[] sources;
        try
        {
            sources = JsonSerializer.Deserialize<string[]>(textReader.ReadToEnd())
                ?? throw new InvalidDataException("Auto-sort source configuration is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Auto-sort source configuration is malformed.", ex);
        }
        if (sources.Length > MaxSources || sources.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new InvalidDataException("Auto-sort source configuration contains an invalid path.");
        return sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void AddSource(string sourceFolder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var full = ValidateSource(sourceFolder);
        var old = LoadSources();
        if (old.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
        if (old.Count >= MaxSources)
            throw new InvalidDataException("At most 12 intake folders can be watched.");
        SaveSources([.. old, full]);
    }

    public void RemoveSource(string sourceFolder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        var full = Path.GetFullPath(sourceFolder);
        var existing = LoadSources();
        SaveSources(existing.Where(path => !path.Equals(full, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public async Task<GameModIntakeScan> ScanAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result = new List<GameModIntakeItem>();
        var receipts = LoadReceipts();
        var games = loadGames().Where(game => GameProfile.IsCanonicalId(game.Id))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var source in LoadSources())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ValidateSource(source);
                await ScanSourceAsync(source, games, result, receipts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                result.Add(new(source, null, GameModIntakeStatus.NeedsReview, ex.Message));
            }
        }

        var report = new GameModIntakeScan(result);
        try
        {
            Directory.CreateDirectory(stateRoot);
            SaveAtomic(ReportPath, JsonSerializer.Serialize(report, PrettyJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("MOD-INTAKE", "Could not save local auto-sort review report.", ex);
        }
        return report;
    }

    private async Task ScanSourceAsync(
        string root, IReadOnlyList<GameProfile> games, List<GameModIntakeItem> output,
        HashSet<string> receipts, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var pending = new Queue<(string Folder, int Depth)>();
        pending.Enqueue((root, 0));
        var examined = 0;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = pending.Dequeue();
            RefuseReparseComponents(current);
            var children = Directory.EnumerateFileSystemEntries(current)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntriesPerSource + 1).ToArray();
            foreach (var entry in children)
            {
                ct.ThrowIfCancellationRequested();
                if (++examined > MaxEntriesPerSource)
                {
                    output.Add(new(root, null, GameModIntakeStatus.NeedsReview,
                        "Source scan exceeded its 2048-entry budget. Split this folder into smaller designated sources."));
                    return;
                }
                try
                {
                    RefuseReparseComponents(entry);
                    if (File.Exists(entry))
                    {
                        if (!IsSupportedArchive(entry)) continue;
                        await ClassifyAndRouteAsync(root, entry, false, games, output, receipts, ct).ConfigureAwait(false);
                    }
                    else if (Directory.Exists(entry))
                    {
                        // Collection folders (including nested game-named download directories)
                        // contain archives or child mod folders. They are not one giant mod.
                        var directArchives = Directory.EnumerateFiles(entry).Take(MaxFolderFiles + 1)
                            .Any(IsSupportedArchive);
                        var folders = Directory.EnumerateDirectories(entry).Take(MaxFolderFiles + 1).ToArray();
                        var directFiles = Directory.EnumerateFiles(entry).Take(1).Any();
                        var nativePcChild = folders.Any(path => Path.GetFileName(path).Equals("nativePC", StringComparison.OrdinalIgnoreCase));
                        var container = directArchives || (!directFiles && !nativePcChild && folders.Length > 0);
                        if (container && depth < MaxContainerDepth)
                            pending.Enqueue((entry, depth + 1));
                        else if (container)
                            output.Add(new(entry, null, GameModIntakeStatus.NeedsReview,
                                "Nested collection exceeds the four-level intake limit."));
                        else if (directFiles || nativePcChild)
                            await ClassifyAndRouteAsync(root, entry, true, games, output, receipts, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    output.Add(new(entry, null, GameModIntakeStatus.NeedsReview, ex.Message));
                }
            }
        }
    }

    private async Task ClassifyAndRouteAsync(
        string sourceRoot, string source, bool isDirectory, IReadOnlyList<GameProfile> games,
        List<GameModIntakeItem> output, HashSet<string> receipts, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        string[] paths;
        if (isDirectory)
            paths = SnapshotFolderFiles(source, ct).Select(path => Path.GetRelativePath(source, path)).ToArray();
        else
        {
            var info = await archive.InspectAsync(source, ct).ConfigureAwait(false);
            if (info.HasSuspiciousPaths)
                throw new InvalidDataException("Unsafe archive paths require manual review.");
            if (info.Entries.Any(entry => !entry.IsDirectory && IsSupportedArchive(entry.Key)))
                throw new InvalidDataException(
                    "This pack contains nested mod archives; keep it intact for Universal Archive Importer review.");
            paths = info.Entries.Take(MaxFolderFiles).Select(entry => entry.Key).ToArray();
        }
        var declared = isDirectory ? ReadFolderGameHint(source) : ReadArchiveGameHint(source);
        // The monitored root may be called "NieR Downloads" yet contain unrelated files.
        // Only the package name and its nested relative location provide identity evidence.
        var context = Path.GetRelativePath(sourceRoot, source);
        var detection = GameModIntakeClassifier.Classify(games, context, paths, declared);
        if (detection.Kind != GameModIntakeKind.Identified)
        {
            output.Add(new(source, null, GameModIntakeStatus.NeedsReview, detection.Reason));
            return;
        }
        var owner = games.Single(game => game.Id.Equals(detection.GameId, StringComparison.OrdinalIgnoreCase));
        var library = GameModStorage.Resolve(managerHome, owner);
        GameModStorage.EnsureDirectories(library);
        RefuseReparseComponents(library.InboxRoot);
        var wasCreated = isDirectory
            ? await CopyDirectoryIntoInboxAsync(source, library.InboxRoot, owner.Id, receipts, ct).ConfigureAwait(false)
            : await CopyArchiveIntoInboxAsync(source, library.InboxRoot, owner.Id, receipts, ct).ConfigureAwait(false);
        output.Add(new(source, owner.Id,
            wasCreated ? GameModIntakeStatus.Routed : GameModIntakeStatus.AlreadyPresent,
            wasCreated ? detection.Reason : "Identical package already exists in this game's Inbox."));
    }

    private async Task<bool> CopyArchiveIntoInboxAsync(string source, string inboxRoot, string gameId,
        HashSet<string> receipts, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (new FileInfo(source).Length > MaxArchiveBytes)
            throw new InvalidDataException("Archive exceeds the automatic intake 8 GiB copy limit.");
        var scratch = CreateScratchPath();
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                IoBufferSize, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[IoBufferSize];
                int count;
                long total = 0;
                while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Archive grew beyond the intake copy budget.");
                    digest.AppendData(buffer.AsSpan(0, count));
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            var sha = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            var receiptKey = ReceiptKey(gameId, sha, isDirectory: false);
            if (receipts.Contains(receiptKey)) return false;
            var filename = SafePackageName(source) + "-" + sha + Path.GetExtension(source).ToLowerInvariant();
            var destination = Path.Combine(inboxRoot, filename);
            RefuseReparseComponents(inboxRoot);
            if (File.Exists(destination))
            {
                RefuseReparseComponents(destination);
                await using var existing = File.OpenRead(destination);
                var existingSha = Convert.ToHexString(await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false));
                if (!existingSha.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An existing auto-sorted package is corrupt; it was not overwritten.");
                RememberReceipt(receipts, receiptKey);
                return false;
            }
            File.Move(scratch, destination);
            RememberReceipt(receipts, receiptKey);
            return true;
        }
        finally
        {
            if (File.Exists(scratch)) File.Delete(scratch);
        }
    }

    private async Task<bool> CopyDirectoryIntoInboxAsync(string source, string inboxRoot, string gameId,
        HashSet<string> receipts, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = SnapshotFolderFiles(source, ct);
        var scratch = CreateScratchPath();
        Directory.CreateDirectory(scratch);
        try
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                RefuseReparseComponents(file);
                var relative = Path.GetRelativePath(source, file);
                digest.AppendData(Encoding.UTF8.GetBytes(relative.Replace('\\', '/').ToLowerInvariant() + "\n"));
                var target = Path.Combine(scratch, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    IoBufferSize, FileOptions.Asynchronous);
                var buffer = new byte[IoBufferSize];
                int count;
                while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Folder exceeds the 8 GiB intake copy budget.");
                    digest.AppendData(buffer.AsSpan(0, count));
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                }
            }
            var sha = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            var receiptKey = ReceiptKey(gameId, sha, isDirectory: true);
            if (receipts.Contains(receiptKey)) return false;
            var destination = Path.Combine(inboxRoot, SafePackageName(source) + "-" + sha);
            RefuseReparseComponents(inboxRoot);
            if (Directory.Exists(destination))
            {
                RefuseReparseComponents(destination);
                var other = await HashDirectoryAsync(destination, ct).ConfigureAwait(false);
                if (!other.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An existing auto-sorted folder is corrupt; it was not overwritten.");
                RememberReceipt(receipts, receiptKey);
                return false;
            }
            Directory.Move(scratch, destination);
            RememberReceipt(receipts, receiptKey);
            return true;
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<string> HashDirectoryAsync(string directory, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in SnapshotFolderFiles(directory, ct))
        {
            var relative = Path.GetRelativePath(directory, file);
            digest.AppendData(Encoding.UTF8.GetBytes(relative.Replace('\\', '/').ToLowerInvariant() + "\n"));
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, IoBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[IoBufferSize];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                digest.AppendData(buffer.AsSpan(0, count));
        }
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    private static string[] SnapshotFolderFiles(string folder, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = new List<string>();
        var queue = new Queue<(string Folder, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = queue.Dequeue();
            RefuseReparseComponents(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                ct.ThrowIfCancellationRequested();
                RefuseReparseComponents(entry);
                if (Directory.Exists(entry))
                {
                    if (depth >= MaxContainerDepth)
                        throw new InvalidDataException("Extracted mod folder exceeds the safe intake depth.");
                    queue.Enqueue((entry, depth + 1));
                }
                else if (File.Exists(entry)) files.Add(entry);
                if (files.Count + queue.Count > MaxFolderFiles)
                    throw new InvalidDataException("Extracted mod folder exceeds the 512-entry intake budget.");
            }
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsSupportedArchive(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".rar" or ".7z";
    }

    private static string SafePackageName(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var name = Directory.Exists(source) ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
        var slug = GameProfile.NormalizeId(name);
        return slug[..Math.Min(slug.Length, 48)];
    }

    private string CreateScratchPath()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var root = Path.Combine(stateRoot, "ModIntakeScratch");
        RefuseReparseComponents(stateRoot);
        if (Directory.Exists(root)) RefuseReparseComponents(root);
        Directory.CreateDirectory(root);
        return Path.Combine(root, "intake-" + Guid.NewGuid().ToString("N"));
    }

    private static string? ReadFolderGameHint(string folder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var name in new[] { "mod-intake.json", "mod.json", "manifest.json", "meta.json" })
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path) && new FileInfo(path).Length <= 16384)
            {
                RefuseReparseComponents(path);
                using var input = File.OpenRead(path);
                var hint = ReadBoundedManifestGame(input);
                if (!string.IsNullOrWhiteSpace(hint)) return hint;
            }
        }
        return null;
    }

    private static string? ReadArchiveGameHint(string archive)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        using var zip = ZipFile.OpenRead(archive);
        string? declared = null;
        foreach (var entry in zip.Entries.Take(1024))
        {
            var filename = Path.GetFileName(entry.FullName.Replace('\\', '/'));
            if (filename is null || !new[] { "mod-intake.json", "mod.json", "manifest.json", "meta.json" }
                .Contains(filename, StringComparer.OrdinalIgnoreCase)) continue;
            if (entry.Length > 16384 || entry.CompressedLength > 16384) continue;
            using var input = entry.Open();
            var hint = ReadBoundedManifestGame(input);
            if (string.IsNullOrWhiteSpace(hint)) continue;
            if (declared is not null &&
                !GameProfile.NormalizeId(hint).Equals(GameProfile.NormalizeId(declared),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive declares conflicting target games; manual review required.");
            declared = hint;
        }
        return declared;
    }

    private static string? ReadBoundedManifestGame(Stream input)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        // Advertised sizes in untrusted compressed headers are not an actual-output limit.
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true);
        var buffer = new char[16 * 1024 + 1];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        return length <= 16 * 1024 ? TryReadJsonGame(new string(buffer, 0, length)) : null;
    }

    private static string? TryReadJsonGame(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!new[] { "gameId", "nexusGameDomain", "gameDomain", "game" }
                    .Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private string ValidateSource(string sourceFolder)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        var full = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var volume = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(volume) || full.Equals(volume.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a dedicated downloads/mods folder, not a drive root.");
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Auto-sort source folder does not exist: " + full);
        if (ContainsPath(managerHome, full) || ContainsPath(full, managerHome))
        {
            // The manager root itself, its ancestors, and generated mod folders must not
            // become scan sources; this prevents re-import loops and accidental whole-tree scans.
            throw new InvalidDataException("Auto-sort sources cannot contain or be inside the manager's own library.");
        }
        foreach (var game in loadGames())
            if (ContainsPath(game.GameRoot, full) || ContainsPath(full, game.GameRoot))
                throw new InvalidDataException("Cannot auto-sort from or across a live game installation directory.");
        RefuseReparseComponents(full);
        return full;
    }

    private static bool ContainsPath(string root, string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return canonical.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)
            || canonical.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void RefuseReparseComponents(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new InvalidDataException("Path has no filesystem root.");
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Auto-sort refuses linked/junction source or destination paths.");
        }
    }

    private HashSet<string> LoadReceipts()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var path = Path.Combine(stateRoot, "mod-intake-receipts.json");
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RefuseReparseComponents(path);
        var entries = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Mod intake receipt index was empty.");
        if (entries.Length > 50000 || entries.Any(entry => entry.Length != 64 || entry.Any(ch => !Uri.IsHexDigit(ch))))
            throw new InvalidDataException("Mod intake receipt index is malformed or exceeds its supported limit.");
        return new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReceiptKey(string gameId, string sha, bool isDirectory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var token = gameId + "|" + (isDirectory ? "folder" : "archive") + "|" + sha;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private void RememberReceipt(HashSet<string> receipts, string key)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!receipts.Add(key)) return;
        RefuseReparseComponents(stateRoot);
        Directory.CreateDirectory(stateRoot);
        var path = Path.Combine(stateRoot, "mod-intake-receipts.json");
        RefuseReparseComponents(path);
        SaveAtomic(path, JsonSerializer.Serialize(receipts.Order(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private void SaveSources(IReadOnlyList<string> sources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(stateRoot);
        SaveAtomic(settingsPath, JsonSerializer.Serialize(sources, PrettyJson));
    }

    private static void SaveAtomic(string path, string contents)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var temp = path + ".intake-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
