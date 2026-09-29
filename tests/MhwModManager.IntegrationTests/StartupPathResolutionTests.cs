using MhwModManager.App;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class StartupPathResolutionTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhw-startup-paths-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Release_layout_resolves_manager_home_to_project_root()
    {
        var versionRoot = Path.Combine(root, "release", "8.8.16");
        Directory.CreateDirectory(versionRoot);
        File.WriteAllText(Path.Combine(root, "Build.bat"), "@echo off");

        var resolved = AppPaths.ResolveToolRoot(null, null, versionRoot);

        Assert.Equal(Path.GetFullPath(root), resolved);
    }

    [Fact]
    public void Explicit_manager_home_wins_over_release_layout()
    {
        var versionRoot = Path.Combine(root, "release", "8.8.16");
        var configured = Path.Combine(root, "configured-home");
        Directory.CreateDirectory(versionRoot);
        Directory.CreateDirectory(configured);
        File.WriteAllText(Path.Combine(root, "Build.bat"), "@echo off");

        var resolved = AppPaths.ResolveToolRoot(configured, null, versionRoot);

        Assert.Equal(Path.GetFullPath(configured), resolved);
    }

    [Fact]
    public void Ordinary_install_root_remains_the_manager_home_without_configuration()
    {
        var installRoot = Path.Combine(root, "ordinary-install");
        Directory.CreateDirectory(installRoot);

        var resolved = AppPaths.ResolveToolRoot(null, null, installRoot);

        Assert.Equal(Path.GetFullPath(installRoot), resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
