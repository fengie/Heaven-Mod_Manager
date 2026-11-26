using System.Diagnostics;
using System.Reflection;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class WpfControlInventoryTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MhwModManager.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Manager checkout required");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealModRowToggleRespectsTheInstalledTemplateGate(bool needsReview)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var source = XDocument.Load(Path.Combine(Root(), "src", "MhwModManager.App", "MainWindow.xaml"));
                var element = source.Descendants().Single(e => e.Name.LocalName == "CheckBox"
                    && e.Attribute("IsChecked")?.Value.Contains("StagedEnabled") == true);
                var checkbox = (CheckBox)XamlReader.Parse(element.ToString());
                var mod = new ModDescriptor("fixture", "fixture", "Fixture", "shelf/fixture", false, 0)
                    { RequiresLayoutReview = needsReview };
                var row = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([mod])), () => { });
                checkbox.DataContext = row;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Assert.Equal(!needsReview, checkbox.IsEnabled);
                var peer = new CheckBoxAutomationPeer(checkbox);
                var toggle = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
                if (needsReview)
                    Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => toggle.Toggle());
                else
                    toggle.Toggle();
                Assert.Equal(!needsReview, row.StagedEnabled);
                Assert.False(row.AppliedEnabled);
                result.SetResult();
            }
            catch (Exception ex) { result.SetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await result.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void DeclaredCommandPathsAndClickHandlersExistOnTheCompiledOwners()
    {
        var app = Path.Combine(Root(), "src", "MhwModManager.App");
        var assembly = typeof(MainWindowViewModel).Assembly;
        foreach (var path in Directory.GetFiles(app, "*.xaml"))
        {
            var document = XDocument.Load(path);
            var owner = assembly.GetType(document.Root!.Attributes().Single(a => a.Name.LocalName == "Class").Value, true)!;
            foreach (var element in document.Descendants())
            {
                if (element.Attribute("Click") is {} click)
                    Assert.NotNull(owner.GetMethod(click.Value, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
                if (element.Attribute("Command") is not {} command) continue;
                if (command.Value.StartsWith("{x:Static ScrollBar.", StringComparison.Ordinal))
                {
                    var name = command.Value["{x:Static ScrollBar.".Length..^1];
                    var scrollBar = typeof(System.Windows.Controls.Primitives.ScrollBar);
                    Assert.IsAssignableFrom<ICommand>(scrollBar.GetProperty(name)?.GetValue(null)
                        ?? scrollBar.GetField(name)?.GetValue(null));
                    continue;
                }
                Assert.StartsWith("{Binding ", command.Value);
                var member = command.Value["{Binding ".Length..].Split(',', '}')[0];
                var template = element.Ancestors().FirstOrDefault(parent => parent.Name.LocalName == "DataTemplate");
                var pageDetails = template?.Parent?.Name.LocalName == "TabItem.Tag" &&
                    template.Attribute("DataType")?.Value == "{x:Type vm:MainWindowViewModel}";
                if (template is not null && !pageDetails)
                {
                    Assert.Contains("RelativeSource AncestorType=Window", command.Value);
                    Assert.StartsWith("DataContext.", member);
                }
                var property = typeof(MainWindowViewModel).GetProperty(member.Replace("DataContext.", ""));
                Assert.True(property is not null && typeof(ICommand).IsAssignableFrom(property.PropertyType),
                    $"{Path.GetFileName(path)}: {command.Value} does not resolve to an ICommand on {nameof(MainWindowViewModel)}");
            }
        }
    }

    [Fact]
    public void InventoryDiagnosticsReportEveryMissingExtraAndRepeatedSite()
    {
        Assert.Equal(
            new string[] { "Expected 0, found 1: new", "Expected 1, found 0: removed", "Expected 2, found 1: repeat" },
            DescribeInventoryDifferences(["removed", "repeat", "repeat", "same"], ["new", "repeat", "same"]));
        Assert.Empty(DescribeInventoryDifferences(["same", "repeat", "repeat"], ["repeat", "same", "repeat"]));
    }

    private static string[] DescribeInventoryDifferences(string[] recorded, string[] discovered)
    {
        var expected = recorded.GroupBy(site => site, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var actual = discovered.GroupBy(site => site, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return expected.Keys.Union(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(site => expected.GetValueOrDefault(site) != actual.GetValueOrDefault(site))
            .Select(site => $"Expected {expected.GetValueOrDefault(site)}, found {actual.GetValueOrDefault(site)}: {site}")
            .ToArray();
    }

    [Fact]
    public async Task SourceSitesAndBehaviorReferencesMatchTheReviewedInventory()
    {
        var root = Root();
        var verification = Path.Combine(root, "docs", "verification");
        var token = TestContext.Current.CancellationToken;
        using var scanner = new Process();
        var hasPowerShell7 = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, "pwsh.exe")));
        var shell = hasPowerShell7 ? "pwsh.exe" : Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        scanner.StartInfo = new ProcessStartInfo(shell) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(root, "scripts", "Get-WpfControlInventory.ps1") })
            scanner.StartInfo.ArgumentList.Add(argument);
        scanner.Start();
        var output = scanner.StandardOutput.ReadToEndAsync(token);
        var errors = scanner.StandardError.ReadToEndAsync(token);
        try { await scanner.WaitForExitAsync(token); }
        finally { if (!scanner.HasExited) scanner.Kill(entireProcessTree: true); }
        Assert.True(scanner.ExitCode == 0, await errors);
        var discovered = (await output).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var recorded = await File.ReadAllLinesAsync(Path.Combine(verification, "wpf-control-inventory.txt"), token);
        // Include repeated sites: a second control wired to the same command requires review too.
        var differences = DescribeInventoryDifferences(recorded, discovered);
        if (differences.Length != 0)
            Assert.Fail("WPF control inventory differs from reviewed source sites:" + Environment.NewLine +
                string.Join(Environment.NewLine, differences));
        Assert.Equal(recorded.Order(StringComparer.Ordinal), discovered.Order(StringComparer.Ordinal));

        var mappings = await File.ReadAllLinesAsync(Path.Combine(verification, "wpf-control-behavior.tsv"), token);
        foreach (var mapping in mappings.Skip(1))
        {
            var fields = mapping.Split('\t');
            Assert.Equal(4, fields.Length);
            Assert.All(fields, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.Single(recorded, site => site == fields[0]);
            var reference = fields[1].Split('.');
            Assert.Equal(2, reference.Length);
            var type = GetType().Assembly.GetType($"{GetType().Namespace}.{reference[0]}", true)!;
            var method = type.GetMethod(reference[1]);
            Assert.NotNull(method);
            Assert.NotEmpty(method.GetCustomAttributes<FactAttribute>());
        }
    }
}
