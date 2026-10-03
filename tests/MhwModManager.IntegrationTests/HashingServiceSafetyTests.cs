using MhwModManager.Filesystem;
using System.Security.Cryptography;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class HashingServiceSafetyTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-hash-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    [Fact]
    public async Task Authoritative_hash_rejects_equal_length_same_timestamp_mutation()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bin");
        await File.WriteAllTextAsync(path, "ORIGINAL", TestToken);
        var originalTimestamp = File.GetLastWriteTimeUtc(path);
        var hashing = new HashingService();

        await Assert.ThrowsAsync<IOException>(() => hashing.HashFileForTestingAsync(
            path,
            authoritative: true,
            async (source, ct) =>
            {
                await File.WriteAllTextAsync(source, "MUTATED!", ct);
                File.SetLastWriteTimeUtc(source, originalTimestamp);
            },
            TestToken));

        Assert.Equal("MUTATED!", await File.ReadAllTextAsync(path, TestToken));
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task Authoritative_hash_of_stable_file_matches_sha256()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bin");
        await File.WriteAllTextAsync(path, "STABLE", TestToken);
        var expected = Convert.ToHexString(SHA256.HashData("STABLE"u8.ToArray())).ToLowerInvariant();

        var result = await new HashingService().HashFileAsync(path, authoritative: true, TestToken);

        Assert.Equal(expected, result.Sha256);
        Assert.NotEmpty(result.Xxh3);
    }

    [Fact]
    public async Task Non_authoritative_hash_does_not_run_certification_read()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bin");
        await File.WriteAllTextAsync(path, "ORIGINAL", TestToken);
        var originalTimestamp = File.GetLastWriteTimeUtc(path);
        var hashing = new HashingService();

        var result = await hashing.HashFileForTestingAsync(
            path,
            authoritative: false,
            async (source, ct) =>
            {
                await File.WriteAllTextAsync(source, "MUTATED!", ct);
                File.SetLastWriteTimeUtc(source, originalTimestamp);
            },
            TestToken);

        Assert.Empty(result.Sha256);
    }

    [Fact]
    public async Task Canceled_authoritative_certification_does_not_return_a_hash()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bin");
        await File.WriteAllBytesAsync(path, new byte[1024 * 1024], TestToken);
        var hashing = new HashingService();
        using var canceled = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hashing.HashFileForTestingAsync(
            path,
            authoritative: true,
            (_, _) =>
            {
                canceled.Cancel();
                return Task.CompletedTask;
            },
            canceled.Token));
    }
}
