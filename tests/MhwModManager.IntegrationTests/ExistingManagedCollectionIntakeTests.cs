using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Reanalysis of already indexed single-package folders must discover independent nested
/// mods without deleting, moving or applying the original tracked source package.
/// </summary>
public sealed class ExistingManagedCollectionIntakeTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-managed-collection-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private string Manager => Path.Combine(home, "Manager");

    private GameProfile Game => GameProfile.Generic("stellar-blade", "Stellar Blade",
        Path.Combine(home, "Game"), "StellarBlade.exe", "SB/Content/Paks/~mods",
        nexusGameDomain: "stellarblade");

    private GameModIntakeService Service() =>
        new(Manager, Path.Combine(Manager, "State"), () => [Game]);

    private string Managed(string name) => Path.Combine(Manager, "Mods by Game",
        "stellar-blade", "Installed", name);

    [Fact]
    public async Task Existing_deep_collection_is_split_into_independent_inbox_mods_and_replayed_without_touching_source()
    {
        var source = Managed("fav");
        var originals = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var name = $"Outfit-{i:D3}";
            var folder = Path.Combine(source, "Downloads", "Archive", "Outfits", name,
                "SB", "Content", "Paks", "~mods");
            Directory.CreateDirectory(folder);
            foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
            {
                var file = Path.Combine(folder, name + "_P" + extension);
                await File.WriteAllTextAsync(file, name + extension, TestToken);
                originals.Add(file);
            }
        }

        var service = Service();
        var preview = service.PreviewManagedCollection(source, Game, TestToken);
        Assert.Equal(12, preview.Candidates.Count(candidate => candidate.Kind == ModBoundaryKind.Folder));

        var first = await service.ImportManagedCollectionAsync(source, Game, TestToken);
        Assert.Equal(12, first.Routed);
        var inbox = Path.Combine(Manager, "Mods by Game", "stellar-blade", "Inbox");
        var packages = Directory.GetDirectories(inbox);
        Assert.Equal(12, packages.Length);
        Assert.All(packages, package =>
        {
            Assert.Equal(3, Directory.GetFiles(package, "*", SearchOption.AllDirectories).Length);
            Assert.True(Directory.Exists(Path.Combine(package, "SB", "Content", "Paks", "~mods")));
            Assert.False(Directory.Exists(Path.Combine(package, "Downloads")));
        });
        Assert.All(originals, original => Assert.True(File.Exists(original)));
        Assert.True(Directory.Exists(source));

        var replay = await service.ImportManagedCollectionAsync(source, Game, TestToken);
        Assert.Equal(0, replay.Routed);
        Assert.Equal(12, replay.AlreadyPresent);
        Assert.Equal(12, Directory.GetDirectories(inbox).Length);
    }

    [Fact]
    public async Task One_existing_mod_is_not_duplicated_or_published_as_a_collection()
    {
        var source = Managed("One mod");
        var payload = Path.Combine(source, "SB", "Content", "Paks", "~mods", "One_P.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        await File.WriteAllTextAsync(payload, "one", TestToken);
        var result = await Service().ImportManagedCollectionAsync(source, Game, TestToken);
        Assert.Equal(0, result.Routed);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(payload));
        Assert.False(Directory.Exists(Path.Combine(Manager, "Mods by Game", "stellar-blade", "Inbox")));
    }

    [Fact]
    public async Task Foreign_or_live_game_sources_cannot_use_the_managed_collection_exception()
    {
        var service = Service();
        var foreign = Path.Combine(home, "Not Managed", "Mods");
        Directory.CreateDirectory(foreign);
        var live = Path.Combine(Game.GameRoot, "SB", "Content", "Paks", "~mods");
        Directory.CreateDirectory(live);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportManagedCollectionAsync(foreign, Game, TestToken));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ImportManagedCollectionAsync(live, Game, TestToken));
        Assert.False(Directory.Exists(Path.Combine(Manager, "Mods by Game", "stellar-blade", "Inbox")));
    }

    [Fact]
    public void Mods_screen_exposes_an_opt_in_existing_collection_rescan()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "src",
            "MhwModManager.App", "MainWindow.xaml"));
        var vm = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.ManagedCollections.cs"));
        Assert.Contains("ReanalyzeManagedCollectionsCommand", xaml);
        Assert.Contains("PreviewManagedCollection", vm);
        Assert.Contains("ImportManagedCollectionAsync", vm);
        Assert.Contains("MessageBoxResult.Yes", vm);
    }

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }
}
