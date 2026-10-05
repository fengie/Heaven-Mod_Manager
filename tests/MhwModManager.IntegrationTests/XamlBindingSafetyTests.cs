using System.Text.RegularExpressions;
using MhwModManager.App.ViewModels;
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
        Assert.Contains("Command=\"{Binding LoadMoreCatalogCommand}\"", xaml);
        Assert.Contains("Visibility=\"{Binding CatalogLoadMoreVisibility}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Load more provider browse results\"", xaml);
        Assert.Contains("private async Task LoadMoreCatalog()", source);
        Assert.Contains("SyncNextPageAsync(", source);
        Assert.Contains("catalogSyncGate.WaitAsync(ct)", source);
        Assert.Contains("public Uri? ThumbnailUri", source);
        Assert.Contains("Uri.UriSchemeHttps", source);
    }

    [Fact]
    public void BrowseModsRequiresExplicitModAndFileSelection()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Select a mod to see details and exact files.", xaml);
        Assert.Contains("AutomationProperties.Name=\"Selected catalog mod details\"", xaml);
        Assert.Contains("DataTrigger Binding=\"{Binding SelectedCatalogItem}\" Value=\"{x:Null}\"", xaml);
        Assert.Contains("DataTrigger Binding=\"{Binding SelectedCatalogFile}\" Value=\"{x:Null}\"", xaml);
        Assert.Contains("<Setter Property=\"IsEnabled\" Value=\"False\"/>", xaml);
        Assert.Contains("SelectedCatalogFile = null;", source);
        Assert.Contains("CatalogFiles.ReplaceAll(value?.Mod.Files.Select", source);
    }

    [Fact]
    public void BrowseModsInstallActionExplainsWhySelectionIsRequired()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("AutomationProperties.Name=\"Install selected catalog file\"", xaml);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding CatalogInstallGuidance}\"", xaml);
        Assert.Contains("Text=\"{Binding CatalogInstallGuidance}\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("public string CatalogInstallGuidance", source);
        Assert.Contains("CatalogFilePresentationKind.Loading =>", source);
        Assert.Contains("CatalogFilePresentationKind.Loaded =>", source);
        Assert.Contains("CatalogFilePresentationKind.Empty =>", source);
        Assert.Contains("CatalogFilePresentationKind.Failed =>", source);
        Assert.Contains("partial void OnSelectedCatalogFileChanged(CatalogFileRow? value)", source);
        Assert.Contains("OnPropertyChanged(nameof(CatalogInstallGuidance));", source);
    }

    [Theory]
    [InlineData(CatalogFilePresentationKind.NotLoaded, 0, false, false)]
    [InlineData(CatalogFilePresentationKind.Loading, 0, false, false)]
    [InlineData(CatalogFilePresentationKind.Loaded, 3, true, false)]
    [InlineData(CatalogFilePresentationKind.Empty, 0, false, true)]
    [InlineData(CatalogFilePresentationKind.Failed, 0, false, true)]
    public void BrowseModsExactFileStateDistinguishesLoadRecoveryPaths(
        CatalogFilePresentationKind kind,
        int fileCount,
        bool expectedShowFiles,
        bool expectedRetry)
    {
        var state = CatalogFilePresentationState.From(kind, fileCount, "provider failed");

        Assert.Equal(kind, state.Kind);
        Assert.Equal(expectedShowFiles, state.ShowFiles);
        Assert.Equal(expectedRetry, state.CanRetry);
        Assert.NotEmpty(state.Title);
        Assert.NotEmpty(state.Detail);
    }

    [Fact]
    public void BrowseModsExactFileStateExposesAccessibleRecoveryAndClearsStaleSelection()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Visibility=\"{Binding CatalogFileStateVisibility}\"", xaml);
        Assert.Contains("Visibility=\"{Binding CatalogFilesVisibility}\"", xaml);
        Assert.Contains("Text=\"{Binding CatalogFilePresentationTitle}\"", xaml);
        Assert.Contains("Text=\"{Binding CatalogFilePresentationDetail}\"", xaml);
        Assert.Contains("Visibility=\"{Binding CatalogFileRetryVisibility}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Retry loading exact provider files\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("SetCatalogFilePresentation(CatalogFilePresentationKind.Loading);", source);
        Assert.Contains("CatalogFilePresentationKind.Empty", source);
        Assert.Contains("CatalogFilePresentationKind.Failed", source);

        var loadStart = source.IndexOf("private async Task LoadCatalogFiles()", StringComparison.Ordinal);
        var installStart = source.IndexOf("private async Task InstallCatalogFile()", loadStart, StringComparison.Ordinal);
        Assert.True(loadStart >= 0 && installStart > loadStart);
        var loadBlock = source[loadStart..installStart];
        var clearSelection = loadBlock.IndexOf("SelectedCatalogFile = null;", StringComparison.Ordinal);
        var loadState = loadBlock.IndexOf("SetCatalogFilePresentation(CatalogFilePresentationKind.Loading);", StringComparison.Ordinal);
        Assert.True(clearSelection >= 0 && loadState > clearSelection);
        Assert.DoesNotContain("SelectedCatalogFile = CatalogFiles.FirstOrDefault();", loadBlock);
    }

    [Fact]
    public void BrowseModsPersistsHydratedFilesWithoutAutoSelecting()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        var loadStart = source.IndexOf("private async Task LoadCatalogFiles()", StringComparison.Ordinal);
        var installStart = source.IndexOf("private async Task InstallCatalogFile()", loadStart, StringComparison.Ordinal);
        Assert.True(loadStart >= 0 && installStart > loadStart);
        var loadBlock = source[loadStart..installStart];

        Assert.Contains("var cached = await repository.GetAsync(selected.Mod.CanonicalId, ct)", loadBlock, StringComparison.Ordinal);
        Assert.Contains("var hydratedMod = selected.Mod with { Files = files };", loadBlock, StringComparison.Ordinal);
        Assert.Contains("await repository.UpsertAsync(cached with { Mod = hydratedMod }, ct);", loadBlock, StringComparison.Ordinal);
        Assert.Contains("SelectedCatalogItem = hydratedRow;", loadBlock, StringComparison.Ordinal);
        Assert.Contains("SelectedCatalogFile = null;", loadBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedCatalogFile = CatalogFiles.FirstOrDefault();", loadBlock, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("", 0, false, false, "No catalog mods yet")]
    [InlineData("armor", 0, true, false, "No mods match this search")]
    [InlineData("armor", 2, true, true, "")]
    public void BrowseModsResultStateDistinguishesEmptyCatalogFromZeroMatches(
        string query,
        int resultCount,
        bool expectedHasQuery,
        bool expectedHasResults,
        string expectedTitle)
    {
        var state = CatalogBrowseResultState.From(query, resultCount);

        Assert.Equal(expectedHasQuery, state.HasQuery);
        Assert.Equal(expectedHasResults, state.HasResults);
        Assert.Equal(expectedTitle, state.Title);
        if (expectedHasResults)
        {
            Assert.Empty(state.Detail);
        }
        else
        {
            Assert.NotEmpty(state.Detail);
        }
    }

    [Fact]
    public void BrowseModsZeroMatchCopyDoesNotClaimTypingQueriedProviders()
    {
        var state = CatalogBrowseResultState.From("armor", 0);

        Assert.Equal(
            "No matching results are in the local cache. Typing filters cached results; choose Search to query configured providers that support text search, or clear the search to return to all cached mods.",
            state.Detail);
        Assert.DoesNotContain("cached or provider results", state.Detail, StringComparison.OrdinalIgnoreCase);
    }


    [Theory]
    [InlineData(true, false, 2, 0, 0, 5, 2, CatalogPresentationKind.Loading, false)]
    [InlineData(false, true, 2, 1, 1, 5, 0, CatalogPresentationKind.PartialFailure, true)]
    [InlineData(false, true, 2, 0, 2, 5, 1, CatalogPresentationKind.Unavailable, true)]
    [InlineData(false, true, 2, 2, 0, 5, 2, CatalogPresentationKind.Stale, false)]
    [InlineData(false, true, 2, 2, 0, 5, 0, CatalogPresentationKind.Fresh, false)]
    [InlineData(false, true, 0, 0, 0, 5, 1, CatalogPresentationKind.CachedOnly, false)]
    public void BrowseModsPresentationStateDistinguishesProviderHealthAndCacheFreshness(
        bool isLoading,
        bool attempted,
        int configured,
        int successful,
        int failed,
        int results,
        int stale,
        CatalogPresentationKind expectedKind,
        bool expectedRetry)
    {
        var state = CatalogPresentationState.From(
            isLoading,
            attempted,
            configured,
            successful,
            failed,
            results,
            stale);

        Assert.Equal(expectedKind, state.Kind);
        Assert.Equal(expectedRetry, state.CanRetry);
        Assert.NotEmpty(state.Title);
        Assert.NotEmpty(state.Detail);
    }

    [Fact]
    public void BrowseModsProviderHealthBannerKeepsRecoveryVisibleAboveResults()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Text=\"{Binding CatalogPresentationTitle}\"", xaml);
        Assert.Contains("Text=\"{Binding CatalogPresentationDetail}\"", xaml);
        Assert.Contains("Visibility=\"{Binding CatalogProviderRetryVisibility}\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Retry failed catalog providers\"", xaml);
        Assert.Contains("SetCatalogProviderOperationInProgress(true);", source);
        Assert.Contains("SetCatalogProviderHealth(providers.Count, successes, failures, attempted: true);", source);
        Assert.Contains("SetCatalogProviderHealth(searchable.Length, successes, failures, attempted: true);", source);
        Assert.Contains("return CatalogItems.Count(row => row.IsStale);", source);
    }

    [Fact]
    public void BrowseModsEmptyStatesExposeRecoveryActionsAndHideDeadEndSelectionPrompt()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Text=\"{Binding CatalogEmptyTitle}\"", xaml);
        Assert.Contains("Text=\"{Binding CatalogEmptyDetail}\"", xaml);
        Assert.Contains("Visibility=\"{Binding CatalogResultsVisibility}\"", xaml);
        Assert.Contains("Visibility=\"{Binding CatalogEmptyVisibility}\"", xaml);
        Assert.Contains("Command=\"{Binding RefreshCatalogCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding ClearCatalogSearchCommand}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Refresh catalog providers\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Clear catalog search\"", xaml);
        Assert.Contains("<Condition Binding=\"{Binding HasCatalogResults}\" Value=\"True\"/>", xaml);
        Assert.Contains("Text=\"{Binding CatalogResultCountLabel}\"", xaml);
        Assert.DoesNotContain("<Run Text=\"{Binding CatalogItemCount,Mode=OneWay}\"/><Run Text=\" cached\"/>", xaml);

        Assert.Contains("private async Task ClearCatalogSearch()", source);
        Assert.Contains("CatalogQuery = \"\";", source);
        Assert.Contains("NotifyCatalogResultState();", source);
        Assert.Contains("SelectedCatalogItem = projected.FirstOrDefault", source);
    }

    [Fact]
    public void BrowseModsExplicitSearchOnlyQueriesProvidersThatAdvertiseSearch()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));
        var storage = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.Storage", "CatalogRepository.cs"));

        Assert.Contains("provider.Capabilities.HasFlag(CatalogProviderCapabilities.Search)", source);
        Assert.Contains("SearchCatalogProvidersAsync(query, ct)", source);
        Assert.Contains("Query: query", source);
        Assert.Contains("Provider search failed without blocking cache results.", source);
        Assert.Contains("Nexus Mods and GameBanana are not probed for unsupported full-catalog search.", source);
        Assert.Contains("Typing filters the local cache.", xaml);
        Assert.Contains("Unsupported providers are not probed.", xaml);
        Assert.Contains("private const int MaxSearchResults = 1000;", storage);

        var debounceStart = source.IndexOf("private async Task DebounceCatalogQueryAsync", StringComparison.Ordinal);
        var runtimeStart = source.IndexOf("private void EnsureCatalogRuntime", debounceStart, StringComparison.Ordinal);
        Assert.True(debounceStart >= 0 && runtimeStart > debounceStart);
        var debounce = source[debounceStart..runtimeStart];
        Assert.Contains("SearchCatalogCacheAsync(ct)", debounce);
        Assert.DoesNotContain("SearchCatalogProvidersAsync", debounce);
    }

    [Fact]
    public void BrowseModsSerializesInitialRefreshManualRefreshAndProviderSearch()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("private readonly SemaphoreSlim catalogSyncGate = new(1, 1);", source, StringComparison.Ordinal);

        var ensureStart = source.IndexOf("private async Task EnsureCatalogLoadedAsync", StringComparison.Ordinal);
        var refreshCommandStart = source.IndexOf("private async Task RefreshCatalog()", ensureStart, StringComparison.Ordinal);
        Assert.True(ensureStart >= 0 && refreshCommandStart > ensureStart);
        var ensureBody = source[ensureStart..refreshCommandStart];
        Assert.Contains("await catalogSyncGate.WaitAsync(ct);", ensureBody, StringComparison.Ordinal);
        Assert.True(
            ensureBody.IndexOf("if (catalogLoaded)", ensureBody.IndexOf("WaitAsync", StringComparison.Ordinal), StringComparison.Ordinal) >= 0,
            "Initial catalog load must re-check catalogLoaded after acquiring the sync gate.");

        var refreshCoreStart = source.IndexOf("private async Task RefreshCatalogCoreAsync", refreshCommandStart, StringComparison.Ordinal);
        Assert.True(refreshCoreStart > refreshCommandStart);
        var refreshCommandBody = source[refreshCommandStart..refreshCoreStart];
        Assert.Contains("await catalogSyncGate.WaitAsync(ct);", refreshCommandBody, StringComparison.Ordinal);
        Assert.Contains("catalogSyncGate.Release();", refreshCommandBody, StringComparison.Ordinal);

        var searchCommandStart = source.IndexOf("private async Task SearchCatalog()", refreshCoreStart, StringComparison.Ordinal);
        var providerSearchStart = source.IndexOf("private async Task SearchCatalogProvidersAsync", searchCommandStart, StringComparison.Ordinal);
        Assert.True(searchCommandStart >= 0 && providerSearchStart > searchCommandStart);
        var searchCommandBody = source[searchCommandStart..providerSearchStart];
        Assert.Contains("await catalogSyncGate.WaitAsync(ct);", searchCommandBody, StringComparison.Ordinal);
        Assert.Contains("catalogSyncGate.Release();", searchCommandBody, StringComparison.Ordinal);
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

    [Fact]
    public void ModLibraryTogglesExposeTargetSpecificAutomationNames()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains("IsChecked=\"{Binding StagedEnabled,Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("AutomationProperties.HelpText=", xaml);
        Assert.Contains("PendingStateLabel", xaml);
        Assert.Contains(
            "<CheckBox IsChecked=\"{Binding Enabled,Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}\" VerticalAlignment=\"Center\" AutomationProperties.Name=\"{Binding Label}\"",
            xaml);
    }
    [Fact]
    public void ModLibraryPendingActionsDisableWhenNothingIsStaged()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var main = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("Content=\"Preview Mod Changes\" Command=\"{Binding PreviewApplyCommand}\" Style=\"{StaticResource QuietButton}\" MinHeight=\"32\" Padding=\"9,5\" Margin=\"2,0\" ToolTip=\"See exactly what would change without editing game files.\" IsEnabled=\"{Binding HasStagedChanges}\" AutomationProperties.HelpText=\"{Binding PendingActionsHelp}\"", xaml);
        Assert.Contains("Content=\"Discard Pending Changes\" Command=\"{Binding DiscardStagedCommand}\" Style=\"{StaticResource QuietButton}\" MinHeight=\"32\" Padding=\"9,5\" Margin=\"2,0\" ToolTip=\"Discard all pending enable/disable changes. Installed game files are not changed.\" IsEnabled=\"{Binding HasStagedChanges}\" AutomationProperties.HelpText=\"{Binding PendingActionsHelp}\"", xaml);
        Assert.Contains("Content=\"Apply Mod Changes\" Command=\"{Binding ApplyCommand}\" Style=\"{StaticResource PrimaryButton}\" MinHeight=\"32\" Padding=\"10,5\" Margin=\"2,0\" ToolTip=\"Safely install your pending mod changes. Ctrl+Enter.\" IsEnabled=\"{Binding HasStagedChanges}\" AutomationProperties.HelpText=\"{Binding PendingActionsHelp}\"", xaml);
        Assert.DoesNotContain("Style=\"{StaticResource ActionTile}\" Content=\"Preview Mod Changes\" Command=\"{Binding PreviewApplyCommand}\" IsEnabled=\"{Binding HasStagedChanges}\"", xaml);
        Assert.Contains("public bool HasStagedChanges", main);
        Assert.Contains("return StagedCount>0;", main);
        Assert.Contains("public string PendingActionsHelp", main);
        Assert.Contains("OnPropertyChanged(nameof(HasStagedChanges));", main);
        Assert.Contains("OnPropertyChanged(nameof(PendingActionsHelp));", main);
    }

    [Fact]
    public void ModLibraryBulkActionsExposeCountAndSnapshotVisibleRows()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var main = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var rows = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "Rows.cs"));

        Assert.Contains("EnableVisibleLabel", xaml);
        Assert.Contains("DisableVisibleLabel", xaml);
        Assert.Contains("HasVisibleMods", xaml);
        Assert.Contains("var targets=ModsView.Cast<ModRowViewModel>().ToArray();", main);
        Assert.Contains("if(targets.Length>1&&MessageBox.Show(", main);
        Assert.Contains("public string PendingStateLabel", rows);
        Assert.Contains("OnPropertyChanged(nameof(PendingStateLabel));", rows);
    }

    [Fact]
    public void ModLibraryImagesAndIssueActionsExposeContextualAutomationNames()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        Assert.Contains(
            "AutomationProperties.Name=\"{Binding DataContext.DisplayName,RelativeSource={RelativeSource AncestorType=DataGridRow},StringFormat='Preview image for {0}'}\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding DisplayName,StringFormat='Thumbnail for {0}'}\"",
            xaml);
        Assert.Contains(
            "Content=\"Clear mark\" Command=\"{Binding DataContext.ClearIssueSuspectCommand,RelativeSource={RelativeSource AncestorType=Window}}\" CommandParameter=\"{Binding ModId}\"",
            xaml);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding DisplayName,StringFormat='Clear problem mark for {0}'}\"",
            xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"{Binding}\"", xaml);
        Assert.DoesNotContain("ConverterParameter=320}\" Stretch=\"UniformToFill\" ToolTip=\"{Binding}\"", xaml);
    }

    [Fact]
    public void SettingsStorageCardSeparatesDurableDataFromBoundedUpdaterReclaim()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Settings.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("ItemsSource=\"{Binding StorageCategories}\"", xaml);
        Assert.Contains("Command=\"{Binding RefreshStorageUsageCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding ReclaimDisposableStorageCommand}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Reclaim disposable storage\"", xaml);
        Assert.Contains("It preserves installed Mods, State, archives, catalog scratch, unknown files, live pending update data, nonterminal recovery data, and reparse paths.", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);

        Assert.Contains("\"Installed Mods\"", settings);
        Assert.Contains("\"Manager State (total)\"", settings);
        Assert.Contains("\"Catalog Download Scratch\"", settings);
        Assert.Contains("\"Updater Staging\"", settings);
        Assert.Contains("\"Updater Recovery\"", settings);
        Assert.Contains("UpdateStorageMaintenance.RunAsync(", settings);
        Assert.Contains("CatalogDownloadMaintenance.RunAsync(", settings);
        Assert.Contains("\"storage.reclaim-disposable\"", settings);
        Assert.Contains("SafeRecursiveTraversal.Snapshot(root, ct)", settings);
        Assert.DoesNotContain("Directory.Delete(", settings);
        Assert.Contains("if(value==7)_=EnsureStorageUsageLoadedAsync(backgroundCts.Token);", main);
    }

    [Fact]
    public void StorageUsageProbeMeasuresOwnedFilesAndFormatsBytes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mhwmm-storage-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[1024]);
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, "b.bin"), new byte[2048]);

            Assert.Equal(3072, StorageUsageProbe.MeasureTree(root, TestContext.Current.CancellationToken));
            Assert.Equal("3.0 KiB", MainWindowViewModel.FormatStorageBytes(3072));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AdvancedToolsInputsExposeContextualAutomationNames()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "WorkflowWindow.xaml"));

        var expectedNames = new[]
        {
            "Search file decisions",
            "Imported profile name",
            "Profile name",
            "Parent profile",
            "Left profile",
            "Right profile",
            "Rule type",
            "First mod",
            "Second mod or preferred winner",
            "Rule path or scope",
            "Rule reason",
            "Mod to inspect relationships",
            "Current mod",
            "Updated mod",
        };

        foreach (var name in expectedNames)
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", xaml);

        Assert.Contains("x:Name=\"ProfileA\" Width=\"220\" DisplayMemberPath=\"Name\" AutomationProperties.Name=\"Left profile\"", xaml);
        Assert.Contains("x:Name=\"ProfileB\" Width=\"220\" DisplayMemberPath=\"Name\" AutomationProperties.Name=\"Right profile\"", xaml);
        Assert.Contains("x:Name=\"OldMod\" Width=\"260\" DisplayMemberPath=\"DisplayName\" AutomationProperties.Name=\"Current mod\"", xaml);
        Assert.Contains("x:Name=\"NewMod\" Width=\"260\" DisplayMemberPath=\"DisplayName\" AutomationProperties.Name=\"Updated mod\"", xaml);
    }

    [Fact]
    public void SafeUpgradeProcedureUsesCurrentReleaseWording()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var start = readme.IndexOf("## Safe upgrade procedure", StringComparison.Ordinal);
        var end = readme.IndexOf("## Source layout", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var procedure = readme[start..end];
        Assert.DoesNotContain("v8.5.0", procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current release", procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Mods`", procedure, StringComparison.Ordinal);
        Assert.Contains("`State`", procedure, StringComparison.Ordinal);
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

    [Fact]
    public void Browse_mods_surfaces_provider_coverage_limits()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Text=\"{Binding CatalogCoverageSummary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("public string CatalogCoverageSummary", viewModel, StringComparison.Ordinal);
        Assert.Contains("REST v3 trending feed only; full-catalog discovery requires a separately supported provider index", viewModel, StringComparison.Ordinal);
        Assert.Contains("resumable browse pages indexed locally", viewModel, StringComparison.Ordinal);
    }
}
