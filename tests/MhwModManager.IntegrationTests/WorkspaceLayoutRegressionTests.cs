using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MhwModManager.App;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class WorkspaceLayoutRegressionTests
{
    [Fact]
    public void UnknownDuplicateAndInvalidSavedLayoutValuesRecoverWithoutLosingTabs()
    {
        var recovered = WorkspaceLayout.Normalize(new WorkspaceLayout(
            ["ModsTab", "retired-tab", "ModsTab", "DashboardTab"], double.NaN, 10000));
        Assert.Equal("ModsTab", recovered.NavigationOrder[0]);
        Assert.Equal("DashboardTab", recovered.NavigationOrder[1]);
        Assert.Equal(WorkspaceLayout.Default.NavigationOrder.Order(), recovered.NavigationOrder.Order());
        Assert.Equal(WorkspaceLayout.DefaultSidebarWidth, recovered.SidebarWidth);
        Assert.Equal<double?>(WorkspaceLayout.MaximumToolsHeight, recovered.ModToolsHeight);
        Assert.Equal(WorkspaceLayout.MinimumSidebarWidth,
            WorkspaceLayout.Normalize(new WorkspaceLayout([], -50, -50)).SidebarWidth);
        Assert.Null(WorkspaceLayout.Normalize(new WorkspaceLayout([], 200, double.NaN)).ModToolsHeight);
    }

    [Fact]
    public void ReorderAndResetPersistOnlyPresentationPreferences()
    {
        var root = Directory.CreateTempSubdirectory("manager-workspace-");
        var path = Path.Combine(root.FullName, "settings.json");
        try
        {
            var store = ManagerSettingsStore.Load(path);
            var order = WorkspaceLayout.Reorder(WorkspaceLayout.Default.NavigationOrder, "HistoryTab", "ModsTab", false);
            Assert.Equal("HistoryTab", order[1]);
            Assert.Equal("ModsTab", order[2]);
            store.Update(settings =>
            {
                settings.LastSelectedTab = 3;
                settings.AutoEnableDiscoveredMods = false;
                settings.Workspace = new WorkspaceLayout(order, 290, 155);
            });
            var saved = ManagerSettingsStore.Load(path);
            var layout = WorkspaceLayout.Normalize(saved.Current.Workspace);
            Assert.Equal(order, layout.NavigationOrder);
            Assert.Equal(290d, layout.SidebarWidth);
            Assert.Equal<double?>(155d, layout.ModToolsHeight);
            saved.Update(settings => settings.Workspace = WorkspaceLayout.Default);
            var reset = ManagerSettingsStore.Load(path).Current;
            Assert.Equal(WorkspaceLayout.Default.NavigationOrder, WorkspaceLayout.Normalize(reset.Workspace).NavigationOrder);
            Assert.Equal(WorkspaceLayout.DefaultSidebarWidth, reset.Workspace!.SidebarWidth);
            Assert.Null(reset.Workspace.ModToolsHeight);
            Assert.Equal(3, reset.LastSelectedTab);
            Assert.False(reset.AutoEnableDiscoveredMods);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public Task ReorderedHeaderVisualsKeepCanonicalChildrenAndKeyboardOrder() =>
        IsolatedWpfSession.RunAsync((_, _) =>
        {
            var panel = new WorkspaceTabPanel();
            var dashboard = new TabItem { Header = "Dashboard", Height = 32 };
            var mods = new TabItem { Header = "Mods", Height = 32 };
            AutomationProperties.SetAutomationId(dashboard, "DashboardTab");
            AutomationProperties.SetAutomationId(mods, "ModsTab");
            panel.Children.Add(dashboard);
            panel.Children.Add(mods);
            panel.SetOrder(WorkspaceLayout.Reorder(panel.Order, "ModsTab", "DashboardTab", false));
            panel.Measure(new Size(200, 100));
            panel.Arrange(new Rect(0, 0, 200, 100));
            Assert.Same(dashboard, panel.Children[0]);
            Assert.Same(mods, panel.Children[1]);
            Assert.True(VisualTreeHelper.GetOffset(mods).Y < VisualTreeHelper.GetOffset(dashboard).Y);
            Assert.Equal(0, System.Windows.Input.KeyboardNavigation.GetTabIndex(mods));
            Assert.Equal(1, System.Windows.Input.KeyboardNavigation.GetTabIndex(dashboard));
            return Task.CompletedTask;
        });
}
