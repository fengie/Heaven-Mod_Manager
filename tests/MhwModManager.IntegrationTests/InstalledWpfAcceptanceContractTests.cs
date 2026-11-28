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
    public void InstalledWpfWorkflowWatchesAllProductDependencies()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            ".github", "workflows", "installed-wpf-mod-lifecycle-e2e.yml"));
        var trigger = Regex.Match(workflow, @"(?ms)^  pull_request:\r?\n(?<body>.*?)(?=^  workflow_dispatch:)");
        Assert.True(trigger.Success);
        string[] paths =
        [
            "src/**",
            "Directory.Build.props",
            "Directory.Packages.props",
            "global.json",
            "data/**",
            "tests/MhwModManager.IntegrationTests/InstalledWpfModLifecycleE2ETests.cs",
            "tests/MhwModManager.IntegrationTests/InstalledWpfModLifecycleE2ETests.*.cs",
            "tests/fixtures/discovered-games/**",
            "tests/fixtures/packaged-ui/**"
        ];
        foreach (var path in paths)
            Assert.Matches($@"(?m)^      - '{Regex.Escape(path)}'\r?$", trigger.Groups["body"].Value);
    }

    [Fact]
    public void InstalledWpfMatrixHasBoundedInventoryBudgetAndStreamsEvidence()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root,
            ".github", "workflows", "installed-wpf-mod-lifecycle-e2e.yml"));
        using var inventory = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "tests", "fixtures", "discovered-games", "games.json")));
        var profiles = inventory.RootElement.GetArrayLength();
        Assert.True(profiles > 0);
        var job = Regex.Match(workflow, @"(?m)^    timeout-minutes: (?<minutes>\d+)\r?$");
        var step = Regex.Match(workflow,
            @"(?ms)^      - name: Verify remaining lifecycle cases and all registered game receipts\r?\n(?<body>.*?)(?=^      - name:)");
        Assert.True(job.Success && step.Success);
        var matrix = Regex.Match(step.Groups["body"].Value, @"(?m)^        timeout-minutes: (?<minutes>\d+)\r?$");
        Assert.True(matrix.Success);
        var jobMinutes = int.Parse(job.Groups["minutes"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var matrixMinutes = int.Parse(matrix.Groups["minutes"].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(matrixMinutes, profiles * 2, 100);
        Assert.InRange(jobMinutes, matrixMinutes + 15, 120);
        Assert.Contains("Tee-Object -Variable output", step.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("$exit = $LASTEXITCODE", step.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("$observedIds.Count -ne $expectedIds.Count", step.Groups["body"].Value, StringComparison.Ordinal);
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
