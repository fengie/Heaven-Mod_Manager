using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Red-first characterization of deep real-world Stellar Blade collections.
/// These are synthetic inventory entries, never user-supplied mod content.
/// </summary>
public sealed class StellarBladeDeepDiscoveryTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "umm-stellar-boundary-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static string[] Triple(string prefix) =>
    [
        prefix + ".pak",
        prefix + ".utoc",
        prefix + ".ucas"
    ];

    [Fact]
    public void Deep_collection_of_320_mods_is_not_collapsed_by_a_loader_named_wrapper()
    {
        var paths = Enumerable.Range(0, 320)
            .SelectMany(i =>
            {
                var mod = "Outfit-" + i.ToString("D3");
                return Triple($"Mods/Downloads/2026/Stellar Blade Collection/Outfits/{mod}/SB/Content/Paks/~mods/{mod}_P");
            })
            .Append("desktop.ini")
            .Append("Mods/Downloads/2026/Stellar Blade Collection/README.md")
            .ToArray();

        var result = ModBoundaryDiscovery.Discover(paths);
        var folders = result.Candidates.Where(candidate => candidate.Kind == ModBoundaryKind.Folder).ToArray();

        Assert.Equal(320, folders.Length);
        Assert.Equal(320, folders.Select(candidate => candidate.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(folders, candidate => candidate.RelativePath.EndsWith("/Outfit-000", StringComparison.Ordinal));
        Assert.Contains(folders, candidate => candidate.RelativePath.EndsWith("/Outfit-319", StringComparison.Ordinal));
        Assert.Single(result.Candidates.Where(candidate => candidate.Kind == ModBoundaryKind.Review));
    }

    [Fact]
    public void Unattributed_root_file_does_not_mask_several_proven_independent_mod_roots()
    {
        var paths = Triple("Collection/Armor/SB/Content/Paks/~mods/Armor_P")
            .Concat(Triple("Collection/Weapon/SB/Content/Paks/~mods/Weapon_P"))
            .Append("unknown.bin");

        var result = ModBoundaryDiscovery.Discover(paths);
        Assert.Equal(2, result.Candidates.Count(candidate => candidate.Kind == ModBoundaryKind.Folder));
        Assert.Single(result.Candidates.Where(candidate => candidate.Kind == ModBoundaryKind.Review));
    }

    [Fact]
    public void A_single_stellar_blade_mod_with_pak_companions_stays_one_package()
    {
        var result = ModBoundaryDiscovery.Discover(Triple("My Mod/SB/Content/Paks/~mods/Outfit_P"));
        var mod = Assert.Single(result.Candidates);
        Assert.Equal(ModBoundaryKind.Folder, mod.Kind);
        Assert.Equal("My Mod", mod.RelativePath);
    }

    [Fact]
    public void Two_complete_flat_pak_triplets_have_separate_boundaries()
    {
        var paths = Triple("SB/Content/Paks/~mods/Armor_P")
            .Concat(Triple("SB/Content/Paks/~mods/Weapon_P"));
        var result = ModBoundaryDiscovery.Discover(paths);
        Assert.Equal(2, result.Candidates.Count);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Kind == ModBoundaryKind.Review);
    }

    [Fact]
    public void Alternative_flat_pak_triplets_are_not_automatically_imported()
    {
        var paths = Triple("SB/Content/Paks/~mods/Outfit_High_P")
            .Concat(Triple("SB/Content/Paks/~mods/Outfit_Low_P"));
        var result = ModBoundaryDiscovery.Discover(paths);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void Stellar_blade_signature_requires_registered_game_and_not_generic_pak_extension()
    {
        var games = new[]
        {
            GameProfile.Generic("stellar-blade", "Stellar Blade", Path.Combine(home, "StellarBlade"),
                "StellarBlade.exe", "SB/Content/Paks/~mods", nexusGameDomain: "stellarblade"),
            GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"),
                "NieRAutomata.exe", "mods")
        };

        var signature = GameModIntakeClassifier.Classify(games, "Unlabeled",
            ["SB/Content/Paks/~mods/Outfit_P.pak", "SB/Content/Paks/~mods/Outfit_P.utoc"]);
        var generic = GameModIntakeClassifier.Classify(games, "Unlabeled", ["SomeMod.pak"]);

        Assert.Equal(GameModIntakeKind.Identified, signature.Kind);
        Assert.Equal("stellar-blade", signature.GameId);
        Assert.Equal(GameModIntakeKind.Unknown, generic.Kind);
    }

    [Fact]
    public async Task One_shot_deep_collection_routes_individual_mods_and_deduplicates_without_touching_sources()
    {
        var source = Path.Combine(home, "Incoming");
        var sources = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var mod = "Outfit-" + i.ToString("D3");
            var folder = Path.Combine(source, "Mods", "Downloads", "Collection", mod, "SB",
                "Content", "Paks", "~mods");
            Directory.CreateDirectory(folder);
            foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
            {
                var file = Path.Combine(folder, mod + "_P" + extension);
                await File.WriteAllTextAsync(file, mod + extension, TestToken);
                sources.Add(file);
            }
        }
        var managerHome = Path.Combine(home, "Manager");
        var profile = GameProfile.Generic("stellar-blade", "Stellar Blade", Path.Combine(home, "Game"),
            "StellarBlade.exe", "SB/Content/Paks/~mods", nexusGameDomain: "stellarblade");
        var intake = new GameModIntakeService(managerHome, Path.Combine(managerHome, "State"), () => [profile]);

        var first = await intake.ImportPathAsync(source, TestToken);
        Assert.Equal(12, first.Routed);
        Assert.Equal(12, Directory.GetDirectories(Path.Combine(managerHome, "Mods by Game",
            "stellar-blade", "Inbox")).Length);
        Assert.All(sources, path => Assert.True(File.Exists(path)));

        var second = await intake.ImportPathAsync(source, TestToken);
        Assert.Equal(0, second.Routed);
        Assert.Equal(12, second.AlreadyPresent);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
