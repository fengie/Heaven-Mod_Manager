using System.IO.Compression;
using System.Security.Cryptography;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Correctness oracles for the synthetic workloads used by the opt-in benchmarks.
/// No wall-clock thresholds: hosted-runner timing is not a reliable unit assertion.
/// </summary>
public sealed class SubsystemWorkloadRegressionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "umm-subsystem-workloads-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Large_nested_archive_inventory_retains_every_independent_candidate()
    {
        var paths = Enumerable.Range(0, 1024)
            .Select(index => $"Downloads/Mods/Pack{index:D4}.zip")
            .ToArray();

        var result = ModBoundaryDiscovery.Discover(paths);

        Assert.Equal(paths.Length, result.Candidates.Count);
        Assert.All(result.Candidates, item => Assert.Equal(ModBoundaryKind.Archive, item.Kind));
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public async Task Archive_inspection_extraction_and_authoritative_hash_agree_on_real_bytes()
    {
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "MHW gear.zip");
        var payload = Enumerable.Range(0, 128 * 1024)
            .Select(index => (byte)(index * 31 % 251)).ToArray();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var output = zip.CreateEntry("nativePC/pl/armor/gear.tex").Open();
            await output.WriteAsync(payload, TestToken);
        }

        var inspector = new ArchiveInspector();
        var inspection = await inspector.InspectAsync(archive, TestToken);
        Assert.False(inspection.HasSuspiciousPaths);
        Assert.True(inspection.HasNativePc);
        Assert.Equal(payload.LongLength, inspection.ExpandedBytes);

        var trusted = Path.Combine(root, "extraction");
        Directory.CreateDirectory(trusted);
        var outputDirectory = Path.Combine(trusted, "owned");
        await inspector.ExtractSafelyAsync(archive, outputDirectory, trusted, TestToken);
        var extractedPath = Path.Combine(outputDirectory, "nativePC", "pl", "armor", "gear.tex");
        Assert.Equal(payload, await File.ReadAllBytesAsync(extractedPath, TestToken));

        var hash = await new HashingService().HashFileAsync(extractedPath, true, TestToken);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), hash.Sha256);
        Assert.Equal(payload.LongLength, hash.Length);
    }

    [Fact]
    public async Task Large_mixed_game_archive_folder_routes_once_and_preserves_every_source()
    {
        const int count = 48;
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        for (var i = 0; i < count; i++)
        {
            var isMhw = i % 2 == 0;
            var name = isMhw ? $"MHW Gear {i:D3}.zip" : $"NieR Automata Outfit {i:D3}.zip";
            var entry = isMhw
                ? $"nativePC/pl/armor/armor{i:D3}.tex"
                : $"mods/outfit{i:D3}.dds";
            using var archive = ZipFile.Open(Path.Combine(source, name), ZipArchiveMode.Create);
            using var file = archive.CreateEntry(entry).Open();
            file.WriteByte((byte)i);
        }

        var manager = Path.Combine(root, "manager");
        var games = new GameProfile[]
        {
            GameProfile.MonsterHunterWorld(Path.Combine(root, "games", "MHW")),
            GameProfile.Generic("nier-automata", "NieR:Automata",
                Path.Combine(root, "games", "NieR"), "NieRAutomata.exe", "mods")
        };
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => games);
        var first = await intake.ImportPathsAsync([source], ct: TestToken);
        Assert.Equal(count, first.Routed);
        Assert.Equal(0, first.NeedsReview);

        foreach (var game in games)
        {
            var inbox = Path.Combine(manager, "Mods by Game", game.Id, "Inbox");
            Assert.Equal(count / 2, Directory.GetFiles(inbox, "*.zip").Length);
        }
        Assert.Equal(count, Directory.GetFiles(source, "*.zip").Length);

        var replay = await intake.ImportPathsAsync([source], ct: TestToken);
        Assert.Equal(0, replay.Routed);
        Assert.Equal(count, replay.AlreadyPresent);
        Assert.Equal(count, Directory.GetFiles(source, "*.zip").Length);
    }

    [Fact]
    public async Task Pre_cancelled_batch_does_not_stage_or_delete_downloads()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "MHW gear.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("nativePC/pl/armor/test.tex").Open())
            entry.WriteByte(1);

        var manager = Path.Combine(root, "manager");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"),
            () => [GameProfile.MonsterHunterWorld(Path.Combine(root, "games", "MHW"))]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => intake.ImportPathsAsync([source], ct: cts.Token));
        Assert.True(File.Exists(archive));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
