using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class InstalledWpfAcceptanceContractTests
{
    [Fact]
    public void UndoIsReachableFromVisibleModsPageWithoutSynthesizedKeyboardInput()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Parse(File.ReadAllText(Path.Combine(root,
            "src", "MhwModManager.App", "MainWindow.xaml")));
        var mods = document.Descendants().Single(element =>
            element.Name.LocalName == "TabItem" &&
            (string?)element.Attribute("Header") == "Mods");
        var undo = Assert.Single(mods.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Content") == "Undo");

        Assert.Equal("{Binding UndoCommand}", (string?)undo.Attribute("Command"));
        Assert.Equal("Undo Last Applied Change",
            (string?)undo.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void InstalledWpfWorkflowCanNeverBeScheduledOrRunOnPersistentDesktop()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root,
            ".github", "workflows", "installed-wpf-mod-lifecycle-e2e.yml"));
        Assert.Contains("runs-on: windows-2025", workflow, StringComparison.Ordinal);
        Assert.Contains("RUNNER_ENVIRONMENT -ne 'github-hosted'", workflow, StringComparison.Ordinal);
        Assert.Contains("MHW_RUN_INSTALLED_WPF_E2E: '1'", workflow, StringComparison.Ordinal);
        var caseCount = typeof(InstalledWpfModLifecycleE2ETests).GetMethods()
            .Where(method => method.IsDefined(typeof(FactAttribute), true))
            .Sum(method => Math.Max(1, method.GetCustomAttributes(typeof(InlineDataAttribute), true).Length));
        Assert.Contains($"$expectedCases = {caseCount}\n", workflow.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("succeeded:", workflow, StringComparison.Ordinal);
        Assert.Contains("skipped:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", workflow.Split("runs-on:").Last()
            .Split("timeout-minutes:")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledWpfWorkflowWatchesTheNestedImportAndDeploymentPath()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            ".github", "workflows", "installed-wpf-mod-lifecycle-e2e.yml"));
        var trigger = Regex.Match(workflow, @"(?ms)^  pull_request:\r?\n(?<body>.*?)(?=^  workflow_dispatch:)");
        Assert.True(trigger.Success);
        string[] paths =
        [
            "src/MhwModManager.App/MainWindow.xaml.cs",
            "src/MhwModManager.App/ViewModels/MainWindowViewModel.AutoSort.cs",
            "src/MhwModManager.App/ViewModels/MainWindowViewModel.Import.cs",
            "src/MhwModManager.Automation/GameModIntakeService.cs",
            "src/MhwModManager.Automation/GameModIntakeClassifier.cs",
            "src/MhwModManager.Automation/ModBoundaryDiscovery.cs",
            "src/MhwModManager.Automation/SmartInboxService.cs",
            "src/MhwModManager.Core/UnrealPakLayout.cs",
            "src/MhwModManager.Filesystem/ModScanner.cs",
            "src/MhwModManager.Filesystem/CatalogService.cs",
            "src/MhwModManager.Storage/ManagerDatabase.cs",
            "src/MhwModManager.App/App.xaml.cs"
        ];
        foreach (var path in paths)
            Assert.Matches($@"(?m)^      - '{Regex.Escape(path)}'\r?$", trigger.Groups["body"].Value);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository checkout not found.");
    }
}
