using System.Xml.Linq;
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
        Assert.Contains("succeeded:", workflow, StringComparison.Ordinal);
        Assert.Contains("skipped:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", workflow.Split("runs-on:").Last()
            .Split("timeout-minutes:")[0], StringComparison.Ordinal);
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
