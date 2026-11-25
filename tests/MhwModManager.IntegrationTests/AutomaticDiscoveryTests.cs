using MhwModManager.App.ViewModels;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class AutomaticDiscoveryTests
{
    public static TheoryData<string> Profiles => DiscoveredGameLifecycleTests.ProfileIds;

    [Theory]
    [MemberData(nameof(Profiles))]
    public Task FreshDataAppliesOnceAndPreservesExistingChoices(string gameId) =>
        IsolatedWpfSession.RunAsync(async (s,ct) =>
        {
            var game=s.Paths.Game;
            var isPak=game.AdapterId=="unreal-paks";
            var filename=isPak?"newoutfit.pak":"newoutfit.bin";
            var relative=isPak && Path.GetFileName(game.ModRootRelativePath).Equals("Paks",StringComparison.OrdinalIgnoreCase)
                ?Path.Combine("~mods",filename):filename;
            byte[] payload=[8,4,9];
            var source=Path.Combine(s.Paths.ModsRoot,"New Outfit",game.ModRootRelativePath,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllBytesAsync(source,payload,ct);
            await s.Catalog.RefreshFoldersAsync(ct);
            var candidates=await s.Database.GetSettingsByPrefixAsync("discovery.pending:",ct);
            var id=Assert.Single(candidates.Values);
            var executableBytes=await File.ReadAllBytesAsync(game.ExecutablePath,ct);
            File.Delete(game.ExecutablePath);
            Assert.Null(await MainWindowViewModel.ApplyDiscoveredAsync(s,[id],ct));
            Assert.Single(await s.Database.GetSettingsByPrefixAsync("discovery.pending:",ct));
            await File.WriteAllBytesAsync(game.ExecutablePath,executableBytes,ct);
            Assert.Equal(1,await MainWindowViewModel.ApplyDiscoveredAsync(s,[id],ct));
            var target=Path.Combine(game.LiveModRoot,relative);
            Assert.Equal(payload,await File.ReadAllBytesAsync(target,ct));
            Assert.True(Assert.Single(await s.Database.GetModsAsync(ct)).Enabled);
            var undo=await s.Executor.UndoLastAsync(ct);
            Assert.True(undo.Success,undo.Message);
            await s.Catalog.RefreshFoldersAsync(ct);
            Assert.Empty(await new ManagerDatabase(s.Paths.DatabasePath).GetSettingsByPrefixAsync("discovery.pending:",ct));
            // Replay an already-fetched batch after Undo on the same catalog.
            Assert.Equal(0,await MainWindowViewModel.ApplyDiscoveredAsync(s,[id],ct));
            Assert.False(File.Exists(target));
            Assert.Equal(payload,File.ReadAllBytes(source));
        },DiscoveredGameProfiles.Load().Single(game=>game.Id==gameId));

    [Fact]
    public Task ImportDisabledPreferencePersistsAndDoesNotReplayAlreadyReviewedPackages() =>
        IsolatedWpfSession.RunAsync(async (s, ct) =>
        {
            using var vm = new MainWindowViewModel(s);
            Assert.True(vm.AutoEnableDiscoveredMods);
            vm.AutoEnableDiscoveredMods = false;
            Assert.False(MhwModManager.App.ManagerSettingsStore.Load(Path.Combine(s.Paths.NextStateRoot, "settings.json"))
                .Current.AutoEnableDiscoveredMods);
            var source = Path.Combine(s.Paths.ModsRoot, "Staged only", s.Paths.Game.ModRootRelativePath, "staged.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllTextAsync(source, "keep original", ct);
            await s.Catalog.RefreshFoldersAsync(ct);
            var pending = await s.Database.GetSettingsByPrefixAsync("discovery.pending:", ct);
            var id = Assert.Single(pending.Values);
            Assert.Equal(0, await MainWindowViewModel.ApplyDiscoveredAsync(s, [id], ct));
            Assert.Empty(await s.Database.GetSettingsByPrefixAsync("discovery.pending:", ct));
            Assert.False(Assert.Single(await s.Database.GetModsAsync(ct)).Enabled);
            var target = Path.Combine(s.Paths.Game.LiveModRoot, "staged.bin");
            Assert.False(File.Exists(target));
            vm.AutoEnableDiscoveredMods = true;
            await s.Catalog.RefreshFoldersAsync(ct);
            Assert.Equal(0, await MainWindowViewModel.ApplyDiscoveredAsync(s, [id], ct));
            Assert.False(File.Exists(target));
            Assert.Equal("keep original", await File.ReadAllTextAsync(source, ct));
        });

    [Fact]
    public Task OldChoicesForeignFilesAndExecutablePayloadStayUntouched() =>
        IsolatedWpfSession.RunAsync(async (s,ct)=>
        {
            var live=s.Paths.Game.LiveModRoot;
            Directory.CreateDirectory(live);
            var originals=new Dictionary<string,byte[]>();
            foreach(var (label,file) in new[]{("Earlier","earlier.bin"),("Foreign","foreign.bin"),("Script","risky.ps1"),("Independent","safe.bin")})
            {
                var path=Path.Combine(s.Paths.ModsRoot,label,s.Paths.Game.ModRootRelativePath,file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path,[4,6,8],ct);
                originals.Add(path,[4,6,8]);
                if(label=="Earlier")
                {
                    await s.Catalog.RefreshFoldersAsync(ct);
                    var existing=await s.Database.GetSettingsByPrefixAsync("discovery.pending:",ct);
                    await s.Database.FinishDiscoveryAsync(existing.Values,ct);
                }
            }
            await File.WriteAllBytesAsync(Path.Combine(live,"foreign.bin"),[7],ct);
            await s.Catalog.RefreshFoldersAsync(ct);
            var pending=await s.Database.GetSettingsByPrefixAsync("discovery.pending:",ct);
            Assert.Equal(1,await MainWindowViewModel.ApplyDiscoveredAsync(s,pending.Values.ToArray(),ct));
            Assert.Equal(new byte[]{7},File.ReadAllBytes(Path.Combine(live,"foreign.bin")));
            Assert.Equal(new byte[]{4,6,8},File.ReadAllBytes(Path.Combine(live,"safe.bin")));
            Assert.False(File.Exists(Path.Combine(live,"earlier.bin")));
            Assert.False(File.Exists(Path.Combine(live,"risky.ps1")));
            Assert.All(originals,pair=>Assert.Equal(pair.Value,File.ReadAllBytes(pair.Key)));
        });
}
