using System.IO;
using MhwModManager.App;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class SingleInstanceAndGameSwitchTests
{
    [Fact]
    public async Task Second_instance_requests_activation_of_original_owner()
    {
        var unique = "umm-test-" + Guid.NewGuid().ToString("N");
        using var owner = new SingleInstanceCoordinator(unique);
        using var duplicate = new SingleInstanceCoordinator(unique);
        Assert.True(owner.IsPrimary);
        Assert.False(duplicate.IsPrimary);
        var signaled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.StartListening(() => signaled.TrySetResult(true));

        Assert.True(await duplicate.RequestActivationAsync(TestContext.Current.CancellationToken));
        Assert.True(await signaled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void In_process_game_switch_does_not_spawn_a_new_executable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var games = File.ReadAllText(Path.Combine(root!.FullName, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.Games.cs"));
        var app = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App", "App.xaml.cs"));

        Assert.DoesNotContain("RestartIntoGame(", games, StringComparison.Ordinal);
        Assert.DoesNotContain("switch-game-restart", games, StringComparison.Ordinal);
        Assert.Contains("SwitchGameInPlaceAsync(", games, StringComparison.Ordinal);
        Assert.Contains("LoadGameSessionAsync(", app, StringComparison.Ordinal);
        Assert.Contains("previousGame", app, StringComparison.Ordinal);
        // The same WPF MainWindow remains open, so the existing
        // OnMainWindowClose lifetime contract stays valid; no new process/window.
        Assert.Contains("previousWindow.ReplaceGameSessionAsync(candidate", app, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode=ShutdownMode.OnMainWindowClose;", app, StringComparison.Ordinal);
        Assert.Contains("previousWindow", app, StringComparison.Ordinal);
        var window = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App",
            "MainWindow.xaml.cs"));
        Assert.Contains("DataContext = replacement;", window, StringComparison.Ordinal);
        Assert.Contains("DataContext = previousViewModel;", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Initial_app_entry_enforces_owner_before_bootstrap()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var app = File.ReadAllText(Path.Combine(root!.FullName, "src", "MhwModManager.App", "App.xaml.cs"));
        var startup = app.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        var owner = app.IndexOf("new SingleInstanceCoordinator(", startup, StringComparison.Ordinal);
        var firstSplash = app.IndexOf("new StartupWindow()", startup, StringComparison.Ordinal);

        Assert.True(owner > startup && owner < firstSplash,
            "Second launch must hand off before initializing a separate game runtime or opening a splash window.");
        Assert.Contains("RequestActivationAsync(", app, StringComparison.Ordinal);
        Assert.Contains("ActivateExistingWindow(", app, StringComparison.Ordinal);
    }
}
