using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A deliberate game choice can route one review-only, structurally independent
/// package, but cannot turn a collection or installer into a guessed single mod.
/// </summary>
public sealed class ManualGameAssignmentIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-manual-game-assignment-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW")),
        GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"),
            "NieRAutomata.exe", "mods")
    ];

    private GameModIntakeService CreateService()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var manager = Path.Combine(home, "Manager");
        return new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
    }

    [Fact]
    public async Task Unknown_standalone_folder_can_be_assigned_only_to_a_registered_game_and_replayed()
    {
        var selected = Path.Combine(home, "Unknown Outfit");
        Directory.CreateDirectory(selected);
        var original = Path.Combine(selected, "plugin.dll");
        await File.WriteAllTextAsync(original, "payload", TestToken);
        var service = CreateService();
        Assert.Equal(1, (await service.ImportPathAsync(selected, TestToken)).NeedsReview);

        var result = await service.AssignReviewedPackageAsync(selected, "nier-automata", TestToken);
        Assert.Equal(1, result.Routed);
        Assert.Equal("nier-automata", Assert.Single(result.Items).GameId);
        Assert.Contains("manually", Assert.Single(result.Items).Reason, StringComparison.OrdinalIgnoreCase);
        var inbox = Path.Combine(home, "Manager", "Mods by Game", "nier-automata", "Inbox");
        Assert.Single(Directory.GetDirectories(inbox));
        Assert.True(File.Exists(original));
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game",
            "monster-hunter-world", "Inbox")));

        var repeat = await service.AssignReviewedPackageAsync(selected, "nier-automata", TestToken);
        Assert.Equal(0, repeat.Routed);
        Assert.Equal(1, repeat.AlreadyPresent);
    }

    [Fact]
    public async Task Unknown_single_archive_can_be_assigned_with_original_untouched()
    {
        Directory.CreateDirectory(home);
        var selected = Path.Combine(home, "Unknown Mod.zip");
        using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
        {
            using var payload = zip.CreateEntry("plugin.dll").Open();
            await payload.WriteAsync(new byte[] { 42 }, TestToken);
        }
        var service = CreateService();
        Assert.Equal(1, (await service.ImportPathAsync(selected, TestToken)).NeedsReview);

        var result = await service.AssignReviewedPackageAsync(selected, "nier-automata", TestToken);
        Assert.Equal(1, result.Routed);
        Assert.True(File.Exists(selected));
        var inbox = Path.Combine(home, "Manager", "Mods by Game", "nier-automata", "Inbox");
        Assert.Single(Directory.GetFiles(inbox));
    }

    [Fact]
    public async Task Multiple_sibling_mods_cannot_be_collapsed_by_manual_game_choice()
    {
        var source = Path.Combine(home, "Mixed");
        var first = Path.Combine(source, "Mod A");
        var second = Path.Combine(source, "Mod B");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        await File.WriteAllTextAsync(Path.Combine(first, "mod-intake.json"),
            "{\"gameId\":\"monster-hunter-world\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(second, "mod-intake.json"),
            "{\"gameId\":\"nier-automata\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(first, "a.bin"), "a", TestToken);
        await File.WriteAllTextAsync(Path.Combine(second, "b.bin"), "b", TestToken);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AssignReviewedPackageAsync(source, "nier-automata", TestToken));
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game")));
        Assert.True(File.Exists(Path.Combine(first, "a.bin")));
        Assert.True(File.Exists(Path.Combine(second, "b.bin")));
    }

    [Fact]
    public async Task Fomod_options_remain_unrouted_despite_explicit_game_choice()
    {
        var selected = Path.Combine(home, "Installer");
        var fomod = Path.Combine(selected, "fomod");
        Directory.CreateDirectory(fomod);
        await File.WriteAllTextAsync(Path.Combine(fomod, "ModuleConfig.xml"), "<config />", TestToken);
        await File.WriteAllTextAsync(Path.Combine(selected, "payload.dll"), "payload", TestToken);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AssignReviewedPackageAsync(selected, "nier-automata", TestToken));
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game")));
    }

    [Fact]
    public async Task Manual_assignment_refuses_invalid_games_and_high_confidence_overrides()
    {
        var selected = Path.Combine(home, "MHW Armor");
        var nativePc = Path.Combine(selected, "nativePC");
        Directory.CreateDirectory(nativePc);
        await File.WriteAllTextAsync(Path.Combine(nativePc, "armor.tex"), "payload", TestToken);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AssignReviewedPackageAsync(selected, "unknown-unregistered-game", TestToken));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AssignReviewedPackageAsync(selected, "nier-automata", TestToken));
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game")));
    }

    [Fact]
    public async Task Manual_assignment_never_reads_from_a_live_game_or_manager_library()
    {
        var liveMod = Path.Combine(home, "MHW", "nativePC");
        Directory.CreateDirectory(liveMod);
        await File.WriteAllTextAsync(Path.Combine(liveMod, "file.dll"), "payload", TestToken);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.AssignReviewedPackageAsync(liveMod, "nier-automata", TestToken));
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
