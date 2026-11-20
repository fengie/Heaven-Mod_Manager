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
        File.WriteAllText(Path.Combine(folder, "example.mrl3"), "fixture material");
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
        foreach (var name in new[] { first, second })
        {
            var folder = Path.Combine(root, "Installed", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "example.mrl3"), "fixture material");
        }
        var (db, catalog) = await CreateCatalogAsync();
        await catalog.RefreshFoldersAsync(TestToken);
        var mods = await db.GetModsAsync(TestToken);
        Assert.Equal(2, mods.Count);
        Assert.All(mods, mod => Assert.Null(mod.NexusModId));
        Assert.Equal(2, LogicalModFamilies.Build(mods).Count);
    }

    [Fact]
    public async Task Refresh_skips_new_empty_folders_without_removing_them_and_registers_them_when_populated()
    {
        var folder = Path.Combine(root, "Installed", "apply");
        Directory.CreateDirectory(folder);
        var (db, catalog) = await CreateCatalogAsync();

        await catalog.RefreshFoldersAsync(TestToken);

        Assert.Empty(await db.GetModsAsync(TestToken));
        Assert.True(Directory.Exists(folder));

        File.WriteAllText(Path.Combine(folder, "example.mrl3"), "fixture material");
        await catalog.RefreshFoldersAsync(TestToken);
        Assert.Equal(folder, Assert.Single(await db.GetModsAsync(TestToken)).SourcePath);
    }

    [Fact]
    public async Task Persisted_empty_folder_preserves_saved_state_and_blocks_the_capture_batch_before_indexing()
    {
        var empty = Path.Combine(root, "Installed", "apply");
        var populated = Path.Combine(root, "Installed", "valid");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(populated);
        File.WriteAllText(Path.Combine(populated, "example.mrl3"), "fixture material");
        var (db, catalog) = await CreateCatalogAsync();
        await db.UpsertModAsync(new("existing", "apply", "Existing custom name", empty, true, 17, FamilyId: "manual:keep"), TestToken);
        await catalog.RefreshFoldersAsync(TestToken);
        var mods = await db.GetModsAsync(TestToken);
        var saved = Assert.Single(mods, m => m.Id == "existing");
        var valid = Assert.Single(mods, m => m.SourcePath == populated);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.EnsureCapturedAsync([valid, saved], TestToken));
        Assert.Contains("Existing custom name", error.Message, StringComparison.Ordinal);
        Assert.Contains("Deselect", error.Message, StringComparison.Ordinal);
        Assert.Empty(await db.GetModFilesAsync(TestToken));
        Assert.True(Directory.Exists(empty));

        File.WriteAllText(Path.Combine(empty, "example.mrl3"), "restored fixture material");
        await catalog.RefreshFoldersAsync(TestToken);
        await catalog.EnsureCapturedAsync([valid, saved], TestToken);
        var current = Assert.Single(await db.GetModsAsync(TestToken), m => m.Id == saved.Id);
        Assert.True(current.Enabled);
        Assert.Equal(17, current.Priority);
        Assert.Equal("manual:keep", current.FamilyId);
        Assert.Equal("Existing custom name", current.DisplayName);
        Assert.Equal(2, (await db.GetModFilesAsync(TestToken)).Count);
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
