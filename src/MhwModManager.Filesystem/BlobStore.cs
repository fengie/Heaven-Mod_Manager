using MhwModManager.Core;
using System.Buffers;
using System.IO.Hashing;
using System.Globalization;
using System.Security.Cryptography;
using MhwModManager.Storage;

namespace MhwModManager.Filesystem;

/// <summary>Immutable SHA-256-addressed content store.</summary>
public sealed class BlobStore(string root, ManagerDatabase db, IAtomicReplaceBackend? atomicReplaceBackend = null)
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ExistingVerificationMaxAttempts = 9;

    public string Root { get; } = root;
    public string PathFor(string sha)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Path.Combine(Root, sha.ToLowerInvariant());
    }

    public async Task<string> CaptureAsync(string source, CancellationToken ct = default) =>
        (await CaptureWithHashAsync(source, ct: ct)).Sha256;

    /// <summary>
    /// Copies into a private temp file while calculating SHA-256 + XXH3 in the same pass.
    /// If the CAS already contains the resulting hash, validate its bytes before discarding the temp.
    /// Corrupt existing objects are rejected, never silently trusted or overwritten.
    /// </summary>
    public async Task<HashResult> CaptureWithHashAsync(string source, bool registerInDatabase = true, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"source={source}; register={registerInDatabase}");
        Directory.CreateDirectory(Root);
        var before = new FileInfo(source);
        if (!before.Exists) throw new FileNotFoundException("File disappeared before capture.", source);

        var temp = Path.Combine(Root, ".capture-" + Guid.NewGuid().ToString("N") + ".tmp");
        var xx = new XxHash3();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int n;
                while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    xx.Append(buffer.AsSpan(0, n));
                    sha.AppendData(buffer.AsSpan(0, n));
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                }
                await output.FlushAsync(ct);
                output.Flush(true);
            }

            var after = new FileInfo(source);
            if (!after.Exists || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
                throw new IOException($"Source changed while it was being captured: {source}");

            var shaHex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            var xxHex = Convert.ToHexString(xx.GetCurrentHash()).ToLowerInvariant();
            var dest = PathFor(shaHex);

            if (File.Exists(dest))
            {
                await VerifyExistingAsync(dest, shaHex, ct);
                File.Delete(temp);
            }
            else
            {
                try { File.Move(temp, dest, false); }
                catch (IOException) when (File.Exists(dest))
                {
                    // A name appearing concurrently is not proof of byte-identical content.
                    await VerifyExistingAsync(dest, shaHex, ct);
                    File.Delete(temp);
                }
            }

            if (registerInDatabase)
            {
                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO blobs(sha256,size,created_at,verified_at) VALUES($h,$s,$u,$u)",
                    new Dictionary<string, object?>
                    {
                        ["$h"] = shaHex,
                        ["$s"] = after.Length,
                        ["$u"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    }, ct);
            }

            return new(shaHex, xxHex, after.Length, after.LastWriteTimeUtc);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public async Task RestoreAsync(string sha, string destination, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"sha={sha}; destination={destination}");
        var source = PathFor(sha);
        if (!File.Exists(source)) throw new InvalidDataException($"Required blob is missing: {sha}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await AtomicFileOps.ReplaceFromAsync(source, destination, atomicReplaceBackend, sha, ct);
    }

    private static async Task VerifyExistingAsync(string path, string expectedSha256, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
                if (!StringComparer.OrdinalIgnoreCase.Equals(actual, expectedSha256))
                    throw new InvalidDataException($"CAS integrity failure: existing blob does not match SHA-256 {expectedSha256}.");
                return;
            }
            catch (IOException ex) when (
                attempt + 1 < ExistingVerificationMaxAttempts &&
                (ex.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
            {
                // A competing same-directory rename can make the destination name visible
                // before Windows releases its delete/rename handle. Keep FileShare.Read so
                // verification still excludes mutation; only retry that transient OS window.
                await Task.Delay(1 << attempt, ct);
            }
        }
    }
}
