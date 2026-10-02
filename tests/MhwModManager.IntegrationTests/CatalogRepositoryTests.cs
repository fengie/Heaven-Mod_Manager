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
        Assert.Null(loaded.Mod.ProviderMetadata);
        Assert.Null(actualFile.ProviderMetadata);
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

    [Fact]
    public async Task Fts_search_covers_title_author_summary_tags_category_and_description()
    {
        var repository = await CreateRepositoryAsync("fts-fields");
        var cached = CreateCached(
            canonicalId: "nexus:fts-fields",
            name: "Crimson Dragon Blade",
            summary: "A sharp dragon greatsword.",
            description: "Detailed silver weapon texture.",
            expiresAt: null,
            providerModId: "fts-fields");
        await repository.UpsertAsync(cached, TestToken);

        foreach (var query in new[] { "Crimson", "Author", "sharp", "greatsword", "Weapons", "silver" })
        {
            var result = await repository.SearchAsync(query, ct: TestToken);
            var hit = Assert.Single(result);
            Assert.Equal(cached.Mod.CanonicalId, hit.Mod.CanonicalId);
        }
    }

    [Fact]
    public async Task Cache_does_not_persist_raw_provider_payloads_or_expiring_urls()
    {
        var repository = await CreateRepositoryAsync("privacy");
        var baseCached = CreateCached(
            canonicalId: "nexus:privacy-fixture",
            name: "Privacy Fixture",
            summary: "Cache privacy",
            description: "Signed URLs must not persist.",
            expiresAt: null,
            providerModId: "privacy-fixture");
        var originalFile = Assert.Single(baseCached.Mod.Files);
        var cached = baseCached with
        {
            Mod = baseCached.Mod with
            {
                Thumbnail = "https://cdn.example.test/thumb.jpg?signature=secret&expires=123",
                Screenshots =
                [
                    new("https://cdn.example.test/public.jpg", "public"),
                    new("https://cdn.example.test/private.jpg?token=secret", "private")
                ],
                Dependencies =
                [
                    new("Public dep", Url: "https://example.test/public"),
                    new("Signed dep", Url: "https://example.test/private?x-amz-signature=secret")
                ],
                ProviderMetadata = """{"token":"must-not-persist"}""",
                Files =
                [
                    originalFile with
                    {
                        Dependencies = [new("Signed file dep", Url: "https://example.test/file?api_key=secret")],
                        ProviderMetadata = """{"token":"must-not-persist"}"""
                    }
                ]
            }
        };

        await repository.UpsertAsync(cached, TestToken);
        var loaded = await repository.GetAsync(cached.Mod.CanonicalId, TestToken);
        Assert.NotNull(loaded);
        Assert.Null(loaded.Mod.ProviderMetadata);
        Assert.Null(loaded.Mod.Thumbnail);
        var image = Assert.Single(loaded.Mod.Screenshots);
        Assert.Equal("public", image.Caption);
        Assert.Equal("https://example.test/public", loaded.Mod.Dependencies[0].Url);
        Assert.Null(loaded.Mod.Dependencies[1].Url);
        var storedFile = Assert.Single(loaded.Mod.Files);
        Assert.Null(storedFile.ProviderMetadata);
        Assert.Null(Assert.Single(storedFile.Dependencies!).Url);

        var signedSource = baseCached with
        {
            Mod = baseCached.Mod with
            {
                CanonicalId = "nexus:signed-source-fixture",
                ProviderModId = "signed-source-fixture",
                SourceUrl = "https://mods.example.test/1?access_token=secret",
                Files = []
            }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAsync(signedSource, TestToken));
    }

    [Fact]
    public async Task Sync_rate_link_and_provenance_state_round_trip()
    {
        var repository = await CreateRepositoryAsync("state");
        var db = new ManagerDatabase(Path.Combine(root, "state", "manager.db"));
        var first = CreateCached(
            "nexus:state-a",
            "State A",
            "First",
            "First item",
            null,
            providerModId: "state-a");
        var second = CreateCached(
            "nexus:state-b",
            "State B",
            "Second",
            "Second item",
            null,
            providerModId: "state-b");
        await repository.UpsertAsync(first, TestToken);
        await repository.UpsertAsync(second, TestToken);

        var now = new DateTimeOffset(2026, 9, 29, 22, 30, 0, TimeSpan.FromHours(-4));
        var sync = new CatalogSyncState(
            "nexus",
            "monsterhunterworld:updated",
            "cursor-2",
            now,
            now,
            CatalogSyncFailureKind.RateLimited);
        await repository.UpsertSyncStateAsync(sync, TestToken);
        Assert.Equal(sync, await repository.GetSyncStateAsync("nexus", sync.ScopeKey, TestToken));

        await using (var c = await db.OpenAsync(TestToken))
        {
            await using var failure = c.CreateCommand();
            failure.CommandText = "SELECT last_failure_kind FROM catalog_sync_state WHERE provider_id='nexus' AND scope_key='monsterhunterworld:updated'";
            Assert.Equal("RateLimited", (string?)await failure.ExecuteScalarAsync(TestToken));

            await using var columns = c.CreateCommand();
            columns.CommandText = "PRAGMA table_info(catalog_sync_state)";
            await using var reader = await columns.ExecuteReaderAsync(TestToken);
            var columnNames = new List<string>();
            while (await reader.ReadAsync(TestToken))
                columnNames.Add(reader.GetString(1));
            Assert.DoesNotContain("last_error", columnNames, StringComparer.OrdinalIgnoreCase);
        }

        var rate = new CatalogRateState(
            "nexus",
            "api",
            new CatalogRateLimit(100, 12, 1000, 500, now.AddMinutes(3), now));
        await repository.UpsertRateStateAsync(rate, TestToken);
        Assert.Equal(rate, await repository.GetRateStateAsync("nexus", "api", TestToken));

        var link = new CatalogLink(
            first.Mod.CanonicalId,
            second.Mod.CanonicalId,
            CatalogLinkEvidenceKind.ExactArchiveSha256,
            new string('a', 64),
            now);
        await repository.UpsertLinkAsync(link, TestToken);
        var storedLink = Assert.Single(await repository.GetLinksAsync(first.Mod.CanonicalId, TestToken));
        Assert.Equal(link.EvidenceKind, storedLink.EvidenceKind);
        Assert.Equal(link.EvidenceValue, storedLink.EvidenceValue);
        Assert.Equal(TimeSpan.Zero, storedLink.ObservedAt.Offset);
        Assert.Equal(link.ObservedAt, storedLink.ObservedAt);

        var provenance = await repository.GetProvenanceAsync(first.Mod.CanonicalId, TestToken);
        Assert.NotNull(provenance);
        Assert.Equal(first.Mod.SourceUrl, provenance.SourceUrl);
        Assert.Equal(first.Cache.SourceFingerprint, provenance.SourceFingerprint);
    }

    [Fact]
    public async Task Search_can_return_more_than_500_rows_up_to_browse_capacity()
    {
        var repository = await CreateRepositoryAsync("scale-capacity");
        for (var i = 0; i < 600; i++)
        {
            await repository.UpsertAsync(
                CreateCached(
                    canonicalId: $"nexus:scale-{i:D4}",
                    name: $"Scale Fixture {i:D4}",
                    summary: "Scale search fixture",
                    description: "Deterministic catalog capacity regression.",
                    expiresAt: null,
                    providerModId: $"scale-{i:D4}"),
                TestToken);
        }

        var results = await repository.SearchAsync(
            "Scale",
            gameId: "monsterhunterworld",
            limit: 1000,
            ct: TestToken);

        Assert.Equal(600, results.Count);
    }

    [Fact]
    public async Task Same_named_items_from_different_sources_remain_distinct()
    {
        var repository = await CreateRepositoryAsync("source-separation");
        var secondCompliance = NexusV3CatalogPolicy.Compliance with { ProviderId = "fixture-source-b" };
        await repository.UpsertSourceAsync("Fixture Source B", secondCompliance, TestToken);

        var first = CreateCached(
            "nexus:same-name",
            "Same Name",
            "First source",
            "First",
            null,
            providerModId: "same-name-a");
        var second = CreateCached(
            "fixture-source-b:same-name",
            "Same Name",
            "Second source",
            "Second",
            null,
            providerId: "fixture-source-b",
            providerModId: "same-name-b");
        await repository.UpsertAsync(first, TestToken);
        await repository.UpsertAsync(second, TestToken);

        var results = await repository.SearchAsync("Same", gameId: "monsterhunterworld", ct: TestToken);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, item => item.Mod.ProviderId == "nexus");
        Assert.Contains(results, item => item.Mod.ProviderId == "fixture-source-b");
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
        DateTimeOffset? expiresAt,
        string providerId = "nexus",
        string providerModId = "global-fixture")
    {
        var fetched = new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero);
        var dependency = new CatalogDependency("Stracker's Loader", Url: "https://example.test/dependency");
        var file = new CatalogModFile(
            providerId,
            providerModId,
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
            providerId,
            providerModId,
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
