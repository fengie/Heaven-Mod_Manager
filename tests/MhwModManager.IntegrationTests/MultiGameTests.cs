using System.Text.Json;
using MhwModManager.App;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class MultiGameTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"umm-multigame-"+Guid.NewGuid().ToString("N"));
    public MultiGameTests()=>Directory.CreateDirectory(root);
    public void Dispose(){try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}

    [Fact]
    public void Registry_can_add_and_activate_arbitrary_windows_game()
    {
        var game=Path.Combine(root,"game");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"Example.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        Directory.CreateDirectory(Path.Combine(game,"Mods"));
        var registry=new GameProfileRegistry(Path.Combine(root,"state"));
        var profile=registry.AddGenericFromExecutable(exe);
        Assert.Equal("Example",profile.DisplayName);
        Assert.Equal("Mods",profile.ModRootRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal(profile.Id,registry.GetActive()!.Id);
    }

    [Theory]
    [InlineData(@"..\\..\\escaped")]
    [InlineData(@"bad\\child")]
    [InlineData("bad/child")]
    [InlineData("Not Canonical")]
    [InlineData("UPPERCASE")]
    public void Registry_rejects_noncanonical_persisted_ids_without_rewriting_registry(string id)
    {
        var game=Path.Combine(root,"registry-game");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"Game.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        var profile=GameProfile.Generic("safe-game","Safe Game",game,"Game.exe","") with { Id=id };
        var state=Path.Combine(root,"registry-state");Directory.CreateDirectory(state);
        var registry=new GameProfileRegistry(state);
        var json=JsonSerializer.Serialize(new[]{profile},JsonOptions);
        File.WriteAllText(registry.RegistryPath,json);

        Assert.Empty(registry.Load());
        Assert.Equal(json,File.ReadAllText(registry.RegistryPath));
    }

    [Fact]
    public void App_paths_ignore_malicious_active_profile_before_creating_workspace_paths()
    {
        var tool=Path.Combine(root,"tool");var state=Path.Combine(tool,"State");Directory.CreateDirectory(state);
        var game=Path.Combine(root,"app-game");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"Game.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        var safe=GameProfile.Generic("safe-game","Safe Game",game,"Game.exe","");
        var malicious=safe with { Id=@"..\\..\\escaped-workspace", DisplayName="Malicious" };
        var registry=new GameProfileRegistry(state);
        var json=JsonSerializer.Serialize(new[]{malicious,safe},JsonOptions);
        File.WriteAllText(registry.RegistryPath,json);File.WriteAllText(Path.Combine(state,"active-game.txt"),malicious.Id);
        var escapedWorkspace=Path.Combine(root,"escaped-workspace");Directory.CreateDirectory(escapedWorkspace);
        var sentinel=Path.Combine(escapedWorkspace,"sentinel.txt");File.WriteAllText(sentinel,"keep");
        var oldHome=Environment.GetEnvironmentVariable("MOD_MANAGER_HOME");
        var oldLegacyHome=Environment.GetEnvironmentVariable("MHW_MANAGER_HOME");
        try
        {
            Environment.SetEnvironmentVariable("MOD_MANAGER_HOME",tool);Environment.SetEnvironmentVariable("MHW_MANAGER_HOME",null);
            var paths=AppPaths.Discover();
            Assert.Equal(safe.Id,paths.Game.Id);
            Assert.False(Directory.Exists(Path.Combine(escapedWorkspace,"Mods")));
            Assert.False(Directory.Exists(Path.Combine(tool,"escaped-workspace","Next")));
            Assert.Equal("keep",File.ReadAllText(sentinel));
            Assert.Equal(json,File.ReadAllText(registry.RegistryPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOD_MANAGER_HOME",oldHome);Environment.SetEnvironmentVariable("MHW_MANAGER_HOME",oldLegacyHome);
        }
    }

    [Fact]
    public async Task Generic_scanner_maps_bare_package_into_configured_mod_root()
    {
        var game=Path.Combine(root,"game2");Directory.CreateDirectory(Path.Combine(game,"Mods"));
        var exe=Path.Combine(game,"Game.exe");await File.WriteAllBytesAsync(exe,[0x4d,0x5a],TestToken);
        var profile=GameProfile.Generic("g","G",game,"Game.exe","Mods");
        var state=Path.Combine(root,"state2");var db=new ManagerDatabase(Path.Combine(state,"manager.db"));await db.InitializeAsync(TestToken);
        var hashing=new HashingService();var blobs=new BlobStore(Path.Combine(state,"blobs"),db);var scanner=new ModScanner(db,blobs,hashing,profile);
        var source=Path.Combine(root,"package");Directory.CreateDirectory(source);await File.WriteAllTextAsync(Path.Combine(source,"hello.pak"),"x",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        var files=await scanner.CaptureAsync(mod,TestToken);
        Assert.Equal(@"root\Mods\hello.pak",Assert.Single(files).Path);
    }

    [Fact]
    public async Task Generic_scanner_preserves_archive_that_already_contains_target_folder()
    {
        var game=Path.Combine(root,"game3");Directory.CreateDirectory(Path.Combine(game,"Mods"));
        var exe=Path.Combine(game,"Game.exe");await File.WriteAllBytesAsync(exe,[0x4d,0x5a],TestToken);
        var profile=GameProfile.Generic("g","G",game,"Game.exe","Mods");
        var state=Path.Combine(root,"state3");var db=new ManagerDatabase(Path.Combine(state,"manager.db"));await db.InitializeAsync(TestToken);
        var scanner=new ModScanner(db,new BlobStore(Path.Combine(state,"blobs"),db),new HashingService(),profile);
        var source=Path.Combine(root,"package3");Directory.CreateDirectory(Path.Combine(source,"Mods","Foo"));await File.WriteAllTextAsync(Path.Combine(source,"Mods","Foo","x.dll"),"x",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        var files=await scanner.CaptureAsync(mod,TestToken);
        Assert.Equal(@"root\Mods\Foo\x.dll",Assert.Single(files).Path);
    }

    [Theory]
    [InlineData("", @"root\nativePC\asset.bin")]
    [InlineData("Mods", @"root\Mods\nativePC\asset.bin")]
    public async Task Generic_scanner_does_not_drop_MHW_named_folders(string target, string expected)
    {
        var profile=GameProfile.Generic("g","G",Path.Combine(root,"game"),"Game.exe",target);
        var db=new ManagerDatabase(Path.Combine(root,"state","manager.db"));await db.InitializeAsync(TestToken);
        var scanner=new ModScanner(db,new BlobStore(Path.Combine(root,"blobs"),db),new HashingService(),profile);
        var source=Path.Combine(root,"package");Directory.CreateDirectory(Path.Combine(source,"nativePC"));
        await File.WriteAllTextAsync(Path.Combine(source,"nativePC","asset.bin"),"asset",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        Assert.Equal(expected,Assert.Single(await scanner.CaptureAsync(mod,TestToken)).Path);
    }

    [Fact]
    public async Task Rescan_recaptures_a_missing_cached_blob()
    {
        var profile=GameProfile.Generic("g","G",Path.Combine(root,"game"),"Game.exe","");
        var db=new ManagerDatabase(Path.Combine(root,"state","manager.db"));await db.InitializeAsync(TestToken);
        var blobs=new BlobStore(Path.Combine(root,"blobs"),db);
        var scanner=new ModScanner(db,blobs,new HashingService(),profile);
        var source=Path.Combine(root,"package");Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source,"asset.bin"),"unchanged bytes",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        var first=Assert.Single(await scanner.CaptureAsync(mod,TestToken));
        File.Delete(blobs.PathFor(first.BlobSha256));
        var second=Assert.Single(await scanner.CaptureAsync(mod,TestToken));
        Assert.Equal(first.BlobSha256,second.BlobSha256);
        Assert.Equal("unchanged bytes",await File.ReadAllTextAsync(blobs.PathFor(second.BlobSha256),TestToken));
    }
}
