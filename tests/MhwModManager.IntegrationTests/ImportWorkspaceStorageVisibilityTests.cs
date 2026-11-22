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
        Assert.Contains("lease-certified exited-owner work", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ImportWorkspaceMaintenance.Run(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportPublicationWorkspace.Cleanup(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_workspace_refuses_a_reparse_ancestor_without_creating_external_scratch()
    {
        var root = Directory.CreateTempSubdirectory("mhwmm-import-parent-link-");
        try
        {
            var external = Path.Combine(root.FullName, "external");
            Directory.CreateDirectory(external);
            var sentinel = Path.Combine(external, "keep.txt");
            File.WriteAllText(sentinel, "KEEP");
            var alias = Path.Combine(root.FullName, "alias");
            try
            {
                Directory.CreateSymbolicLink(alias, external);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or IOException
                                       or PlatformNotSupportedException)
            {
                return;
            }

            var modsRoot = Path.Combine(alias, "Installed");
            Assert.Throws<InvalidDataException>(() => ImportPublicationWorkspace.RootFor(modsRoot));
            Assert.True(File.Exists(sentinel));
            Assert.False(Directory.Exists(Path.Combine(external, ".Installed.import-work")));
        }
        finally
        {
            try { root.Delete(recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Import_publication_and_rollback_refuse_a_reparse_mods_root()
    {
        var root = Directory.CreateTempSubdirectory("mhwmm-import-destination-link-");
        try
        {
            var external = Path.Combine(root.FullName, "external");
            var userPackage = Path.Combine(external, "user-package");
            Directory.CreateDirectory(userPackage);
            var sentinel = Path.Combine(userPackage, "keep.txt");
            File.WriteAllText(sentinel, "KEEP");
            var modsRoot = Path.Combine(root.FullName, "Installed");
            try
            {
                Directory.CreateSymbolicLink(modsRoot, external);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or IOException
                                       or PlatformNotSupportedException)
            {
                return;
            }

            var workspace = ImportPublicationWorkspace.RootFor(modsRoot);
            var staging = Path.Combine(workspace, "archive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "mod.txt"), "MOD");

            Assert.Throws<InvalidDataException>(() =>
                ImportPublicationWorkspace.Publish(modsRoot, staging, Path.Combine(modsRoot, "new-package")));
            Assert.True(Directory.Exists(staging));
            Assert.False(Directory.Exists(Path.Combine(external, "new-package")));

            var rollbackFailure = ImportPublicationWorkspace.RollbackPublished(
                modsRoot, Path.Combine(modsRoot, "user-package"));
            Assert.NotNull(rollbackFailure);
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            try { root.Delete(recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
