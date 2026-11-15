using System.IO.Compression;
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
    public void Thousands_of_sibling_mods_are_partitioned_without_losing_deep_children()
    {
        var paths = Enumerable.Range(0, 2_000)
            .SelectMany(i =>
            {
                var name = "Mod-" + i.ToString("D4");
                return Triple($"Mods/Package Vault/2026/{name}/SB/Content/Paks/~mods/{name}_P");
            })
            .ToArray();
        var result = ModBoundaryDiscovery.Discover(paths);
        Assert.Equal(2_000, result.Candidates.Count);
        Assert.All(result.Candidates, candidate => Assert.Equal(ModBoundaryKind.Folder, candidate.Kind));
        Assert.Equal(2_000, result.Candidates.Select(candidate => candidate.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
        Assert.All(result.Candidates, candidate =>
        {
            Assert.Equal(ModBoundaryKind.FileSet, candidate.Kind);
            Assert.NotNull(candidate.Files);
            Assert.Equal(3, candidate.Files.Count);
            Assert.Contains(candidate.Files, path => path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(candidate.Files, path => path.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(candidate.Files, path => path.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(6, result.Candidates.SelectMany(candidate => candidate.Files!).Distinct().Count());
    }

    [Fact]
    public void Readmes_and_case_differences_inside_shared_pak_directory_do_not_hide_complete_groups()
    {
        var paths = Triple("SB/Content/Paks/~mods/Armor_P")
            .Concat(["SB/Content/Paks/~mods/README.md"])
            .Concat(["SB/Content/Paks/~mods/Weapon_P.PAK",
                "SB/Content/Paks/~mods/Weapon_P.UTOC", "SB/Content/Paks/~mods/Weapon_P.UCAS"])
            .ToArray();
        var result = ModBoundaryDiscovery.Discover(paths);
        Assert.Equal(2, result.Candidates.Count(candidate => candidate.Kind == ModBoundaryKind.FileSet));
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void Incomplete_pak_companion_sets_are_not_split_or_imported()
    {
        var paths = Triple("SB/Content/Paks/~mods/Armor_P")
            .Append("SB/Content/Paks/~mods/Weapon_P.pak");
        var result = ModBoundaryDiscovery.Discover(paths);
        Assert.True(result.NeedsReview);
        Assert.Equal(2, result.Candidates.Count(candidate => candidate.Kind == ModBoundaryKind.Review));
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Kind == ModBoundaryKind.FileSet);
    }

    [Fact]
    public void Duplicate_and_case_colliding_archive_entry_names_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => ModBoundaryDiscovery.Discover(
        [
            "SB/Content/Paks/~mods/Outfit_P.pak",
            "sb/content/paks/~mods/outfit_p.PAK"
        ]));
    }

    [Fact]
    public void Collection_wrappers_beyond_32_levels_preserve_independent_mod_packages()
    {
        var wrapper = Enumerable.Range(0, 40).Aggregate("Mods", (path, _) => path + "/d");
        var files = Triple(wrapper + "/Armor/SB/Content/Paks/~mods/Armor_P")
            .Concat(Triple(wrapper + "/Weapon/SB/Content/Paks/~mods/Weapon_P"));
        var result = ModBoundaryDiscovery.Discover(files);
        Assert.Equal(2, result.Candidates.Count(candidate => candidate.Kind == ModBoundaryKind.Folder));
        Assert.False(result.NeedsReview);
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

    [Fact]
    public async Task Archive_of_incomplete_pak_groups_reports_each_group_without_extracting()
    {
        Directory.CreateDirectory(home);
        var sourceArchive = Path.Combine(home, "incomplete.zip");
        using (var zip = ZipFile.Open(sourceArchive, ZipArchiveMode.Create))
        {
            foreach (var file in new[] { "SB/Content/Paks/~mods/Armor_P.pak",
                "SB/Content/Paks/~mods/Weapon_P.pak" })
            {
                using var writer = zip.CreateEntry(file).Open();
                await writer.WriteAsync(new byte[] { 42 }, TestToken);
            }
        }
        var manager = Path.Combine(home, "Manager");
        var profile = GameProfile.Generic("stellar-blade", "Stellar Blade", Path.Combine(home, "Game"),
            "StellarBlade.exe", "SB/Content/Paks/~mods", nexusGameDomain: "stellarblade");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => [profile]);

        var report = await intake.ImportPathAsync(sourceArchive, TestToken);
        Assert.Equal(0, report.Routed);
        Assert.Equal(2, report.NeedsReview);
        Assert.All(report.Items, item => Assert.Contains("!/", item.SourcePath, StringComparison.Ordinal));
        Assert.True(File.Exists(sourceArchive));
        Assert.False(Directory.Exists(Path.Combine(manager, "State", "ModIntakeScratch")));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "stellar-blade", "Inbox")));
    }

    [Fact]
    public async Task Archive_with_two_stellar_blade_pak_triples_stages_separate_complete_mods()
    {
        Directory.CreateDirectory(home);
        var sourceArchive = Path.Combine(home, "StellarBladeMods.zip");
        using (var zip = ZipFile.Open(sourceArchive, ZipArchiveMode.Create))
        {
            foreach (var entryName in Triple("SB/Content/Paks/~mods/Armor_P")
                .Concat(Triple("SB/Content/Paks/~mods/Weapon_P")))
            {
                using var writer = zip.CreateEntry(entryName).Open();
                await writer.WriteAsync(new byte[] { 42 }, TestToken);
            }
        }
        var manager = Path.Combine(home, "Manager");
        var profile = GameProfile.Generic("stellar-blade", "Stellar Blade", Path.Combine(home, "Game"),
            "StellarBlade.exe", "SB/Content/Paks/~mods", nexusGameDomain: "stellarblade");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => [profile]);

        var first = await intake.ImportPathAsync(sourceArchive, TestToken);
        Assert.Equal(2, first.Routed);
        Assert.True(File.Exists(sourceArchive));
        var packages = Directory.GetDirectories(Path.Combine(manager, "Mods by Game", "stellar-blade", "Inbox"));
        Assert.Equal(2, packages.Length);
        Assert.All(packages, package =>
        {
            var files = Directory.GetFiles(package, "*", SearchOption.AllDirectories);
            Assert.Equal(3, files.Length);
            Assert.Contains(files, file => file.EndsWith(".pak", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(files, file => file.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(files, file => file.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase));
        });
        var second = await intake.ImportPathAsync(sourceArchive, TestToken);
        Assert.Equal(2, second.AlreadyPresent);
        Assert.Equal(0, second.Routed);
    }

    [Fact]
    public async Task Unpacked_shared_paks_are_independently_staged_without_copying_other_groups()
    {
        var incoming = Path.Combine(home, "Incoming");
        foreach (var path in Triple("SB/Content/Paks/~mods/Armor_P")
            .Concat(Triple("SB/Content/Paks/~mods/Weapon_P")))
        {
            var original = Path.Combine(incoming, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            await File.WriteAllTextAsync(original, path, TestToken);
        }
        var manager = Path.Combine(home, "Manager");
        var profile = GameProfile.Generic("stellar-blade", "Stellar Blade", Path.Combine(home, "Game"),
            "StellarBlade.exe", "SB/Content/Paks/~mods", nexusGameDomain: "stellarblade");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => [profile]);

        var first = await intake.ImportPathAsync(incoming, TestToken);
        Assert.Equal(2, first.Routed);
        var packages = Directory.GetDirectories(Path.Combine(manager, "Mods by Game", "stellar-blade", "Inbox"));
        Assert.Equal(2, packages.Length);
        Assert.All(packages, package => Assert.Equal(3,
            Directory.GetFiles(package, "*", SearchOption.AllDirectories).Length));
        Assert.Equal(6, Directory.GetFiles(incoming, "*", SearchOption.AllDirectories).Length);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
