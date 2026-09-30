using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogCacheRepositoryTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-catalog-cache-" + Guid.NewGuid().ToString("N"));

    public CatalogCacheRepositoryTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    [Fact]
    public async Task Upsert_round_trips_mod_files_and_provenance()
    {
        var repository = await CreateRepositoryAsync("roundtrip");
        var fetched = DateTimeOffset.Parse("2026-09-29T20:00:00Z", CultureInfo.InvariantCulture);
        var cached = new CachedCatalogMod(
            CreateMod("nexus", "global-101", "monsterhunterworld", "Crimson Blade", ["weapon", "longsword"]),
            new CatalogCacheMetadata(
                fetched,
                fetched.AddHours(6),
                ""fixture-etag"",
                fetched.AddMinutes(-5),
                "sha256:fixture"));

        await repository.UpsertAsync(cached, TestToken);
        var actual = await repository.GetAsync(cached.Mod.CanonicalId, TestToken);

        Assert.NotNull(actual);
        Assert.Equal("Crimson Blade", actual.Mod.Name);
        Assert.Equal("global-101", actual.Mod.ProviderModId);
        Assert.Equal(2, actual.Mod.Files.Count);
        Assert.Equal(""fixture-etag"", actual.Cache.ETag);
        Assert.Equal("sha256:fixture", actual.Cache.SourceFingerprint);
        Assert.False(actual.IsStale(fetched.AddHours(1)));
        Assert.True(actual.IsStale(fetched.AddHours(7)));
    }

    [Fact]
    public async Task Fts_search_matches_title_author_summary_tags_and_filters_source()
    {
        var repository = await CreateRepositoryAsync("fts");
        var fetched = DateTimeOffset.Parse("2026-09-29T20:00:00Z", CultureInfo.InvariantCulture);

        await repository.UpsertAsync(
            new CachedCatalogMod(
                CreateMod("nexus", "m1", "monsterhunterworld", "Crimson Blade", ["weapon", "longsword"], "Aki", "Fast red weapon"),
                new CatalogCacheMetadata(fetched)),
            TestToken);
        await repository.UpsertAsync(
            new CachedCatalogMod(
                CreateMod("gamebanana", "m2", "monsterhunterworld", "Azure Armor", ["armor"], "Bao", "Blue armor set"),
                new CatalogCacheMetadata(fetched)),
            TestToken);

        Assert.Single(await repository.SearchAsync("Crimson", ct: TestToken));
        Assert.Single(await repository.SearchAsync("Aki", ct: TestToken));
        Assert.Single(await repository.SearchAsync("longsword", ct: TestToken));
        Assert.Empty(await repository.SearchAsync("Crimson", providerId: "gamebanana", ct: TestToken));
        Assert.Single(await repository.SearchAsync("armor", providerId: "gamebanana", gameId: "monsterhunterworld", ct: TestToken));
    }

    [Fact]
    public async Task Reupsert_replaces_file_rows_instead_of_leaving_stale_variants()
    {
        var db = await CreateDatabaseAsync("files");
        var repository = new CatalogCacheRepository(db);
        var fetched = DateTimeOffset.Parse("2026-09-29T20:00:00Z", CultureInfo.InvariantCulture);
        var original = CreateMod("nexus", "m1", "monsterhunterworld", "Fixture", ["test"]);

        await repository.UpsertAsync(new CachedCatalogMod(original, new CatalogCacheMetadata(fetched)), TestToken);
        await repository.UpsertAsync(
            new CachedCatalogMod(
                original with { Files = [original.Files[0]] },
                new CatalogCacheMetadata(fetched.AddMinutes(1))),
            TestToken);

        await using var c = await db.OpenAsync(TestToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM catalog_files WHERE canonical_id=$id";
        cmd.Parameters.AddWithValue("$id", original.CanonicalId);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync(TestToken))!);
    }

    [Fact]
    public async Task Sync_state_persists_cursor_success_and_failure_context()
    {
        var repository = await CreateRepositoryAsync("sync");
        var attempt = DateTimeOffset.Parse("2026-09-29T20:30:00Z", CultureInfo.InvariantCulture);
        var state = new CatalogSyncState(
            "nexus",
            "monsterhunterworld",
            "cursor-42",
            attempt.AddMinutes(-10),
            attempt,
            "rate limited");

        await repository.SetSyncStateAsync(state, TestToken);
        var actual = await repository.GetSyncStateAsync("nexus", "monsterhunterworld", TestToken);

        Assert.Equal(state, actual);
    }

    private async Task<CatalogCacheRepository> CreateRepositoryAsync(string name)
        => new(await CreateDatabaseAsync(name));

    private async Task<ManagerDatabase> CreateDatabaseAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name, "manager.db"));
        await db.InitializeAsync(TestToken);
        return db;
    }

    private static CatalogMod CreateMod(
        string providerId,
        string providerModId,
        string gameId,
        string name,
        IReadOnlyList<string> tags,
        string author = "Fixture Author",
        string summary = "Fixture summary")
    {
        var canonicalId = CatalogMod.BuildCanonicalId(providerId, providerModId);
        var files = new CatalogModFile[]
        {
            new(providerId, providerModId, "file-main", "Main", "main.zip", CatalogFileCategory.Main, "1.0"),
            new(providerId, providerModId, "file-opt", "Optional", "optional.zip", CatalogFileCategory.Optional, "1.0")
        };
        return new CatalogMod(
            canonicalId,
            providerId,
            providerModId,
            gameId,
            name,
            summary,
            summary + " description",
            author,
            "1.0",
            "Gameplay",
            tags,
            null,
            Array.Empty<CatalogImage>(),
            DateTimeOffset.Parse("2026-09-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-09-29T19:00:00Z", CultureInfo.InvariantCulture),
            100,
            20,
            4.5,
            Array.Empty<CatalogDependency>(),
            $"https://example.test/{providerId}/{providerModId}",
            files);
    }
}
