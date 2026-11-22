using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ContextualPageDetailsTests
{
    [Fact]
    public async Task ExpandedDetailsFollowTheCurrentPageAndItsSelectedItem()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { VerifyPageDetails(); finished.SetResult(); }
            catch (Exception error) { finished.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    private sealed class Selection : ObservableObject
    {
        public string GamePathText { get; init; } = "Fixture game folder";
        public object? SelectedOverlap { get; init; }
        public IRelayCommand ExplainSelectedOverlapCommand { get; init; } = null!;
        public IRelayCommand LoadCatalogFilesCommand { get; init; } = null!;
        private object? catalogItem;
        public object? SelectedCatalogItem { get => catalogItem; set => SetProperty(ref catalogItem, value); }
        private object? selected;
        public object? SelectedInstalledFile { get => selected; set => SetProperty(ref selected, value); }
    }

    private static void VerifyPageDetails()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MhwModManager.sln"))) directory = directory.Parent;
        var app = Path.Combine(directory!.FullName, "src", "MhwModManager.App");
        var theme = XDocument.Load(Path.Combine(app, "App.xaml"));
        var main = XDocument.Load(Path.Combine(app, "MainWindow.xaml"));
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var nav = theme.Descendants(w + "Style").Single(s => (string?)s.Attribute(x + "Key") == "MainNavigationTabs");
        var body = nav.Descendants(w + "DockPanel").Single(d => d.Element(w + "Expander") is not null);
        var lineStyle = theme.Descendants(w + "Style").Single(s => (string?)s.Attribute(x + "Key") == "ContextDetailLine");
        var tabs = main.Descendants(w + "TabControl").Single(tab => (string?)tab.Attribute(x + "Name") == "MainTabs").Elements(w + "TabItem").ToArray();
        var withoutDetails = Assert.Single(tabs, t => t.Element(w + "TabItem.Tag") is null);
        Assert.Contains("CoverageTabLabel", (string?)withoutDetails.Attribute("Header"));
        // Use the production presentation and page contents without starting Manager or loading user data.
        var fixture = new XElement(w + "TabControl", new XAttribute(XNamespace.Xmlns + "x", x),
            new XAttribute(XNamespace.Xmlns + "vm", $"clr-namespace:MhwModManager.App.ViewModels;assembly={typeof(MhwModManager.App.ViewModels.MainWindowViewModel).Assembly.GetName().Name}"),
            new XElement(w + "TabControl.Resources", new XElement(w + "SolidColorBrush", new XAttribute(x + "Key", "Text"), new XAttribute("Color", "White")), new XElement(w + "Style", new XAttribute(x + "Key", "QuietButton"), new XAttribute("TargetType", "Button")), lineStyle),
            new XElement(w + "TabControl.Template", new XElement(w + "ControlTemplate", new XAttribute("TargetType", "TabControl"), body)),
            tabs.Select(t => new XElement(w + "TabItem", new XAttribute("Header", t == withoutDetails ? "Coverage" : (string)t.Attribute("Header")!), t.Element(w + "TabItem.Tag"),
                new XElement(w + "ListBox", t.Descendants(w + "DataGrid").Attributes("SelectedItem").Where(a => a.Value.Contains("SelectedInstalledFile", StringComparison.Ordinal)).Select(a => new XAttribute(a))))));
        var control = (TabControl)XamlReader.Parse(fixture.ToString());
        var invocations = 0;
        control.DataContext = new Selection { SelectedOverlap = new { PrimaryPath = "shared/file", Providers = "A, B", Detail = "Winner A" }, ExplainSelectedOverlapCommand = new RelayCommand(() => invocations++), LoadCatalogFilesCommand = new RelayCommand(() => invocations++) };
        control.ApplyTemplate();
        var panel = (DockPanel)VisualTreeHelper.GetChild(control, 0);
        var details = (Expander)panel.Children[0];
        details.IsExpanded = true;
        void Select(string page)
        {
            control.SelectedItem = control.Items.Cast<TabItem>().Single(t => Equals(t.Header, page));
            control.Measure(new Size(900, 600)); control.Arrange(new Rect(0, 0, 900, 600));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        }
        StackPanel DetailPanel()
        {
            var presenter = (ContentPresenter)((ScrollViewer)details.Content).Content;
            presenter.ApplyTemplate();
            return (StackPanel)VisualTreeHelper.GetChild(presenter, 0);
        }
        string[] Lines() => DetailPanel().Children.OfType<TextBox>().Where(box => box.IsReadOnly).Select(box => box.Text).ToArray();
        Select("Dashboard"); Assert.Contains("Game folder: Fixture game folder", Lines());
        Select("Installed files"); Assert.Contains("Select an installed file to see its evidence and location.", Lines());
        var files = (ListBox)((TabItem)control.SelectedItem).Content;
        files.Items.Add(new { Evidence = "Workshop manifest", Location = "Fixture.pack", InstallationRoot = "Fixture root" });
        files.SelectedIndex = 0;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Same(files.SelectedItem, ((Selection)control.DataContext).SelectedInstalledFile);
        Assert.Contains("File location: Fixture.pack", Lines());
        Select("Shared Files");
        Assert.Contains("File: shared/file", Lines());
        var option = Assert.Single(DetailPanel().Children.OfType<Button>());
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(option);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer).Invoke();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Assert.Equal(1, invocations);
        Select("Browse Mods");
        var provider = Assert.Single(DetailPanel().Children.OfType<Button>());
        Assert.Equal(Visibility.Collapsed, provider.Visibility);
        ((Selection)control.DataContext).SelectedCatalogItem = new { Provider = "Fixture provider", Name = "Fixture mod" };
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Equal(Visibility.Visible, provider.Visibility);
        Assert.Contains("Catalog provider: Fixture provider", Lines());
        ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(provider)).Invoke();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Assert.Equal(2, invocations);
        Select("Coverage"); Assert.Equal(Visibility.Collapsed, details.Visibility);
        Select("Installed files"); Assert.Equal(Visibility.Visible, details.Visibility);
        Assert.Contains("Evidence: Workshop manifest", Lines());
    }
}
