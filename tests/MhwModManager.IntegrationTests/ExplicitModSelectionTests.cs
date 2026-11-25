using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ExplicitModSelectionTests
{
    [Fact]
    public Task SelectingRowsDoesNotDeployAndExplicitBulkActionsCommitOnce() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            using var vm = new MainWindowViewModel(services) { ConfirmBeforeApply = false };
            var files = new Dictionary<string, string>(PathRules.Comparer);
            foreach (var id in new[] { "one", "two", "three" })
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
            Assert.Equal(2, (await services.Database.GetModsAsync(ct)).Count(mod => mod.Enabled));
            Assert.Equal("one", await File.ReadAllTextAsync(files["one"], ct));
            Assert.Equal("two", await File.ReadAllTextAsync(files["two"], ct));
            Assert.False(File.Exists(files["three"]));
            Assert.Equal(0, vm.StagedCount);
            vm.SetModViewCommand.Execute("Disabled");
            Assert.Single(vm.ModsView.Cast<ModRowViewModel>());
            Assert.Equal("three", Assert.Single(vm.ModsView.Cast<ModRowViewModel>()).DisplayName);

            vm.SetModViewCommand.Execute("Enabled");
            vm.SelectAllVisibleCommand.Execute(null);
            await vm.DisableSelectedCommand.ExecuteAsync(null);
            Assert.All(files.Values, file => Assert.False(File.Exists(file)));
            Assert.All(await services.Database.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
            vm.SetModViewCommand.Execute("Disabled");
            Assert.Equal(3, vm.ModsView.Cast<ModRowViewModel>().Count());
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
