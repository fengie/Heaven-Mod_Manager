using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// A game may support several mod loader roots concurrently. An archive's
/// game-relative layout must not be nested inside one unrelated preferred root.
/// </summary>
public sealed class GenericMultiLoaderPackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "umm-multiloader-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unreal_package_is_not_nested_inside_the_configured_BepInEx_plugin_root()
    {
        var files = await CaptureAsync(@"BepInEx\plugins",
            [@"BepInEx\plugins", @"Content\Paks"],
            ["Content/Paks/~mods/costume.pak"]);

        Assert.Equal(@"root\Content\Paks\~mods\costume.pak", Assert.Single(files).Path);
    }

    [Fact]
    public async Task BepInEx_package_is_not_nested_inside_the_configured_Unreal_Paks_root()
    {
        var files = await CaptureAsync(@"Content\Paks",
            [@"Content\Paks", @"BepInEx\plugins"],
            ["BepInEx/plugins/character.dll"]);

        Assert.Equal(@"root\BepInEx\plugins\character.dll", Assert.Single(files).Path);
    }

    [Fact]
    public async Task Unsupported_loader_root_is_rejected_instead_of_silently_misplaced()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CaptureAsync(
            @"BepInEx\plugins",
            [@"BepInEx\plugins"],
            ["Content/Paks/~mods/costume.pak"]));
    }

    [Fact]
    public async Task Additional_unmapped_payload_requires_review_instead_of_being_discarded()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CaptureAsync(
            @"BepInEx\plugins",
            [@"BepInEx\plugins", @"Content\Paks"],
            ["Content/Paks/~mods/costume.pak", "OtherLoader/plugin.dll"]));
    }

    [Fact]
    public async Task Two_installed_loader_roots_can_be_captured_in_one_package()
    {
        var files = await CaptureAsync(@"BepInEx\plugins",
            [@"BepInEx\plugins", @"Content\Paks"],
            ["BepInEx/plugins/character.dll", "Content/Paks/~mods/costume.pak"]);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Path == @"root\BepInEx\plugins\character.dll");
        Assert.Contains(files, f => f.Path == @"root\Content\Paks\~mods\costume.pak");
    }

    private async Task<IReadOnlyList<ModFileDescriptor>> CaptureAsync(
        string configuredRoot, string[] availableLoaderRoots, string[] packagePaths)
    {
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(folder, "game");
        var source = Path.Combine(folder, "archive-content");
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "Game.exe"), "executable fixture", Ct);
        foreach (var loader in availableLoaderRoots)
            Directory.CreateDirectory(Path.Combine(gameRoot,
                loader.Replace('\\', Path.DirectorySeparatorChar)));
        foreach (var relative in packagePaths)
        {
            var path = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "synthetic fixture", Ct);
        }

        var game = GameProfile.Generic("dual-loader", "Dual Loader", gameRoot,
            "Game.exe", configuredRoot);
        var db = new ManagerDatabase(Path.Combine(folder, "state", "manager.db"));
        await db.InitializeAsync(Ct);
        var mod = new ModDescriptor("mod", "Imported", "Imported", source, false, 0);
        await db.UpsertModAsync(mod, Ct);
        var blobStore = new BlobStore(Path.Combine(folder, "state", "blobs"), db);
        var scanner = new ModScanner(db, blobStore, new HashingService(), game);
        return await scanner.CaptureAsync(mod, Ct);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
