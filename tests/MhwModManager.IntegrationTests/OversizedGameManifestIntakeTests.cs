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

    private static string OversizedManifest =>
        "{\"gameId\":\"nier-automata\",\"padding\":\"" + new string('x', 17 * 1024) + "\"}";

    [Fact]
    public async Task Oversized_unpacked_manifest_must_not_route_by_a_misleading_folder_name()
    {
        var selected = Path.Combine(home, "MHW Armor");
        Directory.CreateDirectory(selected);
        var manifest = Path.Combine(selected, "manifest.json");
        await File.WriteAllTextAsync(manifest, OversizedManifest, TestToken);
        await File.WriteAllTextAsync(Path.Combine(selected, "OutfitPlugin.dll"), "payload", TestToken);

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("manifest", Assert.Single(report.Items).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "nier-automata", "Inbox")));
        Assert.True(File.Exists(manifest));
    }

    [Fact]
    public async Task Oversized_zip_manifest_must_not_route_by_a_misleading_archive_name()
    {
        Directory.CreateDirectory(home);
        var selected = Path.Combine(home, "MHW Armor.zip");
        using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
        {
            using (var payload = zip.CreateEntry("OutfitPlugin.dll").Open())
                await payload.WriteAsync(new byte[] { 42 }, TestToken);
            using (var manifest = zip.CreateEntry("manifest.json").Open())
                await manifest.WriteAsync(Encoding.UTF8.GetBytes(OversizedManifest), TestToken);
        }

        var manager = Path.Combine(home, "Manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => Games);
        var report = await intake.ImportPathAsync(selected, TestToken);

        Assert.Equal(0, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Contains("manifest", Assert.Single(report.Items).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "nier-automata", "Inbox")));
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
