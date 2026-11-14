using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A dumped collection must be separated by package identity, not its enclosing folder.
/// Unknown or optional files must never be silently installed as another mod.
/// </summary>
public sealed class ModBoundaryDiscoveryTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "umm-mod-boundaries-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW")),
        GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"), "NieRAutomata.exe", "mods")
    ];

    [Fact]
    public void Two_independent_package_roots_are_not_combined_with_a_readme()
    {
        var result = ModBoundaryDiscovery.Discover(
        [
            "Armor A/nativePC/pl/armor/first.tex",
            "Weapon B/nativePC/pl/wp/second.tex",
            "README.md"
        ]);

        Assert.Equal(new[] { "Armor A", "Weapon B" }, result.Candidates.Select(x => x.RelativePath));
        Assert.All(result.Candidates, candidate => Assert.Equal(ModBoundaryKind.Folder, candidate.Kind));
    }

    [Fact]
    public void Nested_collection_wrapper_does_not_hide_mod_boundaries()
    {
        var result = ModBoundaryDiscovery.Discover(
        [
            "Downloads/Collection/Armor A/nativePC/pl/first.tex",
            "Downloads/Collection/Weapon B/nativePC/wp/second.tex"
        ]);

        Assert.Equal(new[]
        {
            "Downloads/Collection/Armor A",
            "Downloads/Collection/Weapon B"
        }, result.Candidates.Select(x => x.RelativePath));
    }

    [Fact]
    public void One_mod_with_multiple_payload_subfolders_stays_one_mod()
    {
        var result = ModBoundaryDiscovery.Discover(
        [
            "nativePC/pl/armor/first.tex",
            "nativePC/pl/wp/second.tex",
            "config/settings.json",
            "README.txt"
        ]);

        var only = Assert.Single(result.Candidates);
        Assert.Equal(".", only.RelativePath);
        Assert.Equal(ModBoundaryKind.Folder, only.Kind);
    }

    [Fact]
    public void Installer_variants_are_not_misidentified_as_independent_mods()
    {
        var fomod = ModBoundaryDiscovery.Discover(
        [
            "fomod/ModuleConfig.xml",
            "options/Armor A/nativePC/armor.tex",
            "options/Armor B/nativePC/armor.tex"
        ]);
        Assert.Equal(ModBoundaryKind.Review, Assert.Single(fomod.Candidates).Kind);

        var alternatives = ModBoundaryDiscovery.Discover(
        [
            "High Resolution/nativePC/armor.tex",
            "Low Resolution/nativePC/armor.tex"
        ]);
        Assert.Equal(ModBoundaryKind.Review, Assert.Single(alternatives.Candidates).Kind);
    }

    [Fact]
    public void Separate_nested_archives_are_independent_inputs()
    {
        var result = ModBoundaryDiscovery.Discover(
        [
            "Pack/MHW Armor.zip",
            "Pack/NieR Outfit.7z",
            "Pack/Readme.txt"
        ]);

        Assert.Equal(new[] { "Pack/MHW Armor.zip", "Pack/NieR Outfit.7z" },
            result.Candidates.Select(x => x.RelativePath));
        Assert.All(result.Candidates, candidate => Assert.Equal(ModBoundaryKind.Archive, candidate.Kind));
    }

    [Fact]
    public void Unattributed_root_files_stay_in_review_instead_of_disappearing()
    {
        var result = ModBoundaryDiscovery.Discover(["ModA.dll", "ModB.dll"]);
        Assert.Equal(ModBoundaryKind.Review, Assert.Single(result.Candidates).Kind);
    }

    [Fact]
    public async Task One_shot_folder_import_separates_nested_mods_and_keeps_originals()
    {
        var input = Path.Combine(home, "incoming");
        var bundle = Path.Combine(input, "Bundle");
        var a = Path.Combine(bundle, "MHW Armor", "nativePC", "pl", "armor", "armor.tex");
        var b = Path.Combine(bundle, "MHW Weapon", "nativePC", "pl", "wp", "weapon.tex");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        await File.WriteAllTextAsync(a, "armor", TestToken);
        await File.WriteAllTextAsync(b, "weapon", TestToken);
        await File.WriteAllTextAsync(Path.Combine(bundle, "README.md"), "mod collection", TestToken);

        var service = new GameModIntakeService(Path.Combine(home, "Manager"), Path.Combine(home, "Manager", "State"), () => Games);
        var first = await service.ImportPathAsync(input, TestToken);
        Assert.Equal(2, first.Routed);
        var inbox = Path.Combine(home, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        Assert.Equal(2, Directory.GetDirectories(inbox).Length);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));

        var repeat = await service.ImportPathAsync(input, TestToken);
        Assert.Equal(0, repeat.Routed);
        Assert.Equal(2, repeat.AlreadyPresent);
    }

    [Fact]
    public async Task Multi_mod_zip_imports_two_independent_packages_without_discarding_source()
    {
        var input = Path.Combine(home, "outer.zip");
        Directory.CreateDirectory(home);
        CreateZip(input,
            "Armor A/nativePC/pl/armor/armor.tex",
            "Weapon B/nativePC/pl/wp/weapon.tex",
            "README.md");

        var service = new GameModIntakeService(Path.Combine(home, "Manager"), Path.Combine(home, "Manager", "State"), () => Games);
        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(2, result.Routed);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(home, "Manager", "Mods by Game", "monster-hunter-world", "Inbox")).Length);
        Assert.True(File.Exists(input));
        var again = await service.ImportPathAsync(input, TestToken);
        Assert.Equal(2, again.AlreadyPresent);
        Assert.Equal(0, again.Routed);
    }

    [Fact]
    public async Task Mixed_game_nested_archives_are_routed_per_child_not_per_outer_pack()
    {
        var input = Path.Combine(home, "outer.zip");
        Directory.CreateDirectory(home);
        var mhw = CreateZipBytes("nativePC/pl/armor/armor.tex");
        var nier = CreateZipBytes("mods/outfit.dds");
        using (var outer = ZipFile.Open(input, ZipArchiveMode.Create))
        {
            using (var entry = outer.CreateEntry("MHW Armor.zip").Open())
                await entry.WriteAsync(mhw, TestToken);
            using (var entry = outer.CreateEntry("NieR Automata Outfit.zip").Open())
                await entry.WriteAsync(nier, TestToken);
        }

        var service = new GameModIntakeService(Path.Combine(home, "Manager"), Path.Combine(home, "Manager", "State"), () => Games);
        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(2, result.Routed);
        Assert.Single(Directory.GetFiles(Path.Combine(home, "Manager", "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.Single(Directory.GetFiles(Path.Combine(home, "Manager", "Mods by Game", "nier-automata", "Inbox")));
        Assert.True(File.Exists(input));
    }

    [Fact]
    public async Task Fomod_archive_is_held_for_review_without_publication()
    {
        var input = Path.Combine(home, "fomod.zip");
        Directory.CreateDirectory(home);
        CreateZip(input, "fomod/ModuleConfig.xml", "options/Armor/nativePC/pl/armor.tex");
        var service = new GameModIntakeService(Path.Combine(home, "Manager"), Path.Combine(home, "Manager", "State"), () => Games);

        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(0, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.True(File.Exists(input));
    }

    [Fact]
    public async Task Loose_unknown_files_do_not_hide_independent_archive_mods()
    {
        var input = Path.Combine(home, "mixed");
        Directory.CreateDirectory(input);
        CreateZip(Path.Combine(input, "MHW Armor.zip"), "nativePC/pl/armor/armor.tex");
        await File.WriteAllTextAsync(Path.Combine(input, "unknown.bin"), "unidentified", TestToken);
        var service = new GameModIntakeService(Path.Combine(home, "Manager"),
            Path.Combine(home, "Manager", "State"), () => Games);

        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(1, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.True(File.Exists(Path.Combine(input, "unknown.bin")));
        Assert.True(File.Exists(Path.Combine(input, "MHW Armor.zip")));
    }

    [Fact]
    public async Task Mixed_bundle_holds_fomod_child_without_blocking_independent_mods()
    {
        var input = Path.Combine(home, "mixed-installer.zip");
        Directory.CreateDirectory(home);
        CreateZip(input, "Fomod Child/fomod/ModuleConfig.xml",
            "Fomod Child/options/Armor/nativePC/armor.tex",
            "Separate Armor/nativePC/pl/armor/armor.tex");
        var service = new GameModIntakeService(Path.Combine(home, "Manager"),
            Path.Combine(home, "Manager", "State"), () => Games);

        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(1, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.True(File.Exists(input));
        Assert.Single(Directory.GetDirectories(Path.Combine(home, "Manager", "Mods by Game",
            "monster-hunter-world", "Inbox")));
    }

    [Fact]
    public async Task A_single_unpacked_mod_root_with_multiple_game_payload_files_remains_one_package()
    {
        var input = Path.Combine(home, "single");
        var a = Path.Combine(input, "nativePC", "pl", "armor", "armor.tex");
        var b = Path.Combine(input, "nativePC", "pl", "wp", "weapon.tex");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        await File.WriteAllTextAsync(a, "armor", TestToken);
        await File.WriteAllTextAsync(b, "weapon", TestToken);
        var service = new GameModIntakeService(Path.Combine(home, "Manager"),
            Path.Combine(home, "Manager", "State"), () => Games);

        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(1, result.Routed);
        Assert.Single(Directory.GetDirectories(Path.Combine(home, "Manager", "Mods by Game",
            "monster-hunter-world", "Inbox")));
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task Unsafe_archive_entries_never_publish_packages()
    {
        var input = Path.Combine(home, "unsafe.zip");
        Directory.CreateDirectory(home);
        CreateZip(input, "../escape.dll", "MHW Armor/nativePC/pl/armor/armor.tex");
        var service = new GameModIntakeService(Path.Combine(home, "Manager"),
            Path.Combine(home, "Manager", "State"), () => Games);

        var result = await service.ImportPathAsync(input, TestToken);

        Assert.Equal(0, result.Routed);
        Assert.Equal(1, result.NeedsReview);
        Assert.True(File.Exists(input));
        Assert.False(Directory.Exists(Path.Combine(home, "Manager", "Mods by Game",
            "monster-hunter-world", "Inbox")));
        Assert.False(File.Exists(Path.Combine(home, "escape.dll")));
    }

    private static void CreateZip(string path, params string[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in entries)
        {
            using var entry = archive.CreateEntry(file).Open();
            entry.WriteByte(42);
        }
    }

    private static byte[] CreateZipBytes(string entry)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry(entry).Open();
            stream.WriteByte(42);
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
