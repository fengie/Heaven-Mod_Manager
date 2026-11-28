using System.IO.Compression;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ArchiveManifestLimitsTests : IDisposable
{
    private readonly string home = Directory.CreateTempSubdirectory("umm-archive-manifest-").FullName;
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Directory_entries_count_toward_inspection_and_extraction_limits()
    {
        var archive = CreateArchive("over.zip", "a/", "b/", "c/");
        var inspector = new ArchiveInspector(maxEntries: 3);
        var target = Path.Combine(home, "over");

        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.InspectAsync(archive, TestToken));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            inspector.ExtractSafelyAsync(archive, target, home, TestToken));
        Assert.False(Directory.Exists(target));
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task Exact_entry_limit_accepts_safe_directory_metadata_and_payload()
    {
        var archive = CreateArchive("exact.zip", "a/", "b/");
        var inspector = new ArchiveInspector(maxEntries: 3);
        var target = Path.Combine(home, "exact");

        Assert.Equal(3, (await inspector.InspectAsync(archive, TestToken)).Entries.Count);
        await inspector.ExtractSafelyAsync(archive, target, home, TestToken);
        Assert.Equal("payload", await File.ReadAllTextAsync(
            Path.Combine(target, "payload.bin"), TestToken));
    }

    [Theory]
    [InlineData("../outside/")]
    [InlineData("payload.bin/")]
    public async Task Unsafe_or_colliding_directory_entry_refuses_entire_extraction(string directory)
    {
        var archive = CreateArchive("invalid.zip", directory);
        var target = Path.Combine(home, "invalid");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ArchiveInspector().ExtractSafelyAsync(archive, target, home, TestToken));
        Assert.False(Directory.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(home, "outside")));
        Assert.True(File.Exists(archive));
    }

    private string CreateArchive(string name, params string[] directories)
    {
        var path = Path.Combine(home, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("payload.bin").Open()))
            writer.Write("payload");
        foreach (var directory in directories)
            zip.CreateEntry(directory);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
