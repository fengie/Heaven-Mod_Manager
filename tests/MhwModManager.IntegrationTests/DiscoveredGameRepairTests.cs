using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DiscoveredGameRepairTests
{
    [Fact]
    public void RelocatingTheRegisteredExecutablePreservesEveryGamesLoaderRoot()
    {
        var workspace = Directory.CreateTempSubdirectory("manager-profile-relink-");
        try
        {
            var registry = new GameProfileRegistry(Path.Combine(workspace.FullName, "state"));
            foreach (var source in DiscoveredGameProfiles.Load())
            {
                var before = DiscoveredGameProfiles.Isolate(source, Path.Combine(workspace.FullName, "old"));
                var moved = DiscoveredGameProfiles.Isolate(source, Path.Combine(workspace.FullName, "new"));
                Directory.CreateDirectory(Path.GetDirectoryName(moved.ExecutablePath)!);
                File.WriteAllBytes(moved.ExecutablePath, [17, 42]);
                registry.Upsert(before);
                var after = registry.RepairFromExecutable(before, moved.ExecutablePath, activate: false);
                Assert.Equal(moved.GameRoot, after.GameRoot);
                Assert.Equal(moved.ExecutablePath, after.ExecutablePath);
                Assert.Equal(before.ExecutableRelativePath, after.ExecutableRelativePath);
                Assert.Equal(moved.LiveModRoot, after.LiveModRoot);
                Assert.Equal(before.Id, after.Id);
            }
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
