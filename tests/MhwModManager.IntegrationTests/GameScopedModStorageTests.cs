using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class GameScopedModStorageTests : IDisposable
{
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string tool=Path.Combine(Path.GetTempPath(),"umm-library-layout-"+Guid.NewGuid().ToString("N"));

    public GameScopedModStorageTests()=>Directory.CreateDirectory(tool);

    [Fact]
    public void Different_games_get_disjoint_installed_download_inbox_and_archive_folders()
    {
        var mhw=GameModStorage.Resolve(tool,GameProfile.MonsterHunterWorld(Path.Combine(tool,"MhwGame")));
        var nier=GameModStorage.Resolve(tool,GameProfile.Generic("nier-automata","NieR:Automata",Path.Combine(tool,"NierGame"),"NieRAutomata.exe",""));

        Assert.Equal(Path.Combine(tool,"Mods by Game","nier-automata","Installed"),nier.InstalledRoot);
        Assert.Equal(Path.Combine(tool,"Mods by Game","nier-automata","Downloads"),nier.DownloadsRoot);
        Assert.Equal(Path.Combine(tool,"Mods by Game","nier-automata","Inbox"),nier.InboxRoot);
        Assert.Equal(Path.Combine(tool,"Mods by Game","nier-automata","Archive"),nier.ArchiveRoot);
        Assert.NotEqual(mhw.InstalledRoot,nier.InstalledRoot);
        Assert.NotEqual(mhw.DownloadsRoot,nier.DownloadsRoot);
        Assert.NotEqual(mhw.InboxRoot,nier.InboxRoot);
        Assert.NotEqual(mhw.ArchiveRoot,nier.ArchiveRoot);
    }

    [Fact]
    public void Legacy_installed_and_inbox_locations_are_kept_for_each_game()
    {
        var mhw=GameModStorage.Resolve(tool,GameProfile.MonsterHunterWorld(Path.Combine(tool,"MhwGame")));
        var nier=GameModStorage.Resolve(tool,GameProfile.Generic("nier-automata","NieR:Automata",Path.Combine(tool,"NierGame"),"NieRAutomata.exe",""));

        Assert.Equal(Path.Combine(tool,"Mods"),mhw.LegacyInstalledRoot);
        Assert.Equal(Path.Combine(tool,"Inbox"),mhw.LegacyInboxRoot);
        Assert.Equal(Path.Combine(tool,"Games","nier-automata","Mods"),nier.LegacyInstalledRoot);
        Assert.Equal(Path.Combine(tool,"Games","nier-automata","Inbox"),nier.LegacyInboxRoot);
        Assert.False(Directory.Exists(nier.InstalledRoot),"Resolving paths must not create or move user files.");
    }

    [Fact]
    public void Dashboard_exposes_the_selected_games_installed_and_download_folders()
    {
        var current=new DirectoryInfo(AppContext.BaseDirectory);
        while(current is not null&&!File.Exists(Path.Combine(current.FullName,"MhwModManager.sln")))current=current.Parent;
        Assert.NotNull(current);
        var xaml=File.ReadAllText(Path.Combine(current!.FullName,"src","MhwModManager.App","MainWindow.xaml"));
        var viewModel=File.ReadAllText(Path.Combine(current.FullName,"src","MhwModManager.App","ViewModels","MainWindowViewModel.cs"));

        Assert.Contains("OpenGameModFolderCommand",xaml);
        Assert.Contains("OpenGameDownloadsFolderCommand",xaml);
        Assert.Contains("ModLibraryPathText",xaml);
        Assert.Contains("GameDownloadsPathText",xaml);
        Assert.Contains("private void OpenGameModFolder()",viewModel);
        Assert.Contains("private void OpenGameDownloadsFolder()",viewModel);
    }

    [Fact]
    public void Invalid_game_identity_cannot_escape_the_scoped_mod_library()
    {
        var invalid=GameProfile.Generic("valid-id","Name",Path.Combine(tool,"Game"),"Game.exe","")
            with{Id=@"..\other-game"};
        Assert.Throws<InvalidDataException>(()=>GameModStorage.Resolve(tool,invalid));
        Assert.False(Directory.Exists(Path.Combine(tool,"other-game")));
    }

    [Fact]
    public async Task Catalog_indexes_both_new_installs_and_old_packages_without_moving_them()
    {
        var game=GameProfile.Generic("nie-r","NieR",Path.Combine(tool,"NierGame"),"NieRAutomata.exe","");
        var storage=GameModStorage.Resolve(tool,game);
        Directory.CreateDirectory(Path.Combine(storage.InstalledRoot,"NewPackage"));
        Directory.CreateDirectory(Path.Combine(storage.LegacyInstalledRoot,"ExistingPackage"));
        Directory.CreateDirectory(Path.Combine(storage.DownloadsRoot,"not-a-mod"));
        var db=new ManagerDatabase(Path.Combine(tool,"state","manager.db"));
        await db.InitializeAsync(TestToken);
        var scanner=new ModScanner(db,new BlobStore(Path.Combine(tool,"blobs"),db),new HashingService(),game);
        var catalog=new CatalogService(db,scanner,storage.InstalledRoot,storage.LegacyInstalledRoot);

        await catalog.RefreshFoldersAsync(TestToken);

        var mods=await db.GetModsAsync(TestToken);
        Assert.Equal(2,mods.Count);
        Assert.Contains(mods,mod=>mod.SourcePath==Path.Combine(storage.InstalledRoot,"NewPackage"));
        Assert.Contains(mods,mod=>mod.SourcePath==Path.Combine(storage.LegacyInstalledRoot,"ExistingPackage"));
        Assert.DoesNotContain(mods,mod=>mod.SourcePath.Contains("Downloads",StringComparison.OrdinalIgnoreCase));
        Assert.True(Directory.Exists(Path.Combine(storage.LegacyInstalledRoot,"ExistingPackage")));
    }

    public void Dispose()
    {
        try{Directory.Delete(tool,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        GC.SuppressFinalize(this);
    }
}
