using MhwModManager.App.ViewModels;
using MhwModManager.Automation;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ImportWorkspaceStorageVisibilityTests
{
    [Fact]
    public void Import_workspace_location_is_sibling_and_can_be_measured_without_mutating_mods()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhwmm-import-workspace-storage-" + Guid.NewGuid().ToString("N"));
        var modsRoot = Path.Combine(root, "Mods by Game", "generic-game", "Installed");
        var expected = Path.Combine(root, "Mods by Game", "generic-game", ".Installed.import-work");
        try
        {
            // A read-only Settings refresh must not create even an empty scratch directory.
            var workspace = ImportPublicationWorkspace.LocationFor(modsRoot);
            Assert.Equal(Path.GetFullPath(expected), workspace);
            Assert.False(Directory.Exists(workspace));
            Assert.False(Directory.Exists(modsRoot));

            Directory.CreateDirectory(workspace);
            File.WriteAllBytes(Path.Combine(workspace, "incomplete-package.bin"), new byte[4096]);
            Assert.Equal(4096, StorageUsageProbe.MeasureTree(workspace, TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(modsRoot));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Settings_includes_unpublished_import_bytes_without_granting_disposable_reclaim()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "MhwModManager.sln")))
            repo = repo.Parent;
        Assert.NotNull(repo);
        var source = File.ReadAllText(Path.Combine(repo!.FullName, "src", "MhwModManager.App",
            "ViewModels", "MainWindowViewModel.Settings.cs"));

        Assert.Contains("ImportPublicationWorkspace.LocationFor(s.Paths.ModsRoot)", source,
            StringComparison.Ordinal);
        Assert.Contains("\"Import Workspaces\"", source, StringComparison.Ordinal);
        Assert.Contains("never automatically reclaimed", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ImportPublicationWorkspace.Cleanup(", source, StringComparison.Ordinal);
    }
}
