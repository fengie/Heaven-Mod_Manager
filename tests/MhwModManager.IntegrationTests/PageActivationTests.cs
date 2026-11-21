using MhwModManager.App.ViewModels;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class PageActivationTests
{
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

    private static async Task WaitUntilAsync(Func<bool> assertion, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (!assertion()) await Task.Delay(20, timeout.Token);
    }
}
