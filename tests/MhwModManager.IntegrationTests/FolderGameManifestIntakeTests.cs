using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Multiple explicit game manifests in one unpacked mod must agree before
/// the package can enter any registered game's Inbox.
/// </summary>
public sealed class FolderGameManifestIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-folder-game-manifests-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW")),
        GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"),
            "NieRAutomata.exe", "mods")
    ];

    [Fact]
    public async Task Conflicting_unpacked_folder_manifests_cannot_silently_route_to_the_first_game()
    {
        var input = Path.Combine(home, "NieR Automata Outfit");
        Directory.CreateDirectory(input);
        var primary = Path.Combine(input, "mod-intake.json");
        var conflicting = Path.Combine(input, "manifest.json");
        await File.WriteAllTextAsync(primary, "{\"gameId\":\"nier-automata\"}", TestToken);
        await File.WriteAllTextAsync(conflicting, "{\"gameId\":\"monster-hunter-world\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(input, "Outfit.bin"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(input, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("conflicting target games", Assert.Single(report.Items).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "nier-automata", "Inbox")));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(primary));
        Assert.True(File.Exists(conflicting));
    }

    [Fact]
    public async Task Consistent_unpacked_folder_manifests_remain_importable_and_idempotent()
    {
        var input = Path.Combine(home, "NieR Automata Outfit");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "mod-intake.json"),
            "{\"gameId\":\"nier-automata\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(input, "manifest.json"),
            "{\"gameId\":\"nier-automata\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(input, "Outfit.bin"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(input, TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal("nier-automata", Assert.Single(report.Items).GameId);
        Assert.Single(Directory.GetDirectories(Path.Combine(manager, "Mods by Game", "nier-automata", "Inbox")));
        Assert.True(File.Exists(Path.Combine(input, "Outfit.bin")));

        var again = await intake.ImportPathAsync(input, TestToken);
        Assert.Equal(0, again.Routed);
        Assert.Equal(1, again.AlreadyPresent);
    }


    [Fact]
    public async Task Oversized_app_owned_folder_manifest_requires_review_instead_of_name_fallback()
    {
        var input = Path.Combine(home, "MHW Armor");
        Directory.CreateDirectory(input);
        var declared = Path.Combine(input, "mod-intake.json");
        var largeJson = "{\"gameId\":\"nier-automata\",\"padding\":\"" +
            new string('x', 16 * 1024) + "\"}";
        await File.WriteAllTextAsync(declared, largeJson, TestToken);
        await File.WriteAllTextAsync(Path.Combine(input, "armor.bin"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(input, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("16 KiB", Assert.Single(report.Items).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(declared));
        Assert.True(File.Exists(Path.Combine(input, "armor.bin")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
