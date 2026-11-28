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


    [Fact]
    public async Task Same_verified_archive_with_different_titles_reuses_one_game_download()
    {
        var source = Path.Combine(root, "package.zip");
        var bytes = new byte[] { 0x50, 0x4B, 3, 4, 10, 20, 30, 40, 50 };
        await File.WriteAllBytesAsync(source, bytes, TestToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var downloads = Path.Combine(root, "Mods by Game", "monsterhunterworld", "Downloads");

        var original = await GameDownloadArchiveStore.SaveAsync(
            downloads, source, "Cheeky Dress", sha, TestToken);
        var duplicate = await GameDownloadArchiveStore.SaveAsync(
            downloads, source, "Cheeky Dress Alternative Title", sha, TestToken);

        Assert.Equal(original, duplicate);
        Assert.Single(Directory.EnumerateFiles(downloads));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original, TestToken));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Concurrent_different_title_acquisitions_share_one_verified_copy()
    {
        // Every waiter starts with the same immutable bytes but a different title.
        // Separate destination names must never let simultaneous saves publish duplicates.
        var source = Path.Combine(root, "parallel.zip");
        var bytes = new byte[1024 * 1024];
        RandomNumberGenerator.Fill(bytes);
        bytes[0] = 0x50;
        bytes[1] = 0x4B;
        bytes[2] = 0x03;
        bytes[3] = 0x04;
        await File.WriteAllBytesAsync(source, bytes, TestToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var downloads = Path.Combine(root, "Mods by Game", "monsterhunterworld", "Downloads");

        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ct = TestToken;
        var attempts = Enumerable.Range(0, 16).Select(async index =>
        {
            await start.Task.WaitAsync(ct);
            return await GameDownloadArchiveStore.SaveAsync(
                downloads, source, $"Alias {index}", sha, ct);
        }).ToArray();
        start.SetResult(true);

        var paths = await Task.WhenAll(attempts);
        var kept = Assert.Single(paths.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(kept, Assert.Single(Directory.EnumerateFiles(downloads)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(kept, ct));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Same_verified_archive_in_different_games_remains_game_scoped()
    {
        var source = Path.Combine(root, "shared.zip");
        var bytes = new byte[] { 0x50, 0x4B, 3, 4, 99, 88, 77 };
        await File.WriteAllBytesAsync(source, bytes, TestToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var firstGame = Path.Combine(root, "Mods by Game", "monsterhunterworld", "Downloads");
        var secondGame = Path.Combine(root, "Mods by Game", "stellarblade", "Downloads");

        var first = await GameDownloadArchiveStore.SaveAsync(
            firstGame, source, "Shared Pack", sha, TestToken);
        var second = await GameDownloadArchiveStore.SaveAsync(
            secondGame, source, "Shared Pack", sha, TestToken);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Single(Directory.EnumerateFiles(firstGame));
        Assert.Single(Directory.EnumerateFiles(secondGame));
    }

    [Fact]
    public async Task Forged_matching_digest_filename_cannot_impersonate_a_verified_game_archive()
    {
        var source = Path.Combine(root, "valid.zip");
        var bytes = new byte[] { 0x50, 0x4B, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(source, bytes, TestToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var downloads = Path.Combine(root, "Mods by Game", "monsterhunterworld", "Downloads");
        Directory.CreateDirectory(downloads);
        var fake = Path.Combine(downloads, $"Old-Title-{sha}.zip");
        await File.WriteAllTextAsync(fake, "not the advertised hash", TestToken);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            GameDownloadArchiveStore.SaveAsync(
                downloads, source, "New Title", sha, TestToken));

        Assert.Single(Directory.EnumerateFiles(downloads));
        Assert.Equal("not the advertised hash", await File.ReadAllTextAsync(fake, TestToken));
        Assert.True(File.Exists(source));
    }

    public void Dispose()
    {
        try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Retention_refuses_a_linked_library_ancestor_before_creating_any_game_directory()
    {
        var source = Path.Combine(root, "source.zip");
        await File.WriteAllTextAsync(source, "isolated archive bytes", TestToken);
        var sha = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(source, TestToken))).ToLowerInvariant();
        var external = Directory.CreateDirectory(Path.Combine(root, "external")).FullName;
        var sentinel = Path.Combine(external, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched", TestToken);
        var link = Path.Combine(root, "library");
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{external}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not create fixture junction.");
        await process.WaitForExitAsync(TestToken);
        Assert.Equal(0, process.ExitCode);
        try
        {
            var downloads = Path.Combine(link, "game", "Downloads");
            await Assert.ThrowsAsync<IOException>(() =>
                GameDownloadArchiveStore.SaveAsync(downloads, source, "Fixture", sha, TestToken));
            Assert.False(Directory.Exists(Path.Combine(external, "game")));
            Assert.Equal("untouched", await File.ReadAllTextAsync(sentinel, TestToken));
            Assert.True(File.Exists(source));
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

}
