using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class IntakeReceiptCapacityTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-intake-receipt-capacity-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_receipt_index_refuses_new_package_before_publication_but_allows_replay(bool archive)
    {
        var manager = Path.Combine(home, "Manager");
        var state = Path.Combine(manager, "State");
        Directory.CreateDirectory(state);
        var index = Path.Combine(state, "mod-intake-receipts.json");
        var seed = Enumerable.Range(0, 49_999)
            .Select(i => i.ToString("x64", CultureInfo.InvariantCulture)).ToArray();
        await File.WriteAllTextAsync(index, JsonSerializer.Serialize(seed), TestToken);
        var first = await CreatePackageAsync("First-MHW", "first payload", archive);
        var second = await CreatePackageAsync("Second-MHW", "second payload", archive);
        var service = new GameModIntakeService(manager, state,
            () => [GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW"))]);

        var accepted = await service.ImportPathAsync(first, TestToken);
        Assert.Equal(1, accepted.Routed);
        var inbox = Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox");
        var published = Assert.Single(Directory.GetFileSystemEntries(inbox));
        var fullIndex = await File.ReadAllBytesAsync(index, TestToken);
        Assert.Equal(50_000, JsonSerializer.Deserialize<string[]>(fullIndex)!.Length);

        var rejected = await service.ImportPathAsync(second, TestToken);
        Assert.Equal(0, rejected.Routed);
        Assert.Equal(1, rejected.NeedsReview);
        Assert.Contains("receipt index", Assert.Single(rejected.Items).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(published, Assert.Single(Directory.GetFileSystemEntries(inbox)));
        Assert.Equal(fullIndex, await File.ReadAllBytesAsync(index, TestToken));

        var replay = await service.ImportPathAsync(first, TestToken);
        Assert.Equal(1, replay.AlreadyPresent);
        Assert.Equal(published, Assert.Single(Directory.GetFileSystemEntries(inbox)));
        Assert.True(archive ? File.Exists(first) && File.Exists(second)
            : Directory.Exists(first) && Directory.Exists(second));
    }

    [Fact]
    public async Task Null_receipt_entry_is_rejected_as_invalid_index()
    {
        var manager = Path.Combine(home, "Manager");
        var state = Path.Combine(manager, "State");
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(Path.Combine(state, "mod-intake-receipts.json"), "[null]", TestToken);
        var package = await CreatePackageAsync("MHW Mod", "payload", archive: false);
        var service = new GameModIntakeService(manager, state,
            () => [GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW"))]);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportPathAsync(package, TestToken));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(Directory.Exists(package));
    }

    private async Task<string> CreatePackageAsync(string name, string payload, bool archive)
    {
        var downloads = Path.Combine(home, "Downloads");
        Directory.CreateDirectory(downloads);
        if (archive)
        {
            var path = Path.Combine(downloads, name + ".zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("nativePC/payload.tex").Open());
            await writer.WriteAsync(payload.AsMemory(), TestToken);
            return path;
        }
        var folder = Path.Combine(downloads, name);
        var nativePc = Path.Combine(folder, "nativePC");
        Directory.CreateDirectory(nativePc);
        await File.WriteAllTextAsync(Path.Combine(nativePc, "payload.tex"), payload, TestToken);
        return folder;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
