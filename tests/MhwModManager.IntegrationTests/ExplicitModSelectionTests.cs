using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ExplicitModSelectionTests
{
    private static readonly string[] FixtureIds = ["one", "two", "three"];

    [Fact]
    public Task MarkingAndBulkStagingNeverDeployUntilApply() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            using var vm = new MainWindowViewModel(services) { ConfirmBeforeApply = false };
            vm.BackgroundMetadataRefreshEnabled = false;
            try
            {
                var files = new Dictionary<string, string>(PathRules.Comparer);
                foreach (var id in FixtureIds)
                {
                    var root = Directory.CreateDirectory(Path.Combine(services.Paths.ToolRoot, id));
                    await File.WriteAllTextAsync(Path.Combine(root.FullName, id + ".bin"), id, ct);
                    var mod = new ModDescriptor(id, id, id, root.FullName, false, 1);
                    await services.Database.UpsertModAsync(mod, ct);
                    var file = Assert.Single(await services.Scanner.CaptureAsync(mod, ct));
                    files.Add(id, services.Executor.Destination(file.Path));
                    vm.Mods.Add(new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([mod])), () => { }));
                }

                var selected = vm.Mods.Where(row => row.Id != vm.Mods[2].Id).ToArray();
                foreach (var row in selected) row.BulkSelected = true;
                Assert.Equal(2, vm.BulkSelectedCount);
                Assert.All(vm.Mods, row => Assert.False(row.AppliedEnabled));
                Assert.Equal(0, vm.StagedCount);
                Assert.All(files.Values, file => Assert.False(File.Exists(file)));
                Assert.All(await services.Database.GetModsAsync(ct), mod => Assert.False(mod.Enabled));

                await vm.EnableSelectedCommand.ExecuteAsync(null);
                Assert.Equal(2, vm.StagedCount);
                Assert.True(vm.HasPendingDeployment);
                Assert.All(selected, row => Assert.Equal("Pending enable", row.PendingStateLabel));
                Assert.All(files.Values, file => Assert.False(File.Exists(file)));
                Assert.All(await services.Database.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
                await vm.ApplyCommand.ExecuteAsync(null);
                Assert.Equal(2, (await services.Database.GetModsAsync(ct)).Count(mod => mod.Enabled));
                Assert.Equal("one", await File.ReadAllTextAsync(files["one"], ct));
                Assert.Equal("two", await File.ReadAllTextAsync(files["two"], ct));
                Assert.False(File.Exists(files["three"]));
                Assert.Equal(0, vm.StagedCount);
                Assert.False(vm.HasPendingDeployment);
                Assert.All(vm.Mods, row => Assert.Empty(row.PendingStateLabel));
                vm.ClearBulkSelectionCommand.Execute(null);
                vm.SetModViewCommand.Execute("Disabled");
                Assert.Single(vm.ModsView.Cast<ModRowViewModel>());
                Assert.Equal("three", Assert.Single(vm.ModsView.Cast<ModRowViewModel>()).DisplayName);

                vm.SetModViewCommand.Execute("Enabled");
                vm.SelectAllVisibleCommand.Execute(null);
                await vm.DisableSelectedCommand.ExecuteAsync(null);
                Assert.Equal(2, vm.StagedCount);
                Assert.True(vm.HasPendingDeployment);
                Assert.True(File.Exists(files["one"]));
                Assert.True(File.Exists(files["two"]));
                Assert.Equal(2, (await services.Database.GetModsAsync(ct)).Count(mod => mod.Enabled));
                await vm.ApplyCommand.ExecuteAsync(null);
                Assert.False(vm.HasPendingDeployment);
                Assert.All(vm.Mods, row => Assert.Empty(row.PendingStateLabel));
                Assert.All(files.Values, file => Assert.False(File.Exists(file)));
                Assert.All(await services.Database.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
                vm.SetModViewCommand.Execute("Disabled");
                Assert.Equal(3, vm.ModsView.Cast<ModRowViewModel>().Count());
                vm.ClearBulkSelectionCommand.Execute(null);
                await vm.AutoPopulateCommand.ExecuteAsync(null);
                Assert.True(vm.HasStagedChanges, vm.StatusText);
                Assert.All(vm.Mods, row => Assert.False(row.AppliedEnabled));
                Assert.All(await services.Database.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
                Assert.All(files.Values, file => Assert.False(File.Exists(file)));
            }
            finally
            {
                await vm.StopPageReadsAsync();
                Assert.False(vm.PageReadsInProgress);
            }
        });

    [Fact]
    public Task CommittedApplySettlesPendingEvenWhenPostCommitRevalidationFails() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            using var vm = new MainWindowViewModel(services) { ConfirmBeforeApply = false };
            vm.BackgroundMetadataRefreshEnabled = false;
            try
            {
                var root = Directory.CreateDirectory(Path.Combine(services.Paths.ToolRoot, "settle"));
                await File.WriteAllTextAsync(Path.Combine(root.FullName, "settle.bin"), "committed bytes", ct);
                var mod = new ModDescriptor("settle", "settle", "Settle", root.FullName, false, 1);
                await services.Database.UpsertModAsync(mod, ct);
                var file = Assert.Single(await services.Scanner.CaptureAsync(mod, ct));
                var destination = services.Executor.Destination(file.Path);
                var row = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([mod])), () => { });
                vm.Mods.Add(row);
                row.StagedEnabled = true;
                await using (var connection = await services.Database.OpenAsync(ct))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        CREATE TRIGGER fixture_fail_post_commit_revalidation
                        BEFORE INSERT ON mod_revalidation
                        WHEN NEW.mod_id = 'settle' AND NEW.required = 0
                        BEGIN SELECT RAISE(ABORT, 'fixture refresh failure'); END;
                        """;
                    await command.ExecuteNonQueryAsync(ct);
                }

                await vm.ApplyCommand.ExecuteAsync(null);
                Assert.True(Assert.Single(await services.Database.GetModsAsync(ct)).Enabled);
                Assert.Equal("committed bytes", await File.ReadAllTextAsync(destination, ct));
                Assert.True(row.AppliedEnabled);
                Assert.False(row.HasStagedChanges);
                Assert.Empty(row.PendingStateLabel);
                Assert.Equal(0, vm.StagedCount);
                Assert.False(vm.HasPendingDeployment);
                Assert.Contains("Applied", vm.StatusText);
                Assert.Contains("refresh", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            }
            finally { await vm.StopPageReadsAsync(); }
        });

    [Fact]
    public Task FailedApplyKeepsTheUncommittedChoicePending() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            using var vm = new MainWindowViewModel(services) { ConfirmBeforeApply = false };
            vm.BackgroundMetadataRefreshEnabled = false;
            try
            {
                var empty = Directory.CreateDirectory(Path.Combine(services.Paths.ToolRoot, "empty"));
                var mod = new ModDescriptor("empty", "empty", "Empty", empty.FullName, false, 1);
                await services.Database.UpsertModAsync(mod, ct);
                var row = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([mod])), () => { });
                vm.Mods.Add(row);
                row.StagedEnabled = true;

                await vm.ApplyCommand.ExecuteAsync(null);
                Assert.False(Assert.Single(await services.Database.GetModsAsync(ct)).Enabled);
                Assert.False(row.AppliedEnabled);
                Assert.Equal("Pending enable", row.PendingStateLabel);
                Assert.Equal(1, vm.StagedCount);
                Assert.True(vm.HasPendingDeployment);
                Assert.Empty((await services.PlannerSnapshots.LoadAsync(ct)).CurrentManifest);
                Assert.Contains("empty source folder", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            }
            finally { await vm.StopPageReadsAsync(); }
        });

    [Fact]
    public void BulkMarkDoesNotChangeFamilyComponentsOrActivation()
    {
        var baseMod = new ModDescriptor("base", "Base", "Base", "base", false, 1,
            FamilyId: "manual:fixture", FamilyRole: "Main");
        var child = new ModDescriptor("child", "Child", "Child", "child", false, 2,
            FamilyId: "manual:fixture", FamilyRole: "Optional");
        var row = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([baseMod, child])), () => { });
        row.BulkSelected = true;
        Assert.False(row.AppliedEnabled);
        Assert.False(row.HasStagedChanges);
        Assert.All(row.ExpandStage(), entry => Assert.False(entry.Value.enabled));
        row.BulkSelected = false;
        Assert.False(row.HasStagedChanges);
    }
}
