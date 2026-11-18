using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogNexusArchiveBackfillTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-nexus-backfill-" + Guid.NewGuid().ToString("N"));

    public CatalogNexusArchiveBackfillTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task Modern_download_folder_has_readable_title_without_asserting_unverified_source_identity()
    {
        const string name = "tattoo-skin-texture-overlay-8397-1-0-1763815951-500733aabbeeccdd00112233445566778899aabbccddeeff0011223344556677";
        var folder = Path.Combine(root, "Installed", name);
        Directory.CreateDirectory(folder);
        var (db, catalog) = await CreateCatalogAsync();

        await catalog.RefreshFoldersAsync(TestToken);

        var mod = Assert.Single(await db.GetModsAsync(TestToken));
        Assert.Null(mod.NexusModId);
        Assert.Equal("tattoo-skin-texture-overlay", mod.DisplayName);
        Assert.Equal(folder, mod.SourcePath);
    }

    [Fact]
    public async Task Existing_package_is_backfilled_without_resetting_user_settings_or_source()
    {
        const string name = "tattoo-skin-texture-overlay-8411-1-0-1766850951-54ed6e10c979c084eb88664a3b639870e0b5f2e2a25ea270bb2f3fc042c22";
        var folder = Path.Combine(root, "Installed", name);
        Directory.CreateDirectory(folder);
        var (db, catalog) = await CreateCatalogAsync();
        await db.UpsertModAsync(new("existing", name, name, folder, true, 17, FamilyId: "manual:keep"), TestToken);

        await catalog.RefreshFoldersAsync(TestToken);
        await catalog.RefreshFoldersAsync(TestToken);

        var mod = Assert.Single(await db.GetModsAsync(TestToken));
        Assert.Equal("existing", mod.Id);
        Assert.True(mod.Enabled);
        Assert.Equal(17, mod.Priority);
        Assert.Equal("manual:keep", mod.FamilyId);
        Assert.Null(mod.NexusModId);
        Assert.Equal("tattoo-skin-texture-overlay", mod.DisplayName);
        Assert.Equal(folder, mod.SourcePath);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Backfill_never_replaces_manual_display_source_or_existing_nexus_identity()
    {
        const string first = "my-overlay-8397-1-0-1763815951-500733aabbeeccdd00112233445566778899aabbccddeeff0011223344556677";
        const string second = "other-overlay-8411-1-0-1766850951-54ed6e10c979c084eb88664a3b639870e0b5f2e2a25ea270bb2f3fc042c22";
        var a = Path.Combine(root, "Installed", first);
        var b = Path.Combine(root, "Installed", second);
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        var (db, catalog) = await CreateCatalogAsync();
        await db.UpsertModAsync(new("a", first, "My hand-edited name", a, false, 4), TestToken);
        await db.UpsertModAsync(new("b", second, "Another source", b, true, 5, SourceUrl: "https://example.org/not-nexus"), TestToken);
        await catalog.RefreshFoldersAsync(TestToken);

        var mods = (await db.GetModsAsync(TestToken)).ToDictionary(m => m.Id);
        Assert.Null(mods["a"].NexusModId);
        Assert.Equal("My hand-edited name", mods["a"].DisplayName);
        Assert.Null(mods["b"].NexusModId);
        Assert.Equal("Another source", mods["b"].DisplayName);
        Assert.Equal("https://example.org/not-nexus", mods["b"].SourceUrl);
    }

    [Fact]
    public async Task A_filename_inferred_source_page_cannot_force_a_family_for_two_unrelated_packages()
    {
        const string first = "Alpha-8397-1-0-1763815951-500733aabbeeccdd00112233445566778899aabbccddeeff0011223344556677";
        const string second = "Beta-8397-1-0-1763815951-54ed6e10c979c084eb88664a3b639870e0b5f2e2a25ea270bb2f3fc042c22";
        Directory.CreateDirectory(Path.Combine(root, "Installed", first));
        Directory.CreateDirectory(Path.Combine(root, "Installed", second));
        var (db, catalog) = await CreateCatalogAsync();
        await catalog.RefreshFoldersAsync(TestToken);
        var mods = await db.GetModsAsync(TestToken);
        Assert.Equal(2, mods.Count);
        Assert.All(mods, mod => Assert.Null(mod.NexusModId));
        Assert.Equal(2, LogicalModFamilies.Build(mods).Count);
    }

    private async Task<(ManagerDatabase Db, CatalogService Catalog)> CreateCatalogAsync()
    {
        var db = new ManagerDatabase(Path.Combine(root, "state", "manager.db"));
        await db.InitializeAsync(TestToken);
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root, "game"));
        var scanner = new ModScanner(db, new BlobStore(Path.Combine(root, "blobs"), db), new HashingService(), game);
        return (db, new CatalogService(db, scanner, Path.Combine(root, "Installed")));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
