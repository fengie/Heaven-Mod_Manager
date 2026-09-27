using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class VisualMetadataTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"mhwmm-visuals-"+Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    public VisualMetadataTests()=>Directory.CreateDirectory(root);
    public void Dispose(){try{Directory.Delete(root,true);}catch{}}

    [Fact]
    public async Task Gallery_combines_persisted_preview_and_local_package_images()
    {
        var db=new ManagerDatabase(Path.Combine(root,"state","manager.db"));await db.InitializeAsync(TestToken);
        var modRoot=Path.Combine(root,"mod");Directory.CreateDirectory(Path.Combine(modRoot,"screens"));
        var explicitPreview=Path.Combine(modRoot,"preview.jpg");
        var extra=Path.Combine(modRoot,"screens","author-shot.png");
        await File.WriteAllBytesAsync(explicitPreview,new byte[9000],TestToken);
        await File.WriteAllBytesAsync(extra,new byte[10000],TestToken);
        var mod=new ModDescriptor("m","m","Visual Mod",modRoot,true,10,PreviewPath:explicitPreview);
        await db.SetSettingAsync("visuals:m",System.Text.Json.JsonSerializer.Serialize(new[]{explicitPreview}),TestToken);

        var gallery=await new ModVisualService(db).GetGalleryAsync([mod],16,TestToken);

        Assert.Equal(explicitPreview,gallery[0]);
        Assert.Contains(extra,gallery);
        Assert.Equal(gallery.Count,gallery.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Settings_prefix_lookup_is_exact_to_prefix()
    {
        var db=new ManagerDatabase(Path.Combine(root,"state2","manager.db"));await db.InitializeAsync(TestToken);
        await db.SetSettingAsync("update:a","2026-09-26T00:00:00Z",TestToken);
        await db.SetSettingAsync("preview:a","x.jpg",TestToken);
        var values=await db.GetSettingsByPrefixAsync("update:",TestToken);
        Assert.Single(values);
        Assert.Equal("2026-09-26T00:00:00Z",values["update:a"]);
    }
    [Fact]
    public async Task Gallery_recognizes_fomod_author_image_hint()
    {
        var db=new ManagerDatabase(Path.Combine(root,"state3","manager.db"));await db.InitializeAsync(TestToken);
        var modRoot=Path.Combine(root,"fomod-mod");var fomod=Path.Combine(modRoot,"fomod");var images=Path.Combine(fomod,"images");
        Directory.CreateDirectory(images);
        var image=Path.Combine(images,"cover.jpg");await File.WriteAllBytesAsync(image,new byte[9000],TestToken);
        await File.WriteAllTextAsync(Path.Combine(fomod,"info.xml"),"<fomod><Image>images/cover.jpg</Image></fomod>",TestToken);
        var mod=new ModDescriptor("f","f","FOMOD Visual",modRoot,true,1);

        var gallery=await new ModVisualService(db).GetGalleryAsync([mod],16,TestToken);

        Assert.Contains(image,gallery);
    }

}
