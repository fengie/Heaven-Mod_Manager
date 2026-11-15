using System.IO.Compression;
using System.Text;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// One selected collection must share a bounded materialization budget across all
/// independently discovered packages. A rejected candidate must not mutate the
/// source or prevent an unrelated smaller candidate from being reviewed/routed.
/// </summary>
public sealed class CollectionBudgetIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-collection-budget-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW"))
    ];

    private static async Task CreateFolderMod(string source, string name, int payloadBytes)
    {
        var folder = Path.Combine(source, name);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "mod-intake.json"),
            "{\"gameId\":\"monster-hunter-world\"}", TestToken);
        await File.WriteAllBytesAsync(Path.Combine(folder, "payload.bin"),
            new byte[payloadBytes], TestToken);
    }

    private static async Task CreateArchiveMod(string source, string name)
    {
        using var zip = ZipFile.Open(Path.Combine(source, name), ZipArchiveMode.Create);
        using (var manifest = zip.CreateEntry("mod-intake.json").Open())
            await manifest.WriteAsync(Encoding.UTF8.GetBytes(
                "{\"gameId\":\"monster-hunter-world\"}"), TestToken);
        using (var payload = zip.CreateEntry("payload.bin").Open())
            await payload.WriteAsync(new byte[48], TestToken);
    }

    [Fact]
    public async Task Distinct_folder_mods_share_one_budget_in_a_selected_collection()
    {
        var source = Path.Combine(home, "Downloads");
        await CreateFolderMod(source, "Armor-MHW", 48);
        await CreateFolderMod(source, "Weapon-MHW", 48);
        var first = Path.Combine(source, "Armor-MHW");
        var firstBytes = Directory.GetFiles(first).Sum(path => new FileInfo(path).Length);

        var manager = Path.Combine(home, "Manager");
        var service = new GameModIntakeService(manager, Path.Combine(manager, "State"),
            () => Games, collectionCopyBudget: firstBytes + 1);
        var result = await service.ImportPathAsync(source, TestToken);

        Assert.Equal(1, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.Contains("cumulative", Assert.Single(result.Items,
            item => item.Status == GameModIntakeStatus.NeedsReview).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetDirectories(Path.Combine(manager, "Mods by Game",
            "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(Path.Combine(source, "Armor-MHW", "payload.bin")));
        Assert.True(File.Exists(Path.Combine(source, "Weapon-MHW", "payload.bin")));
    }

    [Fact]
    public async Task Separate_archives_share_the_same_physical_copy_budget()
    {
        var source = Path.Combine(home, "Downloads");
        Directory.CreateDirectory(source);
        await CreateArchiveMod(source, "Armor-MHW.zip");
        await CreateArchiveMod(source, "Weapon-MHW.zip");
        var firstZip = Path.Combine(source, "Armor-MHW.zip");
        var manager = Path.Combine(home, "Manager");
        var service = new GameModIntakeService(manager, Path.Combine(manager, "State"),
            () => Games, collectionCopyBudget: new FileInfo(firstZip).Length + 1);

        var result = await service.ImportPathAsync(source, TestToken);

        Assert.Equal(1, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.Contains("cumulative", Assert.Single(result.Items,
            item => item.Status == GameModIntakeStatus.NeedsReview).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(Path.Combine(manager, "Mods by Game",
            "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(firstZip));
        Assert.True(File.Exists(Path.Combine(source, "Weapon-MHW.zip")));
    }

    [Fact]
    public async Task An_oversized_earlier_folder_does_not_spend_later_mod_budget()
    {
        var source = Path.Combine(home, "Downloads");
        await CreateFolderMod(source, "A-Large-MHW", 200);
        await CreateFolderMod(source, "B-Small-MHW", 8);
        var smallFolder = Path.Combine(source, "B-Small-MHW");
        var smallBytes = Directory.GetFiles(smallFolder).Sum(path => new FileInfo(path).Length);
        var manager = Path.Combine(home, "Manager");
        var service = new GameModIntakeService(manager, Path.Combine(manager, "State"),
            () => Games, collectionCopyBudget: smallBytes + 1);

        var result = await service.ImportPathAsync(source, TestToken);

        Assert.Equal(1, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.Contains("cumulative", Assert.Single(result.Items,
            item => item.Status == GameModIntakeStatus.NeedsReview).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetDirectories(Path.Combine(manager, "Mods by Game",
            "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(Path.Combine(source, "A-Large-MHW", "payload.bin")));
        Assert.True(File.Exists(Path.Combine(source, "B-Small-MHW", "payload.bin")));
    }

    [Fact]
    public void Custom_collection_budget_can_only_tighten_the_eight_gib_hard_limit()
    {
        var manager = Path.Combine(home, "Manager");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games,
                collectionCopyBudget: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games,
                collectionCopyBudget: 8L * 1024 * 1024 * 1024 + 1));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
