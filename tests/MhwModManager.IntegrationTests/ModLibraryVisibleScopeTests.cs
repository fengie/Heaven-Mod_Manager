using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ModLibraryVisibleScopeTests
{
    [Fact]
    public void Refresh_visible_targets_every_member_of_every_visible_row()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));

        var start = source.IndexOf("private async Task ReindexVisible()", StringComparison.Ordinal);
        var end = source.IndexOf("[RelayCommand]", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0, "Refresh Visible command implementation was not found.");
        if (end < 0) end = source.Length;

        var block = source[start..end];
        Assert.Contains(
            "var visible=ModsView.Cast<ModRowViewModel>().ToArray();",
            block,
            StringComparison.Ordinal);
        Assert.Contains(
            ".SelectMany(x=>x.Members)",
            block,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "StagedMemberDescriptors",
            block,
            StringComparison.Ordinal);
        Assert.Contains(
            "await s.Catalog.EnsureCapturedAsync(target,ct);",
            block,
            StringComparison.Ordinal);
        Assert.Contains(
            "visible.Length",
            block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_bulk_labels_and_help_share_the_filtered_row_count()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("public string EnableVisibleLabel", source, StringComparison.Ordinal);
        Assert.Contains("public string DisableVisibleLabel", source, StringComparison.Ordinal);
        Assert.Contains("public string RefreshVisibleLabel", source, StringComparison.Ordinal);
        Assert.Contains("public string VisibleBulkScopeHelp", source, StringComparison.Ordinal);
        Assert.Contains(
            "Bulk actions affect all {VisibleModCount}",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "OnPropertyChanged(nameof(RefreshVisibleLabel));",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "OnPropertyChanged(nameof(VisibleBulkScopeHelp));",
            source,
            StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException(
            "Could not locate repository root from test base directory.");
    }
}
