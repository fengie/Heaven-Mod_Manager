using System.IO.Compression;
using System.Text;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A recognized but oversized game declaration must never silently fall back
/// to a conflicting file or folder name when selecting a game's Inbox.
/// </summary>
public sealed class OversizedGameManifestIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-oversized-game-hint-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW")),
        GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"),
            "NieRAutomata.exe", "mods")
    ];

    private static string LargeThirdPartyManifest =>
        "{\"gameId\":\"nier-automata\",\"padding\":\"" + new string('x', 17 * 1024) + "\"}";

    [Fact]
    public async Task Large_third_party_folder_manifest_routes_by_its_explicit_game_id()
    {
        var selected = Path.Combine(home, "MHW Armor");
        Directory.CreateDirectory(selected);
        var manifest = Path.Combine(selected, "manifest.json");
        await File.WriteAllTextAsync(manifest, LargeThirdPartyManifest, TestToken);
        await File.WriteAllTextAsync(Path.Combine(selected, "OutfitPlugin.dll"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal(0, report.NeedsReview);
        Assert.Equal("nier-automata", Assert.Single(report.Items).GameId);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.Single(Directory.GetDirectories(Path.Combine(manager, "Mods by Game", "nier-automata", "Inbox")));
        Assert.True(File.Exists(manifest));
    }

    [Fact]
    public async Task Large_third_party_zip_manifest_routes_by_its_explicit_game_id()
    {
        Directory.CreateDirectory(home);
        var selected = Path.Combine(home, "MHW Armor.zip");
        using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
        {
            using (var payload = zip.CreateEntry("OutfitPlugin.dll").Open())
                await payload.WriteAsync(new byte[] { 42 }, TestToken);
            using (var manifest = zip.CreateEntry("manifest.json").Open())
                await manifest.WriteAsync(Encoding.UTF8.GetBytes(LargeThirdPartyManifest), TestToken);
        }

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal(0, report.NeedsReview);
        Assert.Equal("nier-automata", Assert.Single(report.Items).GameId);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.Single(Directory.GetFiles(Path.Combine(manager, "Mods by Game", "nier-automata", "Inbox")));
        Assert.True(File.Exists(selected));
    }

    [Fact]
    public async Task Large_third_party_metadata_without_game_declaration_is_not_a_false_positive()
    {
        var selected = Path.Combine(home, "MHW Armor");
        Directory.CreateDirectory(selected);
        await File.WriteAllTextAsync(Path.Combine(selected, "manifest.json"),
            "{\"description\":\"" + new string('x', 17 * 1024) + "\"}", TestToken);
        var nativePc = Path.Combine(selected, "nativePC");
        Directory.CreateDirectory(nativePc);
        await File.WriteAllTextAsync(Path.Combine(nativePc, "armor.tex"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal(0, report.NeedsReview);
        Assert.Equal("monster-hunter-world", Assert.Single(report.Items).GameId);
        Assert.Single(Directory.GetDirectories(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(Path.Combine(selected, "manifest.json")));
    }

    [Fact]
    public async Task Generic_manifest_above_bounded_metadata_ceiling_requires_review()
    {
        var selected = Path.Combine(home, "MHW Armor");
        Directory.CreateDirectory(selected);
        var manifest = Path.Combine(selected, "manifest.json");
        await File.WriteAllTextAsync(manifest,
            "{\"gameId\":\"nier-automata\",\"padding\":\"" + new string('x', 300 * 1024) + "\"}", TestToken);
        await File.WriteAllTextAsync(Path.Combine(selected, "payload.bin"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("metadata", Assert.Single(report.Items).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(manifest));
    }

    [Fact]
    public async Task Generic_zip_metadata_above_bounded_ceiling_requires_review()
    {
        Directory.CreateDirectory(home);
        var selected = Path.Combine(home, "MHW Armor.zip");
        using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
        {
            using (var payload = zip.CreateEntry("payload.bin").Open())
                await payload.WriteAsync(new byte[] { 42 }, TestToken);
            using (var manifest = zip.CreateEntry("manifest.json").Open())
                await manifest.WriteAsync(Encoding.UTF8.GetBytes(
                    "{\"gameId\":\"nier-automata\",\"padding\":\"" + new string('x', 300 * 1024) + "\"}"), TestToken);
        }

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("metadata", Assert.Single(report.Items).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(selected));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
