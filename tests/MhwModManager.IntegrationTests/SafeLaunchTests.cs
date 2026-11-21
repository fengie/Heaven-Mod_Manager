using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class SafeLaunchTests
{
    [Theory]
    [MemberData(nameof(DiscoveredGameLifecycleTests.ProfileIds), MemberType = typeof(DiscoveredGameLifecycleTests))]
    public Task MissingAndLockedExecutablesLeaveTheAppliedSetupIntact(string id) =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            using var vm = new MainWindowViewModel(services with { StartupArguments = ["--offline-catalog"] });
            var game = services.Paths.Game;
            var source = Directory.CreateDirectory(Path.Combine(services.Paths.ToolRoot, "source package"));
            var payload = Path.Combine(source.FullName, game.AdapterId == "unreal-paks" ? "sample.pak" : "sample.bin");
            await File.WriteAllTextAsync(payload, "mod bytes", ct);
            var mod = new ModDescriptor("sample", "Sample", "Sample", source.FullName, true, 1);
            await services.Database.UpsertModAsync(mod, ct);
            var file = Assert.Single(await services.Scanner.CaptureAsync(mod, ct));
            var live = services.Executor.Destination(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            // Alternate the two real deployment shapes: a mod-only file and a mod
            // taking over a stock file that safe mode temporarily restores.
            var hasOriginal = game.AdapterId == "unreal-paks";
            if (hasOriginal) await File.WriteAllTextAsync(live, "original game bytes", ct);
            var plan = services.Planner.Build(await services.PlannerSnapshots.LoadAsync(ct));
            Assert.True((await services.Executor.ApplyAsync(plan, "Enable sample", ct: ct)).Success);
            var before = await services.PlannerSnapshots.LoadAsync(ct);
            var stock = Path.Combine(Path.GetDirectoryName(live)!, "untouched.dat");
            await File.WriteAllTextAsync(stock, "stock bytes", ct);
            var waiting = Directory.CreateDirectory(Path.Combine(services.Paths.ToolRoot, "unapplied package"));
            var waitingPayload = Path.Combine(waiting.FullName, "unapplied" + Path.GetExtension(payload));
            await File.WriteAllTextAsync(waitingPayload, "unapplied bytes", ct);
            var unapplied = mod with { Id = "unapplied", SourcePath = waiting.FullName };
            await services.Database.UpsertModAsync(unapplied, ct);
            var waitingFile = Assert.Single(await services.Scanner.CaptureAsync(unapplied, ct));
            File.Delete(game.ExecutablePath);

            await vm.LaunchSafeModeCommand.ExecuteAsync(null);
            Assert.True(File.Exists(live), vm.StatusText);
            Assert.Contains("unavailable", vm.StatusText, StringComparison.OrdinalIgnoreCase);

            await File.WriteAllBytesAsync(game.ExecutablePath, new byte[] { 1, 2, 3 }, ct);
            using (var locked = new FileStream(game.ExecutablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await vm.LaunchSafeModeCommand.ExecuteAsync(null);

            Assert.Contains("was restored", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(live), vm.StatusText);
            Assert.Equal("mod bytes", await File.ReadAllTextAsync(live, ct));
            Assert.Equal("stock bytes", await File.ReadAllTextAsync(stock, ct));
            Assert.Equal("mod bytes", await File.ReadAllTextAsync(payload, ct));
            var after = await services.PlannerSnapshots.LoadAsync(ct);
            Assert.Equal(2, after.Mods.Count);
            Assert.All(after.Mods, m => Assert.True(m.Enabled));
            Assert.Single(after.CurrentManifest);
            Assert.Equal(before.Originals.OrderBy(item => item.Key), after.Originals.OrderBy(item => item.Key));
            Assert.False(File.Exists(services.Executor.Destination(waitingFile.Path)));
            Assert.Equal(waitingFile.Path, Assert.Single(services.Planner.Build(after).Changes).Path);
        }, DiscoveredGameProfiles.Load().Single(game => game.Id == id));
}
