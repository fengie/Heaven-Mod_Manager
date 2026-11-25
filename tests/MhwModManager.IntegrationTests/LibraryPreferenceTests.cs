using System.Collections;
using MhwModManager.App;
using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class LibraryPreferenceTests
{
    [Fact]
    public Task PreferencesAndHighlightedSelectionAreIndependentOfActivation() =>
        IsolatedWpfSession.RunAsync(async (s, ct) =>
        {
            using var vm = new MainWindowViewModel(s);
            vm.BackgroundMetadataRefreshEnabled = false;
            try
            {
                var first = new ModDescriptor("first", "First", "First", "first", false, 1, Category: "Armor");
                var second = new ModDescriptor("second", "Second", "Second", "second", false, 2, Category: "Texture");
                await s.Database.UpsertModAsync(first, ct);
                await s.Database.UpsertModAsync(second, ct);
                var one = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([first])), () => { });
                var two = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([second])), () => { });
                vm.Mods.Add(one);
                vm.Mods.Add(two);
                vm.SelectedMod = one;
                vm.ToggleFavoriteCommand.Execute(null);
                Assert.True(one.IsFavorite);
                vm.CustomCategoryText = "My visual mods";
                vm.AssignCustomCategoryCommand.Execute(null);
                Assert.Equal("My visual mods", one.DisplayCategory);
                vm.SetModViewCommand.Execute("Favorites");
                vm.CategoryFilter = "My visual mods";
                vm.NewSavedViewName = "Favorite visuals";
                vm.SaveLibraryViewCommand.Execute(null);
                var saved = Assert.Single(vm.SavedLibraryViews);
                vm.ClearModFiltersCommand.Execute(null);
                vm.SelectedSavedLibraryView = saved;
                vm.UseSavedLibraryViewCommand.Execute(null);
                Assert.Same(one, Assert.Single(vm.ModsView.Cast<ModRowViewModel>()));
                // A highlighted row hidden by the saved filter is not marked by this command.
                vm.MarkHighlightedCommand.Execute((IList)new ArrayList { one, two, new object() });
                Assert.True(one.BulkSelected);
                Assert.False(two.BulkSelected);
                Assert.Equal(0, vm.StagedCount);
                Assert.All(await s.Database.GetModsAsync(ct), mod => Assert.False(mod.Enabled));
                Assert.Empty((await s.PlannerSnapshots.LoadAsync(ct)).CurrentManifest);

                var persisted = ManagerSettingsStore.Load(Path.Combine(s.Paths.NextStateRoot, "settings.json")).Current;
                Assert.Contains(one.LibraryPreferenceKey, persisted.FavoriteLibraryKeys);
                Assert.Equal("My visual mods", persisted.CustomLibraryCategories[one.LibraryPreferenceKey]);
                Assert.Equal(saved, Assert.Single(persisted.SavedLibraryViews));
                vm.DeleteSavedLibraryViewCommand.Execute(null);
                Assert.Empty(vm.SavedLibraryViews);
            }
            finally
            {
                await vm.StopPageReadsAsync();
                Assert.False(vm.PageReadsInProgress);
            }
        });

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public Task LargeLibraryMarkingDoesNotStageOrDeploy(int count) =>
        IsolatedWpfSession.RunAsync(async (s, ct) =>
        {
            using var vm = new MainWindowViewModel(s);
            for (var i = 0; i < count; i++)
            {
                var id = $"mod-{i}";
                var mod = new ModDescriptor(id, id, id, id, false, i);
                vm.Mods.Add(new ModRowViewModel(new LogicalModFamily(id, id, id, [mod], null), () => { }));
            }

            vm.SelectAllVisibleCommand.Execute(null);
            Assert.Equal(count, vm.BulkSelectedCount);
            Assert.Equal(0, vm.StagedCount);
            Assert.All(vm.Mods, row => Assert.False(row.AppliedEnabled));
            vm.ClearBulkSelectionCommand.Execute(null);
            Assert.Equal(0, vm.BulkSelectedCount);
            Assert.Empty((await s.PlannerSnapshots.LoadAsync(ct)).CurrentManifest);
        });

    [Fact]
    public void PreferenceIdentityIsOrderIndependentButChangesWithFamilyMembership()
    {
        var first = new ModDescriptor("first", "First", "First", "first", false, 1);
        var second = new ModDescriptor("second", "Second", "Second", "second", false, 2);
        var a = new ModRowViewModel(new LogicalModFamily("f", "F", "F", [first, second], null), () => { });
        var b = new ModRowViewModel(new LogicalModFamily("g", "G", "G", [second, first], null), () => { });
        var c = new ModRowViewModel(new LogicalModFamily("f", "F", "F", [first], null), () => { });
        Assert.Equal(a.LibraryPreferenceKey, b.LibraryPreferenceKey);
        Assert.NotEqual(a.LibraryPreferenceKey, c.LibraryPreferenceKey);
    }
}
