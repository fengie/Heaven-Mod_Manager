using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Guard against an empty library triggering a full scan/hash of a game's own asset folder
/// while the non-cancellable startup modal is visible.
/// </summary>
public sealed class EmptyLibraryStartupRegressionTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"umm-empty-startup-"+Guid.NewGuid().ToString("N"));

    public EmptyLibraryStartupRegressionTests()=>Directory.CreateDirectory(root);

    [Fact]
    public void Initial_window_load_does_not_automatically_hash_unmanaged_game_assets()
    {
        var method=ReadInitializationMethod();
        Assert.Contains("await ReloadMods(ct);",method);
        Assert.DoesNotContain("s.Adoption.CountAsync",method,StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_library_skips_conflict_planning_and_startup_errors_reach_the_owner()
    {
        var method=ReadInitializationMethod();
        Assert.Contains("if(Mods.Count>0)",method);
        Assert.Contains("await RefreshAnalysis(ct);",method);
        Assert.Contains("rethrowOnFailure:true",method);

        var vm=ReadSource("src","MhwModManager.App","ViewModels","MainWindowViewModel.cs");
        Assert.Contains("if(rethrowOnFailure)throw;",vm);
    }

    [Fact]
    public void Discovered_game_asset_Data_directory_is_not_automatically_a_mod_root()
    {
        var game=CreateGame("nier-data",dataFolder:true,modsFolder:false);
        var profile=DiscoverSingleGame(game,"nier-data-state");

        Assert.Equal("generic-root",profile.AdapterId);
        Assert.Equal(string.Empty,profile.ModRootRelativePath);
    }

    [Fact]
    public void Discovered_explicit_Mods_directory_takes_precedence_over_game_Data()
    {
        var game=CreateGame("nier-with-mods",dataFolder:true,modsFolder:true);
        var profile=DiscoverSingleGame(game,"nier-with-mods-state");

        Assert.Equal("mods-folder",profile.AdapterId);
        Assert.Equal("Mods",profile.ModRootRelativePath,StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_existing_Data_profile_is_not_silently_rewritten()
    {
        var game=CreateGame("explicit-data",dataFolder:true,modsFolder:false);
        var registry=new GameProfileRegistry(Path.Combine(root,"existing-state"),()=>
            [new GameDiscoveryCandidate("NieR:Automata",game,Path.Combine(game,"NieRAutomata.exe"),"Steam","524220")]);
        var existing=GameProfile.Generic("nier-explicit","NieR:Automata",game,"NieRAutomata.exe","data","data-folder",store:"Steam");
        registry.Upsert(existing);

        Assert.False(registry.DiscoverAndRegisterInstalledGamesDetailed().HasChanges);
        Assert.Equal(existing,Assert.Single(registry.Load()));
    }

    private GameProfile DiscoverSingleGame(string game,string state)
    {
        var registry=new GameProfileRegistry(Path.Combine(root,state),()=>
            [new GameDiscoveryCandidate("NieR:Automata",game,Path.Combine(game,"NieRAutomata.exe"),"Steam","524220")]);
        return Assert.Single(registry.DiscoverAndRegisterInstalledGamesDetailed().Added);
    }

    private string CreateGame(string name,bool dataFolder,bool modsFolder)
    {
        var game=Path.Combine(root,name);
        Directory.CreateDirectory(game);
        File.WriteAllBytes(Path.Combine(game,"NieRAutomata.exe"),[0x4d,0x5a]);
        if(dataFolder)Directory.CreateDirectory(Path.Combine(game,"data"));
        if(modsFolder)Directory.CreateDirectory(Path.Combine(game,"Mods"));
        return game;
    }

    private static string ReadInitializationMethod()
    {
        var source=ReadSource("src","MhwModManager.App","ViewModels","MainWindowViewModel.cs");
        var start=source.IndexOf("public async Task InitializeAsync()",StringComparison.Ordinal);
        Assert.True(start>=0,"Startup initialization entrypoint was not found.");
        var end=source.IndexOf("partial void OnSelectedTabChanged",start,StringComparison.Ordinal);
        Assert.True(end>start,"Startup initialization boundary was not found.");
        return source[start..end];
    }

    private static string ReadSource(params string[] parts)
    {
        DirectoryInfo? dir=new(AppContext.BaseDirectory);
        while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"MhwModManager.sln")))dir=dir.Parent;
        if(dir is null)throw new DirectoryNotFoundException("Repository root not found.");
        return File.ReadAllText(Path.Combine(dir.FullName,Path.Combine(parts)));
    }

    public void Dispose()
    {
        try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        GC.SuppressFinalize(this);
    }
}
