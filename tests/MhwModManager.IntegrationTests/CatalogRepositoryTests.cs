using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogRepositoryTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-catalog-" + Guid.NewGuid().ToString("N"));

    public CatalogRepositoryTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Persists_normalized_item_files_provenance_and_cache_metadata()
    {
        var repository = await CreateRepositoryAsync("persist");
        var cached = CreateCached(
            canonicalId: "nexus:global-fixture-1",
            name: "Crimson Dragon Blade",
            summary: "A sharp dragon greatsword.",
            description: "Detailed weapon description.",
            expiresAt: new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.Zero));

        await repository.UpsertAsync(cached, TestToken);

        var loaded = await repository.GetAsync(cached.Mod.CanonicalId, TestToken);
        Assert.NotNull(loaded);
        Assert.Equal(cached.Mod.CanonicalId, loaded!.Mod.CanonicalId);
        Assert.Equal(cached.Mod.Name, loaded.Mod.Name);
        Assert.Equal(cached.Mod.Tags.ToArray(), loaded.Mod.Tags.ToArray());
        Assert.Equal(cached.Mod.Screenshots.ToArray(), loaded.Mod.Screenshots.ToArray());
        Assert.Equal(cached.Mod.Dependencies.ToArray(), loaded.Mod.Dependencies.ToArray());
        var expectedFile = Assert.Single(cached.Mod.Files);
        var actualFile = Assert.Single(loaded.Mod.Files);
        Assert.Equal(expectedFile.ProviderId, actualFile.ProviderId);
        Assert.Equal(expectedFile.ProviderModId, actualFile.ProviderModId);
        Assert.Equal(expectedFile.ProviderFileId, actualFile.ProviderFileId);
        Assert.Equal(expectedFile.Name, actualFile.Name);
        Assert.Equal(expectedFile.FileName, actualFile.FileName);
        Assert.Equal(expectedFile.Category, actualFile.Category);
        Assert.Equal(expectedFile.Dependencies!.ToArray(), actualFile.Dependencies!.ToArray());
        Assert.Equal(cached.Cache, loaded.Cache);

        var db = new ManagerDatabase(Path.Combine(root, "persist", "manager.db"));
        await using var c = await db.OpenAsync(TestToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM catalog_items),
                (SELECT COUNT(*) FROM catalog_files),
                (SELECT COUNT(*) FROM catalog_provenance),
                (SELECT COUNT(*) FROM catalog_items_fts)
            """;
        await using var reader = await cmd.ExecuteReaderAsync(TestToken);
        Assert.True(await reader.ReadAsync(TestToken));
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
    }

    [Fact]
    public async Task Fts_search_reindexes_updates_without_leaving_stale_terms()
    {
        var repository = await CreateRepositoryAsync("reindex");
        var first = CreateCached(
            canonicalId: "nexus:global-fixture-2",
            name: "Crimson Dragon Blade",
            summary: "Dragon weapon",
            description: "Dragon-focused build.",
            expiresAt: null);

        await repository.UpsertAsync(first, TestToken);
        Assert.Single(await repository.SearchAsync("dragon", ct: TestToken));

        var updated = CreateCached(
            canonicalId: first.Mod.CanonicalId,
            name: "Azure Wyvern Blade",
            summary: "Wyvern weapon",
            description: "A completely renamed build.",
            expiresAt: null);
        await repository.UpsertAsync(updated, TestToken);

        Assert.Empty(await repository.SearchAsync("dragon", ct: TestToken));
        var results = await repository.SearchAsync("wyvern", ct: TestToken);
        var hit = Assert.Single(results);
        Assert.Equal("Azure Wyvern Blade", hit.Mod.Name);
    }

    [Fact]
    public async Task Stale_cache_remains_browseable_but_can_be_excluded()
    {
        var repository = await CreateRepositoryAsync("stale");
        var now = new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero);
        var cached = CreateCached(
            canonicalId: "nexus:global-fixture-3",
            name: "Offline Wyvern Cache",
            summary: "Cached while provider is unavailable.",
            description: "Stale-while-revalidate fixture.",
            expiresAt: now.AddMinutes(-5));

        await repository.UpsertAsync(cached, TestToken);

        Assert.Single(await repository.SearchAsync("wyvern", includeStale: true, now: now, ct: TestToken));
        Assert.Empty(await repository.SearchAsync("wyvern", includeStale: false, now: now, ct: TestToken));
        Assert.True((await repository.GetAsync(cached.Mod.CanonicalId, TestToken))!.IsStale(now));
    }

    [Fact]
    public async Task Freshness_filter_compares_instants_not_original_offsets()
    {
        var repository = await CreateRepositoryAsync("offsets");
        var now = new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero);
        var sameInstantLaterOffset = new DateTimeOffset(2026, 9, 30, 1, 30, 0, TimeSpan.FromHours(-3));
        await repository.UpsertAsync(
            CreateCached(
                canonicalId: "nexus:global-fixture-offset",
                name: "Offset Wyvern Cache",
                summary: "UTC normalization fixture.",
                description: "Should still be fresh by instant.",
                expiresAt: sameInstantLaterOffset),
            TestToken);

        Assert.Single(await repository.SearchAsync("wyvern", includeStale: false, now: now, ct: TestToken));
    }

    [Fact]
    public async Task Search_literalizes_fts_operator_like_input()
    {
        var repository = await CreateRepositoryAsync("literal");
        await repository.UpsertAsync(
            CreateCached(
                canonicalId: "nexus:global-fixture-4",
                name: "Dragon Utility",
                summary: "Safe query fixture.",
                description: "Literal token matching.",
                expiresAt: null),
            TestToken);

        var results = await repository.SearchAsync("dragon OR", ct: TestToken);
        Assert.Empty(results);
    }

    private async Task<CatalogRepository> CreateRepositoryAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name, "manager.db"));
        await db.InitializeAsync(TestToken);
        var repository = new CatalogRepository(db);
        await repository.UpsertSourceAsync("Nexus Mods", NexusV3CatalogPolicy.Compliance, TestToken);
        return repository;
    }

    private static CachedCatalogMod CreateCached(
        string canonicalId,
        string name,
        string summary,
        string description,
        DateTimeOffset? expiresAt)
    {
        var fetched = new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero);
        var dependency = new CatalogDependency("Stracker's Loader", Url: "https://example.test/dependency");
        var file = new CatalogModFile(
            "nexus",
            "global-fixture",
            "file-1",
            "Main File",
            "fixture.zip",
            CatalogFileCategory.Main,
            "1.0.0",
            12345,
            "Primary archive",
            fetched.AddMinutes(-30),
            Required: true,
            Recommended: true,
            Dependencies: [dependency],
            ProviderMetadata: "{\"fixture\":true}");

        var mod = new CatalogMod(
            canonicalId,
            "nexus",
            "global-fixture",
            "monsterhunterworld",
            name,
            summary,
            description,
            "Fixture Author",
            "1.0.0",
            "Weapons",
            ["greatsword", "fixture"],
            "https://example.test/thumb.png",
            [new CatalogImage("https://example.test/image.png", "Fixture image")],
            fetched.AddDays(-10),
            fetched.AddMinutes(-10),
            1000,
            100,
            4.5,
            [dependency],
            "https://www.nexusmods.com/monsterhunterworld/mods/1",
            [file],
            "{\"provider\":\"nexus\"}");

        return new CachedCatalogMod(
            mod,
            new CatalogCacheMetadata(
                fetched,
                expiresAt,
                "\"fixture-etag\"",
                fetched.AddMinutes(-1),
                "sha256:fixture"));
    }
}
