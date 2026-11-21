using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using MhwModManager.App.ViewModels;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class SearchAcrossRefreshTests
{
    private static readonly string[] Names = ["Traveller Outfit", "Expedition Kit"];
    [Fact]
    public Task RebuildingLibraryRowsPreservesTheSecondSearchAndItsResult() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            services.Settings.Update(settings => settings.BackgroundMetadataRefreshEnabled = false);
            using var vm = new MainWindowViewModel(services);
            foreach (var name in Names)
            {
                var source = Directory.CreateDirectory(Path.Combine(services.Paths.ModsRoot, name));
                File.WriteAllText(Path.Combine(source.FullName, "fixture.txt"), name);
            }
            var box = new TextBox { DataContext = vm };
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.SearchText)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var peer = (IValueProvider)new TextBoxAutomationPeer(box);
            vm.SelectedTab = 1;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (vm.Mods.Count != 2) await Task.Delay(10, timeout.Token);
            peer.SetValue("Traveller Outfit");
            vm.SubmitModSearchCommand.Execute(null);
            var first = Assert.IsType<ModRowViewModel>(vm.SelectedLibraryItem);
            first.StagedEnabled = true;
            vm.SelectedTab = 4;
            var third = Directory.CreateDirectory(Path.Combine(services.Paths.ModsRoot, "Refresh witness"));
            File.WriteAllText(Path.Combine(third.FullName, "fixture.txt"), "unrelated");
            vm.SelectedTab = 1;
            peer.SetValue("Expedition Kit");
            while (vm.Mods.Count != 3) await Task.Delay(10, timeout.Token);
            Assert.NotSame(first, Assert.Single(vm.Mods, row => row.Id == first.Id));
            Assert.Equal("Expedition Kit", vm.SearchText);
            Assert.Equal("Expedition Kit", box.Text);
            Assert.Equal("Expedition Kit", Assert.IsType<ModRowViewModel>(Assert.Single(vm.LibraryView.Cast<object>())).DisplayName);
            await vm.StopPageReadsAsync();
        });
}
