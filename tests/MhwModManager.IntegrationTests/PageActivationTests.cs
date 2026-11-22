using MhwModManager.Core;
using MhwModManager.App.ViewModels;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class PageActivationTests
{
    [Fact]
    public Task ClosingAnInitializedSessionDrainsInboxWatchersBeforeDisposal() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var vm = new MainWindowViewModel(services);
            await vm.InitializeAsync();
            await vm.StopPageReadsAsync();
            vm.Dispose();
            Assert.False(vm.PageReadsInProgress);
        });

    [Fact]
    public Task ReturningToProfilesAndHistoryPicksUpChangesMadeWhileHidden() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            using var vm = new MainWindowViewModel(services);
            for (var visit = 1; visit <= 2; visit++)
            {
                string name = $"External profile {visit}";
                await services.Profiles.SaveCurrentAsync(name, ct);
                vm.SelectedTab = 4;
                await WaitUntilAsync(() => vm.Profiles.Any(row => row.Name == name), ct);

                await using var connection = await services.Database.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO automation_events(id,time,kind,severity,message)
                    VALUES ($id,datetime('now'),'fixture','Info',$message)
                    """;
                string id = $"page-activation-{visit}";
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$message", name);
                await command.ExecuteNonQueryAsync(ct);
                vm.SelectedTab = 5;
                await WaitUntilAsync(() => vm.ActivityRows.Any(row => row.Id == id), ct);
            }
            Assert.Equal(2, vm.Profiles.Count(row => row.Name.StartsWith("External profile", StringComparison.Ordinal)));
        });

    [Fact]
    public Task ReturningToModsFindsCopiedFolderAndPreservesCurrentEdit() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            services.Settings.Update(settings=>settings.BackgroundMetadataRefreshEnabled=false);
            using var vm=new MainWindowViewModel(services);
            DirectoryInfo WritePackage(string name)
            {
                var dir=Directory.CreateDirectory(Path.Combine(services.Paths.ModsRoot,name));
                File.WriteAllText(Path.Combine(dir.FullName,"fixture.txt"),name);
                // A copied shelf can retain the same parent timestamp on multiple visits.
                Directory.SetLastWriteTimeUtc(services.Paths.ModsRoot,new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc));
                return dir;
            }
            var first=WritePackage("First Fixture");
            vm.SelectedTab=1;
            await WaitUntilAsync(()=>vm.Mods.Any(row=>row.Members.Any(m=>m.SourcePath==first.FullName)),ct);
            Assert.False(services.Catalog.TopLevelFoldersChanged());
            var editing=Assert.Single(vm.Mods);
            vm.SelectedLibraryItem=editing;
            editing.StagedEnabled=true;
            vm.SelectedTab=4;
            var added=WritePackage("Second Fixture");
            Assert.True(services.Catalog.TopLevelFoldersChanged());
            vm.SelectedTab=1;
            await WaitUntilAsync(()=>vm.Mods.Any(row=>row.Members.Any(m=>m.SourcePath==added.FullName)),ct);
            Assert.False(services.Catalog.TopLevelFoldersChanged());
            var restored=Assert.Single(vm.Mods,row=>row.Id==editing.Id);
            Assert.True(restored.StagedEnabled);
            Assert.Same(restored,vm.SelectedLibraryItem);
            Assert.False((await services.Database.GetModsAsync(ct)).Single(m=>m.SourcePath==first.FullName).Enabled);
            Assert.Equal("Second Fixture",File.ReadAllText(Path.Combine(added.FullName,"fixture.txt")));
            await vm.StopPageReadsAsync();
            Assert.False(vm.PageReadsInProgress,"Session database users still active before fixture deletion");
        });

    [Fact]
    public Task ReturningToInstalledFilesFindsNewPackagesEvenWithinOneMinute() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            services.GameRegistry.Upsert(services.Paths.Game);
            using var vm = new MainWindowViewModel(services);
            string WritePackage(string name)
            {
                var directory = Directory.CreateDirectory(Path.Combine(services.Paths.GameRoot, "Mods", name));
                var manifest = Path.Combine(directory.FullName, "mod.json");
                File.WriteAllText(manifest, System.Text.Json.JsonSerializer.Serialize(new { name }));
                File.WriteAllText(Path.Combine(directory.FullName, "fixture.lua"), "-- Fixture");
                return manifest;
            }
            var first = WritePackage("Initial package");
            vm.SelectedTab = 9;
            await WaitUntilAsync(() => vm.VisibleLocalModLocations.Any(row => PathRules.Comparer.Equals(row.Location, first)), ct);
            vm.SelectedTab = 4;
            var next = WritePackage("Fresh package");
            vm.SelectedTab = 9;
            await WaitUntilAsync(() => vm.VisibleLocalModLocations.Any(row => PathRules.Comparer.Equals(row.Location, next)), ct);
            Assert.Contains(vm.VisibleLocalModLocations, row => PathRules.Comparer.Equals(row.Location, first));
            Assert.StartsWith("External reference", Assert.Single(vm.VisibleLocalModLocations, row => PathRules.Comparer.Equals(row.Location, next)).LibraryStatus);
            await vm.StopPageReadsAsync();
        });

    [Fact]
    public Task VisitsDuringAnInventoryScanCoalesceIntoOneFullRescanIncludingOtherGames() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var otherRoot = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GameRoot)!, "other-game"));
            var other = GameProfile.Generic("other-game", "Other game", otherRoot.FullName, "Other.exe", "Mods");
            File.WriteAllText(other.ExecutablePath, "fixture");
            services.GameRegistry.Upsert(other);
            using var vm = new MainWindowViewModel(services);
            var package = Directory.CreateDirectory(Path.Combine(other.GameRoot, "Mods", "Late"));
            var manifest = Path.Combine(package.FullName, "mod.json");
            var scans = 0;
            vm.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(vm.VisibleLocalModLocations) || ++scans != 1) return;
                // The first result is on screen but its scan task has not yet returned.
                File.WriteAllText(manifest, "{\"name\":\"Late\"}");
                File.WriteAllText(Path.Combine(package.FullName, "fixture.lua"), "-- Fixture");
                vm.SelectedTab = 5;
                vm.SelectedTab = 9;
                vm.SelectedTab = 5;
                vm.SelectedTab = 9;
            };
            vm.SelectedTab = 9;
            await WaitUntilAsync(() => vm.VisibleLocalModLocations.Any(row => PathRules.Comparer.Equals(row.Location, manifest)), ct);
            Assert.Equal(2, scans);
            Assert.Equal("Other game", Assert.Single(vm.VisibleLocalModLocations, row => PathRules.Comparer.Equals(row.Location, manifest)).LibraryStatus);
            await vm.StopPageReadsAsync();
        });

    private static async Task WaitUntilAsync(Func<bool> assertion, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // This tests eventual page-state correctness, not the isolated UI p95
        // latency budget. Shared Windows CI can run competing release builds;
        // an 8-second wall-clock deadline produced false negatives in two
        // unrelated page-activation cases while the scan still needed to finish.
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            while (!assertion()) await Task.Delay(20, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Page activation did not reach the expected state within 20 seconds; "
                + "inspect the registered-game scan and page-visit coalescing.",
                ex);
        }
    }
}
