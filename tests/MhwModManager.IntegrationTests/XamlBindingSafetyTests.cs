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
    public void ComboBoxesOwnDarkThemeChromeInsteadOfUsingWindowsLightSystemSurface()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "App.xaml"));

        Assert.Contains("<Style TargetType=\"ComboBoxItem\">", appXaml);
        Assert.Contains("<ControlTemplate TargetType=\"ComboBox\">", appXaml);
        Assert.Contains("x:Name=\"PART_Popup\"", appXaml);
        Assert.Contains("Content=\"{TemplateBinding SelectionBoxItem}\"", appXaml);
        Assert.Contains("ContentTemplate=\"{TemplateBinding ItemTemplate}\"", appXaml);
        Assert.Contains("ContentTemplateSelector=\"{TemplateBinding ItemTemplateSelector}\"", appXaml);
        Assert.Contains("ContentStringFormat=\"{TemplateBinding ItemStringFormat}\"", appXaml);
        Assert.DoesNotContain("ContentTemplate=\"{TemplateBinding SelectionBoxItemTemplate}\"", appXaml);
        Assert.DoesNotContain("ContentStringFormat=\"{TemplateBinding SelectionBoxItemStringFormat}\"", appXaml);
        Assert.Contains("Property=\"Foreground\" Value=\"{StaticResource Text}\"", appXaml);
        Assert.Contains("Property=\"Background\" Value=\"#0C1015\"", appXaml);
        Assert.Contains("Background=\"{StaticResource Panel2}\"", appXaml);
        Assert.DoesNotContain("SystemColors.ControlBrushKey", appXaml);
        Assert.DoesNotContain("SystemColors.ControlTextBrushKey", appXaml);
    }

    [Fact]
    public void HeaderGameSelectorUsesDisplayNameTemplate()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("AutomationProperties.Name=\"Active game\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ActiveGameSelector\"", xaml);
        Assert.Contains("AutomationProperties.ItemStatus=\"{Binding SelectedGame.DisplayName}\"", xaml);
        Assert.Contains("TextSearch.TextPath=\"DisplayName\"", xaml);
        Assert.Contains("<ComboBox.ItemTemplate>", xaml);
        Assert.Contains("Text=\"{Binding DisplayName}\"", xaml);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ActiveGameDisplayName\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding DisplayName}\"", xaml);
        Assert.DoesNotContain("DisplayMemberPath=\"DisplayName\"", xaml);
    }

    [Fact]
    public void BrowseModsShowsArtworkMetadataAndUsesVirtualizedCapacity()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Source=\"{Binding ThumbnailUri}\"", xaml);
        Assert.Contains("SelectedCatalogItem.ThumbnailUri", xaml);
        Assert.Contains("Binding=\"{Binding Version}\"", xaml);
        Assert.Contains("Binding=\"{Binding Updated}\"", xaml);
        Assert.Contains("Text=\"{Binding Downloads}\"", xaml);
        Assert.Contains("EnableRowVirtualization=\"True\"", xaml);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", xaml);
        Assert.Contains("private const int CatalogProviderRefreshLimit = 100;", source);
        Assert.Contains("private const int CatalogVisibleResultLimit = 1000;", source);
        Assert.Contains("Limit: CatalogProviderRefreshLimit", source);
        Assert.Contains("limit: CatalogVisibleResultLimit", source);
        Assert.Contains("public Uri? ThumbnailUri", source);
        Assert.Contains("Uri.UriSchemeHttps", source);
    }

    [Fact]
    public void ConflictCollectionChangesNotifyDerivedAttentionState()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.True(Regex.IsMatch(
            source,
            @"Conflicts\.CollectionChanged\s*\+=\s*\(_, args\)\s*=>\s*\{[\s\S]*?NotifyConflictDerivedState\(\);[\s\S]*?\};",
            RegexOptions.CultureInvariant));

        var helper = Regex.Match(
            source,
            @"private void NotifyConflictDerivedState\(\)\s*\{(?<body>[\s\S]*?)\}",
            RegexOptions.CultureInvariant);
        Assert.True(helper.Success);
        Assert.Contains("MasterDebugLog.BeginMethod()", helper.Groups["body"].Value);
        Assert.Contains("OnPropertyChanged(nameof(BlockerCount));", helper.Groups["body"].Value);
        Assert.Contains("OnPropertyChanged(nameof(HeaderSummary));", helper.Groups["body"].Value);
        Assert.Contains("DataTrigger Binding=\"{Binding BlockerCount}\" Value=\"0\"", xaml);
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

    [Fact]
    public void ExpensiveMetadataAndConflictPreviewWorkIsDeferredOffInitialStartup()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "App.xaml.cs"));
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));

        var startupStart = app.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        var windowConstruct = app.IndexOf("var window=startup.Run(\"ui.main-window.construct\"", startupStart, StringComparison.Ordinal);
        Assert.True(startupStart >= 0 && windowConstruct > startupStart);
        var blockingStartup = app[startupStart..windowConstruct];

        Assert.DoesNotContain("nexus.RefreshAsync", blockingStartup);
        Assert.Contains("startup.intelligence.nexus.deferred", blockingStartup);
        Assert.Contains("if(value==1&&!initialMetadataRefreshCompleted&&!initialMetadataRefreshStarted&&BusyVisibility!=Visibility.Visible)", mainViewModel);
        Assert.Contains("EnsureInitialMetadataLoadedAsync(backgroundCts.Token)", mainViewModel);
        Assert.Contains("if(value==3)_=EnsureConflictPreviewsLoadedAsync(backgroundCts.Token);", mainViewModel);
        Assert.Contains("var shouldEnrich=SelectedTab==3;", mainViewModel);
        Assert.Contains("var displayRows=shouldEnrich?await EnrichConflictPreviewsAsync(analysis.rows,ct):analysis.rows;", mainViewModel);
    }

    [Fact]
    public void PreviewImagesDecodeNearTheirDisplayedSize()
    {
        var root = FindRepositoryRoot();
        var converter = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "SafeImageSourceConverter.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("image.DecodePixelWidth=decodeWidth;", converter);
        Assert.Contains("MaximumDecodeWidth=2048", converter);
        Assert.Contains("ConverterParameter=160", xaml);
        Assert.Contains("ConverterParameter=224", xaml);
        Assert.Contains("ConverterParameter=320", xaml);
        Assert.Contains("ConverterParameter=384", xaml);
        Assert.Contains("ConverterParameter=512", xaml);
        Assert.DoesNotContain("Source=\"{Binding ThumbnailPath,Converter={StaticResource SafeImage}}\"", xaml);
        Assert.DoesNotContain("Source=\"{Binding PreviewPath,Converter={StaticResource SafeImage}}\"", xaml);
    }

    [Fact]
    public void ModsPagePrioritizesWindowedLibraryViewport()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("<Grid Margin=\"8,6,8,6\">", xaml);
        Assert.DoesNotContain("<Grid Margin=\"28,14,32,14\">", xaml);
        Assert.Contains("x:Name=\"ModLibraryToolbar\"", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("Grid x:Name=\"ModLibraryRegion\" Grid.Row=\"2\"", xaml);
        Assert.Contains("ToolTip=\"{Binding PlanPreviewText}\"", xaml);
        Assert.Contains("MinHeight=\"32\"", xaml);
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
