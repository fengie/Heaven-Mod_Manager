using System.Globalization;
using System.IO.Compression;
using System.Text;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>Regressions for explicitly declared games beyond the first 1,024 ZIP entries.</summary>
public sealed class LargeZipManifestIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "umm-late-zip-game-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW")),
        GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"),
            "NieRAutomata.exe", "mods")
    ];

    [Fact]
    public async Task Late_zip_manifest_routes_the_registered_game_instead_of_the_archive_filename()
    {
        Directory.CreateDirectory(home);
        var selected = Path.Combine(home, "MHW Armor.zip");
        using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
        {
            for (var index = 0; index < 1_025; index++)
            {
                using var note = zip.CreateEntry("docs/note-" +
                    index.ToString("D4", CultureInfo.InvariantCulture) + ".md").Open();
                note.WriteByte(42);
            }
            using (var payload = zip.CreateEntry("OutfitPlugin.dll").Open())
                payload.WriteByte(42);
            using (var manifest = zip.CreateEntry("manifest.json").Open())
                await manifest.WriteAsync(
                    Encoding.UTF8.GetBytes("{\"gameId\":\"nier-automata\"}"), TestToken);
        }
        var intake = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);

        var first = await intake.ImportPathAsync(selected, TestToken);
        Assert.Equal(1, first.Routed);
        Assert.Equal("nier-automata", Assert.Single(first.Items).GameId);
        Assert.Single(Directory.GetFiles(Path.Combine(home, "Mods by Game", "nier-automata", "Inbox")));
        Assert.False(Directory.Exists(Path.Combine(home, "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(selected));

        var replay = await intake.ImportPathAsync(selected, TestToken);
        Assert.Equal(0, replay.Routed);
        Assert.Equal(1, replay.AlreadyPresent);
    }

    [Fact]
    public async Task Conflicting_zip_game_manifests_after_entry_1024_remain_in_review()
    {
        Directory.CreateDirectory(home);
        var selected = Path.Combine(home, "NieR Automata Outfit.zip");
        using (var zip = ZipFile.Open(selected, ZipArchiveMode.Create))
        {
            using (var early = zip.CreateEntry("manifest.json").Open())
                await early.WriteAsync(
                    Encoding.UTF8.GetBytes("{\"gameId\":\"nier-automata\"}"), TestToken);
            for (var index = 0; index < 1_025; index++)
            {
                using var note = zip.CreateEntry("docs/readme-" +
                    index.ToString("D4", CultureInfo.InvariantCulture) + ".md").Open();
                note.WriteByte(42);
            }
            using (var late = zip.CreateEntry("mod.json").Open())
                await late.WriteAsync(
                    Encoding.UTF8.GetBytes("{\"gameId\":\"monster-hunter-world\"}"), TestToken);
            using (var payload = zip.CreateEntry("GenericPayload.bin").Open())
                payload.WriteByte(42);
        }
        var intake = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);

        var result = await intake.ImportPathAsync(selected, TestToken);
        Assert.Equal(0, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.Contains("conflicting target games", Assert.Single(result.Items).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(home, "Mods by Game", "nier-automata", "Inbox")));
        Assert.False(Directory.Exists(Path.Combine(home, "Mods by Game", "monster-hunter-world", "Inbox")));
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
