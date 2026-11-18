using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Structural UI contract for the opt-in cross-source relationship workflow.
/// The installed WPF application still requires separate runtime/UIA acceptance.
/// </summary>
public sealed class FamilySuggestionUiWiringTests
{
    [Fact]
    public void Mods_page_exposes_review_filter_and_confirm_action()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));
        var command = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.FamilySuggestions.cs"));

        Assert.Contains("FamilyLinks", xaml);
        Assert.Contains("HasSuggestedParents", xaml);
        Assert.Contains("SuggestedParents", xaml);
        Assert.Contains("ConfirmSuggestedFamilyCommand", xaml);
        Assert.Contains("Attach as add-on", xaml);
        Assert.Contains("ModRelationshipInference.Suggest", viewModel);
        Assert.Contains("private async Task ConfirmSuggestedFamily", command);
        Assert.Contains("ChainManualFamilyAsync", command);
        Assert.Contains("MessageBoxButton.YesNo", command);
    }

    private static string FindRepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "MhwModManager.sln")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
