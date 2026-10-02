using MhwModManager.Automation;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class SaveBackupServiceTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhw-save-backup-" + Guid.NewGuid().ToString("N"));

    public SaveBackupServiceTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Stable_copy_is_published_without_partial_files()
    {
        var source = Path.Combine(root, "SAVEDATA1000");
        var destinationRoot = Path.Combine(root, "snapshot");
        var destination = Path.Combine(destinationRoot, "SAVEDATA1000");
        Directory.CreateDirectory(destinationRoot);
        var bytes = Enumerable.Range(0, 128 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        await File.WriteAllBytesAsync(source, bytes, TestToken);

        await SaveBackupService.CopyFileAsync(source, destination, TestToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestToken));
        Assert.Empty(Directory.EnumerateFiles(destinationRoot, "*.partial-*"));
    }

    [Fact]
    public async Task Same_length_source_mutation_never_publishes_torn_destination()
    {
        var source = Path.Combine(root, "SAVEDATA1000");
        var destinationRoot = Path.Combine(root, "unstable-snapshot");
        var destination = Path.Combine(destinationRoot, "SAVEDATA1000");
        Directory.CreateDirectory(destinationRoot);
        var bytes = Enumerable.Repeat((byte)0x11, 128 * 1024).ToArray();
        await File.WriteAllBytesAsync(source, bytes, TestToken);

        var mutationCount = 0;
        await Assert.ThrowsAsync<IOException>(() =>
            SaveBackupService.CopyFileAsync(
                source,
                destination,
                TestToken,
                attempt =>
                {
                    mutationCount++;
                    var replacement = Enumerable.Repeat(
                        (byte)(0x20 + attempt),
                        bytes.Length).ToArray();
                    File.WriteAllBytes(source, replacement);
                }));

        Assert.Equal(3, mutationCount);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(destinationRoot, "*.partial-*"));
    }
}
