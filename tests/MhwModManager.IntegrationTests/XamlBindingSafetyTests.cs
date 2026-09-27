using System.Text.RegularExpressions;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class XamlBindingSafetyTests
{
    [Fact]
    public void InlineRunBindingsAreExplicitlyOneWay()
    {
        var root = FindRepositoryRoot();
        var xamlPath = Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var unsafeBindings = RunBindingRegex().Matches(xaml)
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(value => !value.Contains("Mode=OneWay", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unsafeBindings);
    }

    [Fact]
    public void ConflictUiOffersManualFamilyChaining()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        Assert.Contains("Make main + chain others", xaml);
        Assert.Contains("ChainConflictFamilyCommand", xaml);
        Assert.Contains("Choose only", xaml);
    }

    [Fact]
    public void OverlapUiExposesExplainWhyEvidence()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        Assert.Contains("Explain Why", xaml);
        Assert.Contains("ExplainSelectedOverlapCommand", xaml);
        Assert.Contains("SelectedExplanation.RuleSource", xaml);
        Assert.Contains("SelectedExplanation.Evidence", xaml);
        Assert.Contains("SelectedExplanation.Providers", xaml);
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

    [GeneratedRegex("<Run\\b[^>]*\\bText=\\\"\\{Binding[^}]+\\}\\\"[^>]*/>", RegexOptions.CultureInvariant)]
    private static partial Regex RunBindingRegex();
}
