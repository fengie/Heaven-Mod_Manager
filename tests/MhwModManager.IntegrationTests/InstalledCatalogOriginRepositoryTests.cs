using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class InstalledCatalogOriginRepositoryTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "mhwmm-installed-origin-" + Guid.NewGuid().ToString("N"));

    public InstalledCatalogOriginRepositoryTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Exact_origin_round_trips_with_normalized_hash_and_utc_time()
    {
        var (db, repository) = await CreateAsync("roundtrip");
        await SeedModAsync(db, "mod-a");

        var downloadedAt = new DateTimeOffset(
            2026, 9, 29, 23, 30, 0, TimeSpan.FromHours(-4));
        var origin = new InstalledCatalogOrigin(
            "mod-a",
            "nexus",
            "provider-mod-123",
            "provider-file-456",
            "1.2.3",
            downloadedAt,
            "https://www.nexusmods.com/monsterhunterworld/mods/123",
            new string('A', 64),
            """{"lineage":"stable-id"}""");

        await repository.UpsertAsync(origin, TestToken);
        var loaded = await repository.GetAsync("mod-a", TestToken);

        Assert.NotNull(loaded);
        Assert.Equal("mod-a", loaded!.ModId);
        Assert.Equal("nexus", loaded.ProviderId);
        Assert.Equal("provider-mod-123", loaded.ProviderModId);
        Assert.Equal("provider-file-456", loaded.ProviderFileId);
        Assert.Equal("1.2.3", loaded.InstalledVersion);
        Assert.Equal(downloadedAt.ToUniversalTime(), loaded.DownloadedAt);
        Assert.Equal(new string('a', 64), loaded.ArchiveSha256);
        Assert.Equal("""{"lineage":"stable-id"}""", loaded.ProviderMetadata);
    }

    [Fact]
    public async Task Upsert_replaces_origin_for_same_local_mod_without_duplicate_rows()
    {
        var (db, repository) = await CreateAsync("replace");
        await SeedModAsync(db, "mod-a");

        await repository.UpsertAsync(CreateOrigin("mod-a", "file-old", "1.0.0"), TestToken);
        await repository.UpsertAsync(CreateOrigin("mod-a", "file-new", "2.0.0"), TestToken);

        var all = await repository.GetAllAsync(TestToken);
        var loaded = Assert.Single(all);
        Assert.Equal("file-new", loaded.ProviderFileId);
        Assert.Equal("2.0.0", loaded.InstalledVersion);
    }

    [Fact]
    public async Task Origin_is_deleted_when_local_mod_is_deleted()
    {
        var (db, repository) = await CreateAsync("cascade");
        await SeedModAsync(db, "mod-a");
        await repository.UpsertAsync(CreateOrigin("mod-a", "file-1", "1.0.0"), TestToken);

        await using (var c = await db.OpenAsync(TestToken))
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM mods WHERE id='mod-a'";
            await cmd.ExecuteNonQueryAsync(TestToken);
        }

        Assert.Null(await repository.GetAsync("mod-a", TestToken));
    }

    [Fact]
    public async Task Persistence_rejects_signed_source_urls_secret_metadata_and_bad_hashes()
    {
        var (db, repository) = await CreateAsync("reject");
        await SeedModAsync(db, "mod-a");

        var signedUrl = CreateOrigin("mod-a", "file-1", "1.0.0") with
        {
            SourceUrl = "https://example.test/mod?access_token=secret"
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.UpsertAsync(signedUrl, TestToken));

        var secretMetadata = CreateOrigin("mod-a", "file-1", "1.0.0") with
        {
            ProviderMetadata = """{"token":"must-not-persist"}"""
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.UpsertAsync(secretMetadata, TestToken));

        var badHash = CreateOrigin("mod-a", "file-1", "1.0.0") with
        {
            ArchiveSha256 = "not-a-sha256"
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.UpsertAsync(badHash, TestToken));

        Assert.Empty(await repository.GetAllAsync(TestToken));
    }

    private async Task<(ManagerDatabase Db, InstalledCatalogOriginRepository Repository)> CreateAsync(
        string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name, "manager.db"));
        await db.InitializeAsync(TestToken);
        return (db, new InstalledCatalogOriginRepository(db));
    }

    private static async Task SeedModAsync(ManagerDatabase db, string modId)
    {
        await using var c = await db.OpenAsync(TestToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mods(
                id,name,display_name,source_path,enabled,priority,imported_at,updated_at)
            VALUES(
                $id,$name,$display,$path,0,0,$imported,$updated)
            """;
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        cmd.Parameters.AddWithValue("$id", modId);
        cmd.Parameters.AddWithValue("$name", modId);
        cmd.Parameters.AddWithValue("$display", modId);
        cmd.Parameters.AddWithValue("$path", Path.Combine(Path.GetTempPath(), modId, Guid.NewGuid().ToString("N")));
        cmd.Parameters.AddWithValue("$imported", now);
        cmd.Parameters.AddWithValue("$updated", now);
        await cmd.ExecuteNonQueryAsync(TestToken);
    }

    private static InstalledCatalogOrigin CreateOrigin(
        string modId,
        string providerFileId,
        string? version)
    {
        return new InstalledCatalogOrigin(
            modId,
            "fixture-provider",
            "provider-mod-1",
            providerFileId,
            version,
            new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.Zero),
            "https://example.test/mods/provider-mod-1",
            new string('b', 64));
    }
}
