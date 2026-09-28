using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed class UpdatePackageStager(GitHubUpdateSource source, Action<string>? log = null)
{
    private readonly GitHubUpdateSource updateSource = source;
    private readonly Action<string> writeLog = log ?? (_ => { });

    public async Task<StagedUpdate> StageAsync(UpdateCandidate candidate, string token, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={candidate.Manifest.BuildNumber}");
        candidate.Manifest.Validate();
        var updaterRoot = GetUpdaterRoot();
        EnsureUpdaterRoot(updaterRoot);
        var stageRoot = Path.Combine(updaterRoot, "staging",
            $"{candidate.Manifest.BuildNumber}-{Guid.NewGuid():N}");
        UpdatePathSafety.CreateDirectorySafely(updaterRoot, stageRoot);
        var archivePath = Path.Combine(stageRoot, candidate.Manifest.ArtifactName);
        var payloadRoot = Path.Combine(stageRoot, "payload");
        try
        {
            writeLog($"update download started build={candidate.Manifest.BuildNumber}");
            await updateSource.DownloadArtifactAsync(candidate, token, archivePath, ct);
            await ExtractSafelyAsync(archivePath, payloadRoot, ct);
            var productManifestPath = Path.Combine(payloadRoot, UpdateProtocol.ProductManifestFileName);
            await UpdatePackageVerifier.VerifyAsync(
                payloadRoot, candidate.Manifest.ProductManifestSha256, ct);
            var staged = new StagedUpdate(candidate.Manifest, payloadRoot, productManifestPath);
            var pending = Path.Combine(updaterRoot, UpdateProtocol.PendingFileName);
            await WriteJsonAtomicallyAsync(pending, staged, ct);
            writeLog($"update staging completed build={candidate.Manifest.BuildNumber}");
            return staged;
        }
        catch
        {
            try { if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true); } catch { }
            throw;
        }
    }

    public static string GetUpdaterRoot()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new InvalidOperationException("Local application data directory is unavailable.");
        return Path.Combine(local, "MhwModManager", "Updater");
    }

    public static void EnsureUpdaterRoot(string updaterRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={updaterRoot}");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!Directory.Exists(local))
            throw new DirectoryNotFoundException($"Local application data root is missing: {local}");
        var productRoot = Path.Combine(local, "MhwModManager");
        if (Directory.Exists(productRoot) &&
            (File.GetAttributes(productRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater root parent is a reparse point: {productRoot}");
        Directory.CreateDirectory(productRoot);
        if ((File.GetAttributes(productRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater root parent is a reparse point: {productRoot}");
        if (Directory.Exists(updaterRoot) &&
            (File.GetAttributes(updaterRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater root is a reparse point: {updaterRoot}");
        Directory.CreateDirectory(updaterRoot);
        if ((File.GetAttributes(updaterRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Updater root is a reparse point: {updaterRoot}");
    }

    private static async Task ExtractSafelyAsync(string archivePath, string payloadRoot, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"archive={archivePath}");
        var stageRoot = Path.GetDirectoryName(payloadRoot)
                        ?? throw new InvalidOperationException("Payload root has no parent.");
        UpdatePathSafety.CreateDirectorySafely(stageRoot, payloadRoot);
        await using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > UpdateProtocol.MaxArchiveEntries)
            throw new InvalidDataException($"Update archive has too many entries: {archive.Entries.Count}.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalOutput = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (IsSymlink(entry))
                throw new InvalidDataException($"Update archive symbolic-link entry rejected: {entry.FullName}");
            var isDirectory = string.IsNullOrEmpty(entry.Name);
            var normalized = UpdatePathSafety.NormalizeRelativeFilePath(entry.FullName.TrimEnd('/', '\\'));
            if (!seen.Add(normalized))
                throw new InvalidDataException($"Duplicate/case-colliding update archive entry: {entry.FullName}");
            var destination = UpdatePathSafety.CombineUnderRoot(payloadRoot, normalized);
            if (isDirectory)
            {
                UpdatePathSafety.CreateDirectorySafely(payloadRoot, destination);
                continue;
            }

            var parent = Path.GetDirectoryName(destination)
                         ?? throw new InvalidDataException($"Archive entry has no destination parent: {entry.FullName}");
            UpdatePathSafety.CreateDirectorySafely(payloadRoot, parent);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(payloadRoot, parent);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;
                    if (totalOutput > UpdateProtocol.MaxExtractedBytes - read)
                        throw new InvalidDataException("Update archive exceeded the extracted-output resource budget.");
                    totalOutput += read;
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await output.FlushAsync(ct);
                output.Flush(true);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static bool IsSymlink(ZipArchiveEntry entry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"entry={entry.FullName}");
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymlink = 0xA000;
        return ((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymlink;
    }

    internal static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("JSON path has no parent.");
        Directory.CreateDirectory(parent);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, UpdateProtocol.Json), ct);
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}

public static class UpdatePackageVerifier
{
    public static async Task<ProductFileManifest> VerifyAsync(
        string payloadRoot,
        string expectedProductManifestSha256,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"payload={payloadRoot}");
        if (!Directory.Exists(payloadRoot)) throw new DirectoryNotFoundException(payloadRoot);
        if ((File.GetAttributes(payloadRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Staged payload root is a reparse point: {payloadRoot}");
        var manifestPath = Path.Combine(payloadRoot, UpdateProtocol.ProductManifestFileName);
        if (!File.Exists(manifestPath)) throw new InvalidDataException("Staged payload is missing product-files.json.");
        var manifestHash = await HashFileAsync(manifestPath, ct);
        if (!string.Equals(manifestHash, expectedProductManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Product manifest SHA-256 mismatch. Expected {expectedProductManifestSha256}, actual {manifestHash}.");
        var manifest = JsonSerializer.Deserialize<ProductFileManifest>(
                           await File.ReadAllTextAsync(manifestPath, ct), UpdateProtocol.Json)
                       ?? throw new InvalidDataException("Product manifest is empty.");
        manifest.Validate();

        var expected = new HashSet<string>(
            manifest.Files.Select(f => UpdatePathSafety.NormalizeRelativeFilePath(f.Path)),
            StringComparer.OrdinalIgnoreCase)
        {
            UpdateProtocol.ProductManifestFileName
        };
        var actual = EnumerateFilesSafely(payloadRoot, ct)
            .Select(f => UpdatePathSafety.NormalizeRelativeFilePath(Path.GetRelativePath(payloadRoot, f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(expected))
        {
            var unexpected = actual.Except(expected, StringComparer.OrdinalIgnoreCase).Take(5);
            var missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase).Take(5);
            throw new InvalidDataException(
                $"Staged payload file set does not match product manifest. Unexpected=[{string.Join(", ", unexpected)}] Missing=[{string.Join(", ", missing)}]");
        }

        foreach (var entry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var path = UpdatePathSafety.CombineUnderRoot(payloadRoot, entry.Path);
            UpdatePathSafety.EnsureExistingComponentsNotReparse(payloadRoot, path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != entry.Size)
                throw new InvalidDataException($"Staged product file size mismatch: {entry.Path}");
            var hash = await HashFileAsync(path, ct);
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Staged product file SHA-256 mismatch: {entry.Path}");
        }
        return manifest;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"root={root}");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Staged payload contains a reparse point: {entry}");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else yield return entry;
            }
        }
    }
}
