using MhwModManager.App;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ManagerSettingsTests
{
    [Fact]
    public void DefaultsPreserveExistingConvenienceAndSafetyBehavior()
    {
        var settings = new ManagerSettings();

        Assert.True(settings.AutoUpdateEnabled);
        Assert.True(settings.UiAnimationsEnabled);
        Assert.True(settings.RememberLastTab);
        Assert.True(settings.ConfirmBeforeApply);
        Assert.True(settings.ConfirmBeforeDiscardStaged);
        Assert.True(settings.BackgroundMetadataRefreshEnabled);
        Assert.Equal(0, settings.LastSelectedTab);
    }

    [Fact]
    public void SettingsRoundTripThroughPersistentStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhw-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var store = ManagerSettingsStore.Load(path);
            store.Update(settings =>
            {
                settings.AutoUpdateEnabled = false;
                settings.UiAnimationsEnabled = false;
                settings.RememberLastTab = true;
                settings.LastSelectedTab = 7;
                settings.ConfirmBeforeApply = false;
                settings.ConfirmBeforeDiscardStaged = false;
                settings.BackgroundMetadataRefreshEnabled = false;
            });

            var reloaded = ManagerSettingsStore.Load(path).Current;
            Assert.False(reloaded.AutoUpdateEnabled);
            Assert.False(reloaded.UiAnimationsEnabled);
            Assert.True(reloaded.RememberLastTab);
            Assert.Equal(7, reloaded.LastSelectedTab);
            Assert.False(reloaded.ConfirmBeforeApply);
            Assert.False(reloaded.ConfirmBeforeDiscardStaged);
            Assert.False(reloaded.BackgroundMetadataRefreshEnabled);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MalformedSettingsFallBackToSafeDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhw-settings-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{ definitely not valid json");

            var settings = ManagerSettingsStore.Load(path).Current;

            Assert.True(settings.AutoUpdateEnabled);
            Assert.True(settings.ConfirmBeforeApply);
            Assert.True(settings.ConfirmBeforeDiscardStaged);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SettingsUiKeepsManualUpdaterAvailableWhenAutomationIsOptional()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var updater = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Updater.cs"));
        var settingsVm = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Settings.cs"));

        Assert.Contains("<TabItem Header=\"Settings\">", xaml);
        Assert.Contains("IsChecked=\"{Binding AutoUpdateEnabled,Mode=TwoWay}\"", xaml);
        Assert.Contains("Command=\"{Binding CheckForProgramUpdatesCommand}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding UiAnimationsEnabled,Mode=TwoWay}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding RememberLastTab,Mode=TwoWay}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding ConfirmBeforeApply,Mode=TwoWay}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding ConfirmBeforeDiscardStaged,Mode=TwoWay}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding BackgroundMetadataRefreshEnabled,Mode=TwoWay}\"", xaml);
        Assert.Contains("programUpdaterStarted || !AutoUpdateEnabled", updater);
        Assert.Contains("CheckAndStageProgramUpdateAsync(manual: true", updater);
        Assert.Contains("Manual checks remain available.", settingsVm);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
