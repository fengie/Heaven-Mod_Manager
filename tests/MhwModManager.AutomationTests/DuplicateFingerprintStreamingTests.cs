using System.Security.Cryptography;
using System.Text;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

/// <summary>
/// Equivalence oracles for streaming the historic newline-delimited duplicate
/// fingerprint instead of retaining every mod-file path in memory.
/// </summary>
public sealed class DuplicateFingerprintStreamingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-fingerprint-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public DuplicateFingerprintStreamingTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task StreamingFingerprintIsByteForByteEquivalentToLegacyOrderedParts()
    {
        var db = await CreateDatabaseAsync("legacy-equivalence.db");
        var files = new (string Path, string Sha, long Length)[]
        {
            (@"nativePC\pl\zeta.TEX", "SHA-Z", 11),
            (@"nativePC\pl\Alpha.dds", "SHA-A", 22),
            (@"nativePC\pl\MIXED.mod3", "SHA-M", 33)
        };
        for (var i = 0; i < 3; i++)
        {
            var id = $"mod{i}";
            await db.UpsertModAsync(new(id, id, id, Path.Combine(root, id), false, i), Token);
            await db.ReplaceModFilesAsync(id,
                files.Select(f => new ModFileDescriptor(id, f.Path,
                    i == 2 && f.Path.EndsWith("zeta.TEX", StringComparison.Ordinal) ? "different" : f.Sha,
                    null, f.Length, DateTimeOffset.UtcNow, FileClass.Texture)).ToArray(), Token);
        }

        // SQLite mod_files.path has NOCASE collation. The original implementation
        // hashes the path-lowercase|blob-SHA parts joined by LF, not CRLF.
        var ordered = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Path.ToLowerInvariant() + "|" + f.Sha);
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ordered)))).ToLowerInvariant();

        var analysis = await new DuplicateCleanupService(db, Path.Combine(root, "archive")).AnalyzeAsync(Token);
        var group = Assert.Single(analysis.ExactDuplicates);
        Assert.Equal(expected, group.Fingerprint);
        Assert.Equal(2, group.ModIds.Count);
        Assert.Equal("mod0", group.ModIds[0]);
        Assert.Equal("mod1", group.ModIds[1]);
        Assert.Equal("mod1", group.RecommendedKeeperId);
        Assert.Equal(66L, group.ReclaimableBytes);
        Assert.Equal(66L, analysis.ReclaimableBytes);
        Assert.Empty(analysis.SupersededModIds);
    }

    [Fact]
    public async Task LargeIndexedCorpusProducesOneExactGroupWithoutChangingSemantics()
    {
        var db = await CreateDatabaseAsync("large-equivalence.db");
        const int modCount = 128;
        const int filesPerMod = 80;
        for (var i = 0; i < modCount; i++)
        {
            var id = $"mod-{i:D4}";
            await db.UpsertModAsync(new(id, id, id, Path.Combine(root, id), false, i), Token);
            var files = Enumerable.Range(0, filesPerMod)
                .Select(f => new ModFileDescriptor(id, $@"nativePC\pl\armor\file{f:D4}.tex",
                    $"hash-{f:D4}", null, 1024, DateTimeOffset.UtcNow, FileClass.Texture))
                .ToArray();
            await db.ReplaceModFilesAsync(id, files, Token);
        }

        var analysis = await new DuplicateCleanupService(db, Path.Combine(root, "archive")).AnalyzeAsync(Token);
        var group = Assert.Single(analysis.ExactDuplicates);
        Assert.Equal(modCount, group.ModIds.Count);
        Assert.Equal($"mod-{modCount - 1:D4}", group.RecommendedKeeperId);
        Assert.Equal((modCount - 1L) * filesPerMod * 1024, group.ReclaimableBytes);
        Assert.Equal(group.ReclaimableBytes, analysis.ReclaimableBytes);
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name));
        await db.InitializeAsync(Token);
        return db;
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
