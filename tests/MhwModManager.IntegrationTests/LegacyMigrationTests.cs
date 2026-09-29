using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class LegacyMigrationTests : IDisposable
{
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"umm-legacy-migration-"+Guid.NewGuid().ToString("N"));

    public LegacyMigrationTests()=>Directory.CreateDirectory(root);

    public void Dispose()
    {
        try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }

    [Fact]
    public async Task Migration_repairs_corrupt_existing_CAS_object_without_retry_livelock()
    {
        var toolRoot=Path.Combine(root,"tool");
        var legacyRoot=Path.Combine(toolRoot,"State","V2");
        var legacyBlobs=Path.Combine(legacyRoot,"Blobs");
        var nextRoot=Path.Combine(toolRoot,"State","Next");
        var nextBlobs=Path.Combine(nextRoot,"Blobs");
        Directory.CreateDirectory(legacyBlobs);
        Directory.CreateDirectory(nextBlobs);

        var payload=Encoding.UTF8.GetBytes("authoritative legacy blob");
        var hash=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        await File.WriteAllBytesAsync(Path.Combine(legacyBlobs,hash),payload,TestToken);

        var state=new
        {
            schema=2,
            order=new[]{"Example Mod"},
            mods=new Dictionary<string,object?>
            {
                ["Example Mod"]=new
                {
                    files=new Dictionary<string,string>
                    {
                        [@"root\nativePC\asset.bin"]=hash
                    }
                }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(legacyRoot,"state.json"),
            JsonSerializer.Serialize(state),
            TestToken);

        var destinationBlob=Path.Combine(nextBlobs,hash);
        await File.WriteAllTextAsync(destinationBlob,"CORRUPT",TestToken);

        var db=new ManagerDatabase(Path.Combine(nextRoot,"manager.db"));
        await db.InitializeAsync(TestToken);
        var migrator=new LegacyV7Migrator(db,toolRoot,nextBlobs);

        var first=await migrator.MigrateIfNeededAsync(TestToken);

        Assert.True(first.Performed);
        Assert.True(first.Success,first.Message);
        Assert.Equal(payload,await File.ReadAllBytesAsync(destinationBlob,TestToken));
        Assert.Equal("Example Mod",Assert.Single(await db.GetModsAsync(TestToken)).Name);

        var retry=await migrator.MigrateIfNeededAsync(TestToken);
        Assert.False(retry.Performed);
        Assert.True(retry.Success);
    }
}
