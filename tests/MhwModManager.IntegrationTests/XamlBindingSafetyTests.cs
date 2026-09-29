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
        Assert.Contains("Treat as Main Mod + Add-ons", xaml);
        Assert.Contains("ChainConflictFamilyCommand", xaml);
        Assert.Contains("Use This Mod", xaml);
    }

    [Fact]
    public void OverlapUiExposesExplainWhyEvidence()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        Assert.Contains("Why this file uses this mod", xaml);
        Assert.Contains("ExplainSelectedOverlapCommand", xaml);
        Assert.Contains("SelectedExplanation.RuleSource", xaml);
        Assert.Contains("SelectedExplanation.Evidence", xaml);
        Assert.Contains("SelectedExplanation.Providers", xaml);
    }

    [Fact]
    public void ActivityPageExtractionPreservesExistingBindingSurface()
    {
        var root = FindRepositoryRoot();
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var activityFeature = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Activity.cs"));
        var activityPage = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "ActivityPageViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("public ActivityPageViewModel Activity{get;}", mainViewModel);
        Assert.Contains("ActivityRows=Activity.Rows;", mainViewModel);
        Assert.Contains("await Activity.RefreshAsync(ct);", activityFeature);
        Assert.Contains("PresentationReadRepository", activityPage);
        Assert.Contains("ItemsSource=\"{Binding ActivityRows}\"", xaml);
        Assert.Contains("Command=\"{Binding RefreshActivityCommand}\"", xaml);
    }

    [Fact]
    public void CoveragePageExtractionPreservesExistingBindingSurface()
    {
        var root = FindRepositoryRoot();
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var coverageFeature = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Coverage.cs"));
        var coveragePage = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "CoveragePageViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("public CoveragePageViewModel Coverage{get;}", mainViewModel);
        Assert.Contains("OutfitRows=Coverage.Rows;", mainViewModel);
        Assert.Contains("await Coverage.RefreshAsync(HasSemanticCoverage,ct);", coverageFeature);
        Assert.Contains("PresentationReadRepository", coveragePage);
        Assert.Contains("ItemsSource=\"{Binding OutfitRows}\"", xaml);
        Assert.Contains("Command=\"{Binding RefreshOutfitsCommand}\"", xaml);
    }

    [Fact]
    public void ProfilesPageExtractionPreservesExistingBindingSurface()
    {
        var root = FindRepositoryRoot();
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var profilesFeature = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Profiles.cs"));
        var profilesPage = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "ProfilesPageViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("public ProfilesPageViewModel ProfilesPage{get;}", mainViewModel);
        Assert.Contains("Profiles=ProfilesPage.Rows;", mainViewModel);
        Assert.Contains("await ProfilesPage.RefreshAsync(ct);", profilesFeature);
        Assert.Contains("ProfileRepository", profilesPage);
        Assert.Contains("ItemsSource=\"{Binding Profiles}\"", xaml);
        Assert.Contains("Command=\"{Binding RefreshProfilesCommand}\"", xaml);
    }

    [Fact]
    public void GamesPageExtractionPreservesListBindingAndShellLifecycle()
    {
        var root = FindRepositoryRoot();
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var gamesFeature = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Games.cs"));
        var gamesPage = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "GamesPageViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("public GamesPageViewModel GamesPage{get;}", mainViewModel);
        Assert.Contains("Games=GamesPage.Rows;", mainViewModel);
        Assert.Contains("GamesPage.Refresh();", mainViewModel);
        Assert.Contains("GameProfileRegistry", gamesPage);
        Assert.Contains("Rows.ReplaceAll(registry.Load());", gamesPage);
        Assert.Contains("GamesPage.Refresh();", gamesFeature);
        Assert.True(Regex.IsMatch(gamesFeature, @"private async Task ScanInstalledGames\(\)\s*\{\s*using var __mhwTrace = MasterDebugLog\.BeginMethod\(\);", RegexOptions.CultureInvariant));
        Assert.Contains("s.GameRegistry.SetActive(SelectedGame.Id);", gamesFeature);
        Assert.Contains("RestartIntoGame(SelectedGame);", gamesFeature);
        Assert.Contains("ItemsSource=\"{Binding Games}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding SelectedGame,Mode=TwoWay}\"", xaml);
        Assert.Contains("Command=\"{Binding SwitchGameCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding ScanInstalledGamesCommand}\"", xaml);
    }


    [Fact]
    public void HiddenSecondaryPagesAreLazyLoadedAfterStartup()
    {
        var root = FindRepositoryRoot();
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));

        var initializeStart = mainViewModel.IndexOf("public async Task InitializeAsync()", StringComparison.Ordinal);
        var initializeEnd = mainViewModel.IndexOf("partial void OnSelectedTabChanged", initializeStart, StringComparison.Ordinal);
        Assert.True(initializeStart >= 0 && initializeEnd > initializeStart);
        var initialize = mainViewModel[initializeStart..initializeEnd];

        Assert.DoesNotContain("await RefreshProfiles(ct);", initialize);
        Assert.DoesNotContain("await RefreshActivity(ct);", initialize);
        Assert.Contains("partial void OnSelectedTabChanged(int value)", mainViewModel);
        Assert.Contains("case 4 when !profilesLoaded:", mainViewModel);
        Assert.Contains("case 5 when !activityLoaded:", mainViewModel);
        Assert.Contains("case 6 when !overlapsLoaded:", mainViewModel);
        Assert.Contains("overlapsLoaded=false;", mainViewModel);
        Assert.Contains("if(SelectedTab==6)await EnsureDeferredPageLoadedAsync(6,ct);", mainViewModel);
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
