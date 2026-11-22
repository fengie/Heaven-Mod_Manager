using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class RestoreWorkingSetupTests
{
    [Theory]
    [MemberData(nameof(DiscoveredGameLifecycleTests.ProfileIds), MemberType = typeof(DiscoveredGameLifecycleTests))]
    public Task RestoreButtonRemovesLaterDeploymentAndPersistsDisabledState(string id) =>
        IsolatedWpfSession.RunAsync(async (s, ct) =>
        {
            using var vm = new MainWindowViewModel(s with { StartupArguments = ["--offline-catalog"] });
            await vm.RestoreLastGoodCommand.ExecuteAsync(null);
            Assert.Contains("No last-known-good", vm.StatusText, StringComparison.OrdinalIgnoreCase);

            var source = Directory.CreateDirectory(Path.Combine(s.Paths.ToolRoot, "source package")).FullName;
            var payload = Path.Combine(source, s.Paths.Game.AdapterId == "unreal-paks" ? "later.pak" : "later.bin");
            await File.WriteAllTextAsync(payload, "keep source bytes", ct);
            await s.LastGood.RecordAsync(null, ct);
            var later = new ModDescriptor("later", "Later", "Later", source, true, 17);
            await s.Database.UpsertModAsync(later, ct);
            var file = Assert.Single(await s.Scanner.CaptureAsync(later, ct));
            var initial = s.Planner.Build(await s.PlannerSnapshots.LoadAsync(ct));
            Assert.False(initial.IsBlocked);
            Assert.True((await s.Executor.ApplyAsync(initial, "Enable later package", ct: ct)).Success);
            var deployed = s.Executor.Destination(file.Path);
            Assert.Equal("keep source bytes", await File.ReadAllTextAsync(deployed, ct));
            var stock = Path.Combine(Path.GetDirectoryName(deployed)!, "stock.dat");
            await File.WriteAllTextAsync(stock, "stock bytes", ct);

            await vm.RestoreLastGoodCommand.ExecuteAsync(null);

            Assert.False(File.Exists(deployed), vm.StatusText);
            var restored = Assert.Single(await s.Database.GetModsAsync(ct));
            Assert.False(restored.Enabled);
            Assert.Equal(17, restored.Priority);
            Assert.Empty((await s.PlannerSnapshots.LoadAsync(ct)).CurrentManifest);
            Assert.Empty(s.Planner.Build(await s.PlannerSnapshots.LoadAsync(ct)).Changes);
            Assert.Equal("stock bytes", await File.ReadAllTextAsync(stock, ct));
            Assert.Equal("keep source bytes", await File.ReadAllTextAsync(payload, ct));
        }, DiscoveredGameProfiles.Load().Single(game => game.Id == id));
}
