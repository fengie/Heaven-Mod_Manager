using System.Security.Cryptography;
using MhwModManager.Automation;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class GameDownloadArchiveStoreTests : IDisposable
{
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"umm-archive-retain-"+Guid.NewGuid().ToString("N"));

    public GameDownloadArchiveStoreTests()=>Directory.CreateDirectory(root);

    [Fact]
    public async Task Verified_archive_is_saved_into_one_game_folder_with_readable_identity_and_digest()
    {
        var archive=Path.Combine(root,"catalog-ephemeral.zip");
        var contents=new byte[]{80,75,3,4,0,1,2,3,4,5};
        await File.WriteAllBytesAsync(archive,contents,TestToken);
        var sha=Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
        var downloads=Path.Combine(root,"Mods by Game","nier-automata","Downloads");

        var saved=await GameDownloadArchiveStore.SaveAsync(downloads,archive,"My NieR Mod",sha,TestToken);
        var savedAgain=await GameDownloadArchiveStore.SaveAsync(downloads,archive,"My NieR Mod",sha,TestToken);

        Assert.Equal(saved,savedAgain);
        Assert.Equal(downloads,Path.GetDirectoryName(saved));
        Assert.Contains("My-NieR-Mod",Path.GetFileName(saved),StringComparison.Ordinal);
        Assert.Contains(sha,Path.GetFileName(saved),StringComparison.Ordinal);
        Assert.Equal(contents,await File.ReadAllBytesAsync(saved,TestToken));
        Assert.Single(Directory.EnumerateFiles(downloads));
        Assert.True(File.Exists(archive),"User/source archive must never be moved or deleted by the keeper.");
    }

    [Fact]
    public async Task Incorrect_digest_never_publishes_or_overwrites_an_archive()
    {
        var archive=Path.Combine(root,"wrong.zip");
        await File.WriteAllTextAsync(archive,"original",TestToken);
        var downloads=Path.Combine(root,"Mods by Game","test","Downloads");

        await Assert.ThrowsAsync<InvalidDataException>(()=>
            GameDownloadArchiveStore.SaveAsync(downloads,archive,"Test",new string('0',64),TestToken));

        Assert.Empty(Directory.EnumerateFiles(downloads));
        Assert.Equal("original",await File.ReadAllTextAsync(archive,TestToken));
    }

    [Fact]
    public async Task Existing_modified_destination_fails_closed_instead_of_relabeling_it_verified()
    {
        var archive=Path.Combine(root,"unchanged.7z");
        var contents=new byte[]{0x37,0x7a,0xbc,0xaf,0x27,0x1c};
        await File.WriteAllBytesAsync(archive,contents,TestToken);
        var sha=Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
        var downloads=Path.Combine(root,"Mods by Game","test","Downloads");

        var saved=await GameDownloadArchiveStore.SaveAsync(downloads,archive,"Mod",sha,TestToken);
        await File.WriteAllTextAsync(saved,"tampered",TestToken);

        await Assert.ThrowsAsync<InvalidDataException>(()=>
            GameDownloadArchiveStore.SaveAsync(downloads,archive,"Mod",sha,TestToken));
        Assert.Equal("tampered",await File.ReadAllTextAsync(saved,TestToken));
    }

    public void Dispose()
    {
        try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        GC.SuppressFinalize(this);
    }
}
