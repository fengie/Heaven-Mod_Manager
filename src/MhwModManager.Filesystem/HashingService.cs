using MhwModManager.Core;
using System.Buffers;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;

namespace MhwModManager.Filesystem;

public sealed record HashResult(string Sha256,string Xxh3,long Length,DateTimeOffset LastWriteUtc);

public sealed class HashingService
{
    private const int BufferSize = 256 * 1024;

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance service is intentionally injectable and shared across hashing consumers/tests.")]
    public Task<HashResult> HashFileAsync(string path, bool authoritative = true, CancellationToken ct = default) =>
        HashFileCoreAsync(path, authoritative, afterInitialRead: null, ct);

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Test seam remains on the injectable service surface for focused source-mutation regressions.")]
    internal Task<HashResult> HashFileForTestingAsync(
        string path,
        bool authoritative,
        Func<string, CancellationToken, Task> afterInitialRead,
        CancellationToken ct = default) =>
        HashFileCoreAsync(path, authoritative, afterInitialRead, ct);

    private static async Task<HashResult> HashFileCoreAsync(
        string path,
        bool authoritative,
        Func<string, CancellationToken, Task>? afterInitialRead,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"path={path}; authoritative={authoritative}");
        var before = new FileInfo(path);
        if (!before.Exists) throw new FileNotFoundException("File disappeared while hashing.", path);
        var beforeLength = before.Length;
        var beforeWrite = before.LastWriteTimeUtc;

        var xx = new XxHash3();
        using var sha = authoritative ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            int n;
            while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                xx.Append(buffer.AsSpan(0,n));
                sha?.AppendData(buffer.AsSpan(0,n));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var shaBytes = sha?.GetHashAndReset();
        if (afterInitialRead is not null)
            await afterInitialRead(path, ct);

        if (shaBytes is not null)
        {
            await using var verificationStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var verificationSha = await SHA256.HashDataAsync(verificationStream, ct);
            if (!CryptographicOperations.FixedTimeEquals(shaBytes, verificationSha))
                throw new IOException($"File changed while it was being hashed: {path}");
        }

        var after = new FileInfo(path);
        if (!after.Exists || after.Length != beforeLength || after.LastWriteTimeUtc != beforeWrite)
            throw new IOException($"File changed while it was being hashed: {path}");

        var fast = Convert.ToHexString(xx.GetCurrentHash()).ToLowerInvariant();
        var crypt = shaBytes is null ? string.Empty : Convert.ToHexString(shaBytes).ToLowerInvariant();
        return new(crypt,fast,after.Length,after.LastWriteTimeUtc);
    }
}
