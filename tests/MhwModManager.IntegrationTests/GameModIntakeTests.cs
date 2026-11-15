using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>Regression contract: auto-sort uses registered game identity, not the active game.</summary>
public sealed class GameModIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "umm-game-intake-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(home, "MHW")),
        GameProfile.Generic("nier-automata", "NieR:Automata", Path.Combine(home, "NieR"), "NieRAutomata.exe", "mods"),
        GameProfile.Generic("another-unity-game", "Another Unity Game", Path.Combine(home, "Unity"), "UnityGame.exe", "BepInEx/plugins")
    ];

    [Fact]
    public void Recognizes_unique_game_signatures_but_not_shared_mod_extensions()
    {
        var games = Games;
        var mhw = GameModIntakeClassifier.Classify(games, "AnonymousMod.zip",
            ["ModFolder/nativePC/pl/wp/wp000.tex"]);
        var nier = GameModIntakeClassifier.Classify(games, "NieR Automata Outfit.zip",
            ["mods/outfit.dds"]);
        var unknown = GameModIntakeClassifier.Classify(games, "pretty-graphics.zip",
            ["BepInEx/plugins/PrettyGraphics.dll"]);
        var conflict = GameModIntakeClassifier.Classify(games, "NieR Automata Outfit.zip",
            ["nativePC/pl/wp/wp000.tex"]);

        Assert.Equal(GameModIntakeKind.Identified, mhw.Kind);
        Assert.Equal("monster-hunter-world", mhw.GameId);
        Assert.Equal(GameModIntakeKind.Identified, nier.Kind);
        Assert.Equal("nier-automata", nier.GameId);
        Assert.Equal(GameModIntakeKind.Unknown, unknown.Kind);
        Assert.Equal(GameModIntakeKind.Ambiguous, conflict.Kind);
    }

    [Fact]
    public void Explicit_provider_game_identity_matches_only_registered_games()
    {
        var games = Games;
        var nier = GameModIntakeClassifier.Classify(games, "untitled.zip",
            ["mods/generic.bin"], "nier-automata");
        var nexus = GameModIntakeClassifier.Classify(games, "untitled.zip",
            ["mods/generic.bin"], "monsterhunterworld");
        var invalid = GameModIntakeClassifier.Classify(games, "untitled.zip",
            ["mods/generic.bin"], "unregistered-game");

        Assert.Equal("nier-automata", nier.GameId);
        Assert.Equal("monster-hunter-world", nexus.GameId);
        Assert.Null(invalid.GameId);
        Assert.Equal(GameModIntakeKind.Unknown, invalid.Kind);
    }

    [Fact]
    public async Task Routes_both_games_from_one_designated_source_and_preserves_originals()
    {
        var source = Path.Combine(Path.GetTempPath(), "umm-intake-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            CreateZip(Path.Combine(source, "MHW Armor.zip"), "nativePC/pl/armor/test.tex");
            CreateZip(Path.Combine(source, "NieR Automata Outfit.zip"), "mods/outfit.dds");
            CreateZip(Path.Combine(source, "Unknown.zip"), "BepInEx/plugins/unknown.dll");

            var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);
            sorter.AddSource(source);
            Assert.Contains(source, sorter.LoadSources());

            var result = await sorter.ScanAsync(TestToken);
            Assert.Equal(2, result.Routed);
            Assert.Equal(1, result.NeedsReview);
            Assert.Single(Directory.GetFiles(Path.Combine(home, "Mods by Game", "monster-hunter-world", "Inbox")));
            Assert.Single(Directory.GetFiles(Path.Combine(home, "Mods by Game", "nier-automata", "Inbox")));
            Assert.False(Directory.Exists(Path.Combine(home, "Mods by Game", "another-unity-game", "Inbox")));
            Assert.True(File.Exists(Path.Combine(source, "MHW Armor.zip")));
            Assert.True(File.Exists(Path.Combine(source, "NieR Automata Outfit.zip")));
            Assert.True(File.Exists(Path.Combine(source, "Unknown.zip")));

            // Smart Inbox archives processed imports. A second intake scan must not
            // reintroduce them just because the destination Inbox is now empty.
            foreach (var id in new[] { "monster-hunter-world", "nier-automata" })
            {
                var inbox = Path.Combine(home, "Mods by Game", id, "Inbox");
                var processed = Path.Combine(inbox, "Processed");
                Directory.CreateDirectory(processed);
                foreach (var file in Directory.GetFiles(inbox))
                    File.Move(file, Path.Combine(processed, Path.GetFileName(file)));
            }

            var again = await sorter.ScanAsync(TestToken);
            Assert.Equal(0, again.Routed);
            Assert.Equal(2, again.AlreadyPresent);
            Assert.Empty(Directory.GetFiles(Path.Combine(home, "Mods by Game", "nier-automata", "Inbox")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task Nested_folder_mods_are_detected_without_scanning_unrelated_directories()
    {
        var source = Path.Combine(Path.GetTempPath(), "umm-intake-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, "Downloads", "Nier Mods"));
        try
        {
            CreateZip(Path.Combine(source, "Downloads", "Nier Mods", "outfit.zip"), "mods/outfit.dds");
            var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);
            sorter.AddSource(source);

            var report = await sorter.ScanAsync(TestToken);

            Assert.Equal(1, report.Routed);
            Assert.Single(Directory.GetFiles(Path.Combine(home, "Mods by Game", "nier-automata", "Inbox")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task Extracted_folder_mod_is_routed_without_moving_source_files()
    {
        var source = Path.Combine(Path.GetTempPath(), "umm-intake-unpacked-" + Guid.NewGuid().ToString("N"));
        var unpacked = Path.Combine(source, "An MHW armor mod");
        var payload = Path.Combine(unpacked, "nativePC", "pl", "armor", "armor.tex");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        await File.WriteAllTextAsync(payload, "native content", TestToken);
        try
        {
            var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);
            sorter.AddSource(source);

            var result = await sorter.ScanAsync(TestToken);

            Assert.Equal(1, result.Routed);
            var inbox = Path.Combine(home, "Mods by Game", "monster-hunter-world", "Inbox");
            var copied = Assert.Single(Directory.GetDirectories(inbox));
            Assert.True(File.Exists(Path.Combine(copied, "nativePC", "pl", "armor", "armor.tex")));
            Assert.True(File.Exists(payload));

            // Processed Inbox entries disappear from the Inbox root; the source must
            // not be reimported by the next startup or filesystem event.
            var processed = Path.Combine(inbox, "Processed");
            Directory.CreateDirectory(processed);
            Directory.Move(copied, Path.Combine(processed, Path.GetFileName(copied)));
            var again = await sorter.ScanAsync(TestToken);
            Assert.Equal(0, again.Routed);
            Assert.Equal(1, again.AlreadyPresent);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task Source_folder_name_alone_does_not_make_every_archive_a_mod_for_that_game()
    {
        var source = Path.Combine(Path.GetTempPath(), "NieR Automata Downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            CreateZip(Path.Combine(source, "Unknown.zip"), "BepInEx/plugins/unknown.dll");
            var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);
            sorter.AddSource(source);

            var result = await sorter.ScanAsync(TestToken);

            Assert.Equal(0, result.Routed);
            Assert.Equal(1, result.NeedsReview);
            Assert.False(Directory.Exists(Path.Combine(home, "Mods by Game", "nier-automata", "Inbox")));
            Assert.True(File.Exists(Path.Combine(source, "Unknown.zip")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void Malformed_or_oversized_saved_sources_fail_with_bounded_recoverable_errors()
    {
        var state = Path.Combine(home, "State");
        Directory.CreateDirectory(state);
        var configuration = Path.Combine(state, "mod-intake-folders.json");
        var sorter = new GameModIntakeService(home, state, () => Games);

        File.WriteAllText(configuration, "{broken JSON");
        Assert.Throws<InvalidDataException>(() => sorter.LoadSources());

        File.WriteAllText(configuration, new string('x', 32768));
        Assert.Throws<InvalidDataException>(() => sorter.LoadSources());
    }

    [Fact]
    public async Task Archive_with_nested_mod_archives_is_queued_for_bundle_review_instead_of_misrouted()
    {
        var source = Path.Combine(Path.GetTempPath(), "umm-intake-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            CreateZip(Path.Combine(source, "MHW bundle.zip"), "DifferentGame.zip");
            var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);
            sorter.AddSource(source);

            var report = await sorter.ScanAsync(TestToken);

            Assert.Equal(0, report.Routed);
            Assert.Equal(1, report.NeedsReview);
            Assert.False(Directory.Exists(Path.Combine(home, "Mods by Game", "monster-hunter-world", "Inbox")));
            Assert.True(File.Exists(Path.Combine(source, "MHW bundle.zip")));
        }
        finally { Directory.Delete(source, recursive: true); }
    }

    [Fact]
    public void Refuses_scanning_manager_owned_or_drive_root_directories()
    {
        var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => Games);
        Directory.CreateDirectory(home);
        Assert.Throws<InvalidDataException>(() => sorter.AddSource(home));
        var root = Path.GetPathRoot(home)!;
        Assert.Throws<InvalidDataException>(() => sorter.AddSource(root));
    }

    [Fact]
    public void Dashboard_exposes_designated_source_management_and_scan_status()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "src", "MhwModManager.App", "MainWindow.xaml"));
        var vm = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.AutoSort.cs"));
        Assert.Contains("AddAutoSortFolderCommand", xaml);
        Assert.Contains("RemoveAutoSortFolderCommand", xaml);
        Assert.Contains("ScanAutoSortFoldersCommand", xaml);
        Assert.Contains("StartAutoSorting", vm);
    }

    private static void CreateZip(string path, string internalFile)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var entryStream = zip.CreateEntry(internalFile).Open();
        entryStream.WriteByte(42);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
