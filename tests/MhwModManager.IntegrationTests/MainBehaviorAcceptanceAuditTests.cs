using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Regression probes first run against the v8.8.101 baseline where all four failed.
/// Their implementation shipped through PRs #789 and #790. Preserve the behavioral
/// assertions on integrated main; do not remove tests to hide regressions.
/// </summary>
public sealed class MainBehaviorAcceptanceAuditTests
{
    [Fact]
    public void Second_launch_must_activate_the_first_instance_before_creating_a_splash()
    {
        var source = ReadSource("src", "MhwModManager.App", "App.xaml.cs");
        var entry = source.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        var splash = source.IndexOf("new StartupWindow()", entry, StringComparison.Ordinal);
        var ownership = source.IndexOf("new SingleInstanceCoordinator(", entry, StringComparison.Ordinal);

        Assert.True(entry >= 0 && splash > entry);
        Assert.True(ownership > entry && ownership < splash,
            "A second launch currently reaches a new app startup before claiming single-instance ownership.");
        Assert.Contains("RequestActivationAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_registered_games_must_keep_the_same_process()
    {
        var source = ReadSource("src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Games.cs");

        Assert.DoesNotContain("RestartIntoGame(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("switch-game-restart", source, StringComparison.Ordinal);
        Assert.Contains("SwitchGameInPlaceAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Mod_filters_and_bulk_controls_must_not_require_horizontal_scrolling()
    {
        var source = ReadSource("src", "MhwModManager.App", "MainWindow.xaml");
        var document = XDocument.Parse(source);
        var modsTab = document.Descendants().Single(element =>
            element.Name.LocalName == "TabItem" &&
            (string?)element.Attribute("Header") == "Mods");
        var scrollBars = modsTab.Descendants()
            .Where(element => element.Name.LocalName == "ScrollViewer"
                && (string?)element.Attribute("HorizontalScrollBarVisibility") == "Auto")
            .ToArray();

        Assert.Empty(scrollBars);
        Assert.Contains(modsTab.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Content") == "Select All");
        Assert.Contains(modsTab.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Content") == "Deselect All");
    }

    [Fact]
    public void Auto_populate_must_process_newly_downloaded_game_inbox_before_planning()
    {
        var source = ReadSource("src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs");
        var start = source.IndexOf("private async Task AutoPopulate()", StringComparison.Ordinal);
        var finish = source.IndexOf("internal static Task<T> RunAutoPopulateBuildAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && finish > start);
        var action = source[start..finish];
        var inbox = action.IndexOf("s.Inbox.ProcessAsync(", StringComparison.Ordinal);
        var build = action.IndexOf("service.BuildAsync(", StringComparison.Ordinal);

        Assert.True(inbox >= 0 && build > inbox,
            "Auto Populate only plans indexed mods. It does not ingest the game's pending Inbox first.");
    }

    private static string ReadSource(params string[] paths)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "MhwModManager.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, Path.Combine(paths)));
    }
}
