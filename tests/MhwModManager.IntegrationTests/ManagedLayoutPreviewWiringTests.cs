using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Previously tracked collections must be inspectable even when not eligible for
/// automatic splitting. Inspection must never alter original mod files or game state.
/// </summary>
public sealed class ManagedLayoutPreviewWiringTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-layout-preview-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_sifu_collection_reports_separate_companion_groups_without_importing()
    {
        var manager = Path.Combine(home, "Manager");
        var source = Path.Combine(manager, "Mods by Game", "sifu", "Installed", "BulkCollection");
        Directory.CreateDirectory(source);
        var originals = new List<string>();
        foreach (var name in new[] { "SkinOne", "SkinTwo" })
        foreach (var ext in new[] { ".pak", ".sig" })
        {
            var path = Path.Combine(source, "Nested", "Downloads", name + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, name + ext, TestToken);
            originals.Add(path);
        }
        var game = GameProfile.Generic("sifu", "Sifu", Path.Combine(home, "Sifu"),
            "Sifu.exe", "Sifu/Content/Paks/~mods");
        var intake = new GameModIntakeService(manager, Path.Combine(manager, "State"), () => [game]);

        var inspected = intake.PreviewManagedCollectionLayout(source, game, TestToken);
        Assert.Equal(4, inspected.FileCount);
        Assert.Equal(2, inspected.Groups.Count);
        Assert.All(inspected.Groups, group =>
        {
            Assert.Equal(ModLayoutGroupKind.CompanionPattern, group.Kind);
            Assert.Equal(2, group.Files.Count);
        });
        Assert.Equal(4, inspected.Groups.Sum(group => group.Files.Count));

        // The existing automatic importer remains conservative. Layout patterns
        // alone do not authorize automatic staging, install or deletion.
        var import = await intake.ImportManagedCollectionAsync(source, game, TestToken);
        Assert.Equal(0, import.Routed);
        Assert.All(originals, path => Assert.True(File.Exists(path)));
        Assert.False(Directory.Exists(Path.Combine(manager, "Mods by Game", "sifu", "Inbox")));
    }

    [Fact]
    public void Existing_mods_screen_exposes_read_only_layout_review()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "src",
            "MhwModManager.App", "MainWindow.xaml"));
        var vm = File.ReadAllText(Path.Combine(root.FullName, "src",
            "MhwModManager.App", "ViewModels", "MainWindowViewModel.ManagedCollections.cs"));
        var window = File.ReadAllText(Path.Combine(root.FullName, "src",
            "MhwModManager.App", "ModLayoutReviewWindow.cs"));
        Assert.Contains("Inspect Existing Mod Layouts", xaml);
        Assert.Contains("InspectExistingModCollectionsCommand", xaml);
        Assert.Contains("PreviewManagedCollectionLayout", vm);
        Assert.Contains("new ModLayoutReviewWindow(", vm);
        Assert.Contains("No original files are changed", window);
    }

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }
}
