using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A selected collection's inventory ceiling counts visited empty directories
/// as well as files and queued directories, rather than only the current queue.
/// The production ceiling remains 50,000; a smaller injected limit makes these
/// filesystem regressions deterministic and inexpensive on the Windows runner.
/// </summary>
public sealed class FolderInventoryBudgetIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-folder-inventory-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private async Task<string> CreateCollectionAsync(int emptyFolders)
    {
        var source = Path.Combine(home, "Downloads");
        var mod = Path.Combine(source, "MHW Outfit");
        Directory.CreateDirectory(mod);
        await File.WriteAllTextAsync(Path.Combine(mod, "mod-intake.json"),
            "{\"gameId\":\"monster-hunter-world\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(mod, "outfit.bin"), "mod payload", TestToken);
        for (var i = 0; i < emptyFolders; i++)
            Directory.CreateDirectory(Path.Combine(source, $"Empty-docs-{i:D2}"));
        return source;
    }

    private GameModIntakeService CreateIntake(int inventoryEntryBudget)
    {
        var manager = Path.Combine(home, "Manager");
        return new GameModIntakeService(manager, Path.Combine(manager, "State"),
            () => [GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW"))],
            inventoryEntryBudget: inventoryEntryBudget);
    }

    [Fact]
    public async Task Previously_visited_empty_directories_count_toward_total_inventory_and_refuse_publication()
    {
        // The selected root has 1 mod folder, 2 files and 5 empty sibling
        // directories: eight entries total, but never eight queued at once.
        var source = await CreateCollectionAsync(emptyFolders: 5);
        var report = await CreateIntake(inventoryEntryBudget: 7)
            .ImportPathAsync(source, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("file-and-folder safety budget", Assert.Single(report.Items).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game",
            "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(Path.Combine(source, "MHW Outfit", "mod-intake.json")));
        Assert.True(File.Exists(Path.Combine(source, "MHW Outfit", "outfit.bin")));
        Assert.True(Directory.Exists(Path.Combine(source, "Empty-docs-04")));
    }

    [Fact]
    public async Task Exactly_at_inventory_limit_still_routes_identified_mod_without_touching_originals()
    {
        // 1 mod folder + 2 files + 4 empty siblings = exactly seven entries.
        var source = await CreateCollectionAsync(emptyFolders: 4);
        var report = await CreateIntake(inventoryEntryBudget: 7)
            .ImportPathAsync(source, TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal(0, report.NeedsReview);
        var inbox = Path.Combine(home, "Manager", "Mods by Game",
            "monster-hunter-world", "Inbox");
        Assert.Single(Directory.GetDirectories(inbox));
        Assert.True(File.Exists(Path.Combine(source, "MHW Outfit", "outfit.bin")));
        Assert.True(Directory.Exists(Path.Combine(source, "Empty-docs-03")));
    }

    [Fact]
    public void Inventory_budget_can_only_tighten_the_production_fifty_thousand_entry_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateIntake(inventoryEntryBudget: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateIntake(inventoryEntryBudget: 50_001));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
