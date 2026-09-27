using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class MultiGameTests : IDisposable
{
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
