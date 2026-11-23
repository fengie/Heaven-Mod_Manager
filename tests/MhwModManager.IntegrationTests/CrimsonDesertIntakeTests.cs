using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Crimson Desert is a PAZ/PAMT game, not an Unreal Paks or MHW nativePC game.
/// Intake may preserve and stage a large collection, but no unverified archive,
/// field patch or native plugin may be installed into the live game tree.
/// </summary>
public sealed class CrimsonDesertIntakeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Selecting_crimson_bin64_executable_preserves_install_root_and_adapter()
    {
        using var fixture = new CrimsonFixture();
        var registry = new GameProfileRegistry(fixture.StateRoot);

        var game = registry.AddGenericFromExecutable(fixture.ExecutablePath, activate: false);

        Assert.Equal("crimson-desert", game.Id);
        Assert.Equal("crimson-desert", game.AdapterId);
        Assert.Equal(Path.GetFullPath(fixture.GameRoot), game.GameRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"bin64\CrimsonDesert.exe", game.ExecutableRelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("3321460", game.SteamAppId);
        Assert.Equal("", game.ModRootRelativePath);
        Assert.True(GameProfileRegistry.IsUsable(game));
    }

    [Fact]
    public void Known_steam_game_candidate_selects_the_same_canonical_crimson_profile()
    {
        using var fixture = new CrimsonFixture();
        var registry = new GameProfileRegistry(fixture.StateRoot, () =>
        [
            new GameDiscoveryCandidate("Crimson Desert Enhanced", fixture.GameRoot, null, "Steam", "3321460")
        ]);

        var games = registry.DiscoverAndRegisterInstalledGamesDetailed().Changed;
        var game = Assert.Single(games);

        Assert.Equal("crimson-desert", game.Id);
        Assert.Equal("crimson-desert", game.AdapterId);
        Assert.Equal(fixture.GameRoot, game.GameRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Steam", game.Store);
    }

    [Fact]
    public void Unverified_crimson_executable_does_not_grant_a_compiled_adapter()
    {
        using var fixture = new CrimsonFixture(writeArchiveEvidence: false);
        var registry = new GameProfileRegistry(fixture.StateRoot);

        var game = registry.AddGenericFromExecutable(fixture.ExecutablePath, activate: false);

        Assert.Equal(fixture.GameRoot, game.GameRoot, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("crimson-desert", game.AdapterId);
    }

    [Fact]
    public void Crimson_adapter_does_not_claim_mhw_semantics_or_live_deployment()
    {
        using var fixture = new CrimsonFixture();
        var game = GameProfile.Generic("crimson-desert", "Crimson Desert", fixture.GameRoot,
            @"bin64\CrimsonDesert.exe", "", adapterId: "crimson-desert", steamAppId: "3321460");
        var adapter = GameAdapters.Resolve(game);

        Assert.Equal("crimson-desert", adapter.Id);
        Assert.False(adapter.SupportsMhwConflictSemantics);
        Assert.Null(adapter.ParseAsset(@"root\0008\0.paz"));
        Assert.Contains(adapter.Validate(game), issue =>
            issue.Blocking && issue.Code == "crimson-deployment-unverified");
    }

    [Fact]
    public void Unverified_crimson_deployment_is_blocked_even_when_files_are_preindexed()
    {
        using var fixture = new CrimsonFixture();
        var game = GameProfile.Generic("crimson-desert", "Crimson Desert", fixture.GameRoot,
            @"bin64\CrimsonDesert.exe", "", adapterId: "crimson-desert", steamAppId: "3321460");
        var mod = new ModDescriptor("test", "Data Patch", "Data Patch", "source", true, 0);
        var file = new ModFileDescriptor("test", @"root\meta\0.papgt",
            new string('a', 64), null, 128, DateTimeOffset.UnixEpoch, FileClass.GameData);
        var snapshot = new PlannerSnapshot([mod], [file], [],
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, DeploymentManifestEntry>(), new Dictionary<string, string?>());

        var plan = new DeploymentPlanner(new ConflictEngine(game), game).Build(snapshot);

        Assert.True(plan.IsBlocked);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Conflicts, conflict =>
            conflict.Blocking && conflict.ReasonCode == "crimson-deployment-unverified");
        Assert.True(File.Exists(Path.Combine(fixture.GameRoot, "meta", "0.papgt")));
    }

    [Fact]
    public async Task Large_explicitly_named_collection_routes_independent_mods_without_touching_sources_or_game()
    {
        using var fixture = new CrimsonFixture();
        const int count = 256;
        var collection = Directory.CreateDirectory(Path.Combine(fixture.Root, "Crimson Desert")).FullName;
        for (var index = 0; index < count; index++)
        {
            var archive = Path.Combine(collection, $"anonymous-{index:D4}.zip");
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            var entry = zip.CreateEntry($"anonymous-{index:D4}.field.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write($$"""{"format":3,"format_minor":1,"name":"Synthetic {{index}}","targets":[{"kind":"table","table":"ItemInfo","ops":[{"key":"id_42","field":"max_count","value":1}]}]}""");
        }

        var game = GameProfile.Generic("crimson-desert", "Crimson Desert", fixture.GameRoot,
            @"bin64\CrimsonDesert.exe", "", adapterId: "crimson-desert", steamAppId: "3321460");
        var intake = new GameModIntakeService(Path.Combine(fixture.Root, "manager"),
            Path.Combine(fixture.Root, "manager-state"), () => [game]);

        var report = await intake.ImportPathsAsync([collection], ct: Ct);

        Assert.Equal(count, report.Routed);
        Assert.Equal(0, report.NeedsReview);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(count, Directory.GetFiles(collection, "*.zip").Length);
        Assert.Equal(count, Directory.GetFiles(Path.Combine(fixture.Root, "manager", "Mods by Game",
            "crimson-desert", "Inbox"), "*.zip").Length);
        Assert.True(File.Exists(Path.Combine(fixture.GameRoot, "meta", "0.papgt")));
        Assert.False(Directory.Exists(Path.Combine(fixture.GameRoot, "mods")));
    }

    private sealed class CrimsonFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "umm-crimson-" + Guid.NewGuid().ToString("N"));
        public string GameRoot => Path.Combine(Root, "game");
        public string StateRoot => Path.Combine(Root, "state");
        public string ExecutablePath => Path.Combine(GameRoot, "bin64", "CrimsonDesert.exe");

        public CrimsonFixture(bool writeArchiveEvidence = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ExecutablePath)!);
            File.WriteAllBytes(ExecutablePath, [0x4d, 0x5a, 0x00]);
            if (writeArchiveEvidence)
            {
                var meta = Directory.CreateDirectory(Path.Combine(GameRoot, "meta")).FullName;
                File.WriteAllText(Path.Combine(meta, "0.papgt"), "test-only bytes");
                File.WriteAllText(Path.Combine(meta, "0.paver"), "test-only version marker");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
