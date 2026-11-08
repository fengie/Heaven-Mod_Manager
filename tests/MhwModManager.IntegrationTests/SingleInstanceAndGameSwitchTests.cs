using System.IO;
using MhwModManager.App;
using MhwModManager.Core;
using MhwModManager.Filesystem;
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
    public void Candidate_registration_must_not_activate_a_game_before_session_commit()
    {
        var root = Path.Combine(Path.GetTempPath(), "umm-switch-lease-" + Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(root, "First");
        var secondRoot = Path.Combine(root, "Second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var secondExe = Path.Combine(secondRoot, "SecondGame.exe");
        File.WriteAllBytes(secondExe, [0x4d, 0x5a]);
        try
        {
            var registry = new GameProfileRegistry(Path.Combine(root, "State"));
            var first = GameProfile.Generic("first-game", "First Game", firstRoot, "First.exe", "Mods");
            registry.Upsert(first);
            registry.SetActive(first.Id);

            var candidate = registry.AddGenericFromExecutable(secondExe, activate: false);
            Assert.NotEqual(first.Id, candidate.Id);
            Assert.Equal(first.Id, registry.GetActive()?.Id);
            Assert.Contains(registry.Load(), profile => profile.Id == candidate.Id);

            // Preserve the original public registry behavior for existing callers.
            var activated = registry.AddGenericFromExecutable(secondExe);
            Assert.Equal(activated.Id, registry.GetActive()?.Id);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Relinking_an_inactive_candidate_must_not_activate_it_before_commit()
    {
        var root = Path.Combine(Path.GetTempPath(), "umm-relink-lease-" + Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(root, "First");
        var oldRoot = Path.Combine(root, "OldSecond");
        var newRoot = Path.Combine(root, "RelinkedSecond");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(newRoot);
        var executable = Path.Combine(newRoot, "SecondGame.exe");
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        try
        {
            var registry = new GameProfileRegistry(Path.Combine(root, "State"));
            var first = GameProfile.Generic("first-game", "First Game", firstRoot, "First.exe", "Mods");
            var second = GameProfile.Generic("second-game", "Second Game", oldRoot, "SecondGame.exe", "Mods");
            registry.Upsert(first);
            registry.Upsert(second);
            registry.SetActive(first.Id);

            var repaired = registry.RepairFromExecutable(second, executable, activate: false);
            Assert.Equal(second.Id, repaired.Id);
            Assert.Equal(first.Id, registry.GetActive()?.Id);
            Assert.True(File.Exists(repaired.ExecutablePath));
            Assert.Equal(repaired, registry.Load().Single(p => p.Id == second.Id));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
