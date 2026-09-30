using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogCacheStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-catalog-cache-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public CatalogCacheStorageTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Source_aware_catalog_cache_round_trips_mod_files_cache_and_provenance()
    {
        var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var mod = CreateMod(
            "provider-a",
            "42",
            "fixture-game",
            "Dragon Texture Pack",
            files:
            [
                new(
                    "provider-a",
                    "42",
                    "file-main",
                    "Main archive",
                    "dragon.zip",
                    CatalogFileCategory.Main,
                    Version: "2.1",
                    SizeBytes: 12345,
                    Description: "Main payload",
                    UploadedAt: now.AddMinutes(-20),
                    Required: true)
            ]);
        var cached = new CachedCatalogMod(
            mod,
            new(now.AddMinutes(-5), now.AddHours(1), ""etag-a"", now.AddMinutes(-5), "sha256:fixture"));

        await database.UpsertCachedCatalogModAsync(cached, "Provider A", "OfficialApi", TestToken);

        var stored = await database.GetCachedCatalogModAsync("provider-a", "42", TestToken);
        Assert.NotNull(stored);
        Assert.Equal(mod.CanonicalId, stored.Mod.CanonicalId);
        Assert.Equal("Dragon Texture Pack", stored.Mod.Name);
        Assert.Equal(""etag-a"", stored.Cache.ETag);
        Assert.Equal("sha256:fixture", stored.Cache.SourceFingerprint);
        var storedFile = Assert.Single(stored.Mod.Files);
        Assert.Equal("file-main", storedFile.ProviderFileId);
        Assert.Equal(CatalogFileCategory.Main, storedFile.Category);
        Assert.Null(stored.Mod.ProviderMetadata);
        Assert.Null(storedFile.ProviderMetadata);

        var provenance = await database.GetCatalogProvenanceAsync("provider-a", "42", TestToken);
        Assert.NotNull(provenance);
        Assert.Equal(mod.CanonicalId, provenance.CanonicalId);
        Assert.Equal(mod.SourceUrl, provenance.SourceUrl);
        Assert.Equal("sha256:fixture", provenance.SourceFingerprint);
    }

    [Fact]
    public async Task Fts5_search_covers_title_author_summary_tags_category_and_description()
    {
        var database = await CreateDatabaseAsync();
        var mod = CreateMod(
            "provider-a",
            "99",
            "fixture-game",
            "Moonlit Armor",
            author: "Ada Smith",
            summary: "High quality recolor",
            description: "Detailed silver texture replacement",
            category: "Visuals",
            tags: ["armor", "fashion"]);
        await database.UpsertCachedCatalogModAsync(
            new(mod, new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1))),
            "Provider A",
            "OfficialApi",
            TestToken);

        foreach (var query in new[] { "Moonlit", "Ada", "quality", "fashion", "Visuals", "silver" })
        {
            var results = await database.SearchCachedCatalogModsAsync(
                new("fixture-game", query, Limit: 10),
                TestToken);
            var found = Assert.Single(results);
            Assert.Equal(mod.CanonicalId, found.Mod.CanonicalId);
        }
    }

    [Fact]
    public async Task Upsert_replaces_old_files_and_fts_terms_and_stale_filter_is_explicit()
    {
        var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var oldMod = CreateMod(
            "provider-a",
            "7",
            "fixture-game",
            "Alpha Legacy",
            files:
            [
                new("provider-a", "7", "old-file", "Old", "old.zip", CatalogFileCategory.OldVersion)
            ]);
        await database.UpsertCachedCatalogModAsync(
            new(oldMod, new(now.AddMinutes(-10), now.AddMinutes(-1))),
            "Provider A",
            "OfficialApi",
            TestToken);

        var newMod = CreateMod(
            "provider-a",
            "7",
            "fixture-game",
            "Omega Current",
            files:
            [
                new("provider-a", "7", "new-file", "New", "new.zip", CatalogFileCategory.Main)
            ]);
        await database.UpsertCachedCatalogModAsync(
            new(newMod, new(now, now.AddMinutes(-1))),
            "Provider A",
            "OfficialApi",
            TestToken);

        Assert.Empty(await database.SearchCachedCatalogModsAsync(new("fixture-game", "Alpha"), TestToken));
        var omega = Assert.Single(await database.SearchCachedCatalogModsAsync(new("fixture-game", "Omega"), TestToken));
        Assert.Equal("new-file", Assert.Single(omega.Mod.Files).ProviderFileId);

        var staleVisible = await database.SearchCachedCatalogModsAsync(
            new("fixture-game", "Omega", IncludeStale: true),
            TestToken);
        Assert.Single(staleVisible);

        var staleHidden = await database.SearchCachedCatalogModsAsync(
            new("fixture-game", "Omega", IncludeStale: false),
            TestToken);
        Assert.Empty(staleHidden);
    }

    [Fact]
    public async Task Catalog_keeps_same_named_items_from_different_providers_separate()
    {
        var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var provider in new[] { "provider-a", "provider-b" })
        {
            var mod = CreateMod(provider, "1", "fixture-game", "Same Name");
            await database.UpsertCachedCatalogModAsync(
                new(mod, new(now, now.AddHours(1))),
                provider,
                "OfficialApi",
                TestToken);
        }

        var results = await database.SearchCachedCatalogModsAsync(
            new("fixture-game", "Same", Limit: 10),
            TestToken);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.Mod.ProviderId == "provider-a");
        Assert.Contains(results, result => result.Mod.ProviderId == "provider-b");
    }

    [Fact]
    public async Task Sync_rate_and_strong_evidence_link_state_round_trip()
    {
        var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var left = CreateMod("provider-a", "11", "fixture-game", "Left");
        var right = CreateMod("provider-b", "22", "fixture-game", "Right");
        await database.UpsertCachedCatalogModAsync(
            new(left, new(now, now.AddHours(1))),
            "Provider A",
            "OfficialApi",
            TestToken);
        await database.UpsertCachedCatalogModAsync(
            new(right, new(now, now.AddHours(1))),
            "Provider B",
            "OfficialApi",
            TestToken);

        var sync = new CatalogSyncState(
            "provider-a",
            "fixture-game:recent",
            "cursor-2",
            now.AddMinutes(-2),
            now,
            null,
            ""sync-etag"",
            now.AddMinutes(-5));
        await database.UpsertCatalogSyncStateAsync(sync, TestToken);
        Assert.Equal(sync, await database.GetCatalogSyncStateAsync(sync.ProviderId, sync.ScopeKey, TestToken));

        var health = new CatalogProviderHealth(
            "provider-a",
            CatalogProviderState.RateLimited,
            "Retry later",
            new(100, 0, 1000, 900, now.AddMinutes(3), now),
            now);
        await database.UpsertCatalogRateStateAsync(health, TestToken);
        var storedHealth = await database.GetCatalogRateStateAsync("provider-a", TestToken);
        Assert.NotNull(storedHealth);
        Assert.Equal(CatalogProviderState.RateLimited, storedHealth.State);
        Assert.Equal(0, storedHealth.RateLimit?.HourlyRemaining);
        Assert.Equal(health.RateLimit?.RetryAfter, storedHealth.RateLimit?.RetryAfter);

        var link = new CatalogLink(
            left.CanonicalId,
            right.CanonicalId,
            CatalogLinkEvidenceKind.ExactArchiveSha256,
            new string('a', 64),
            now);
        await database.UpsertCatalogLinkAsync(link, TestToken);
        var links = await database.GetCatalogLinksAsync(left.CanonicalId, TestToken);
        var storedLink = Assert.Single(links);
        Assert.Equal(CatalogLinkEvidenceKind.ExactArchiveSha256, storedLink.EvidenceKind);
        Assert.Equal(link.EvidenceValue, storedLink.EvidenceValue);
    }

    [Fact]
    public async Task Cache_refuses_signed_source_urls_and_drops_signed_image_urls()
    {
        var database = await CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var signedSource = CreateMod(
            "provider-a",
            "55",
            "fixture-game",
            "Signed Source") with
        {
            SourceUrl = "https://mods.example.test/55?token=secret"
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            database.UpsertCachedCatalogModAsync(
                new(signedSource, new(now, now.AddHours(1))),
                "Provider A",
                "OfficialApi",
                TestToken));

        var signedImage = CreateMod(
            "provider-a",
            "56",
            "fixture-game",
            "Signed Image") with
        {
            Thumbnail = "https://cdn.example.test/thumb.jpg?signature=secret",
            Screenshots =
            [
                new("https://cdn.example.test/keep.jpg", "public"),
                new("https://cdn.example.test/drop.jpg?expires=123&sig=secret", "signed")
            ]
        };
        await database.UpsertCachedCatalogModAsync(
            new(signedImage, new(now, now.AddHours(1))),
            "Provider A",
            "OfficialApi",
            TestToken);

        var stored = await database.GetCachedCatalogModAsync("provider-a", "56", TestToken);
        Assert.NotNull(stored);
        Assert.Null(stored.Mod.Thumbnail);
        var screenshot = Assert.Single(stored.Mod.Screenshots);
        Assert.Equal("public", screenshot.Caption);
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync()
    {
        var database = new ManagerDatabase(Path.Combine(root, Guid.NewGuid().ToString("N"), "manager.db"));
        await database.InitializeAsync(TestToken);
        return database;
    }

    private static CatalogMod CreateMod(
        string providerId,
        string providerModId,
        string gameId,
        string name,
        string author = "Fixture Author",
        string summary = "Fixture summary",
        string description = "Fixture description",
        string? category = "Gameplay",
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<CatalogModFile>? files = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new(
            CatalogMod.BuildCanonicalId(providerId, providerModId),
            providerId,
            providerModId,
            gameId,
            name,
            summary,
            description,
            author,
            "1.0",
            category,
            tags ?? ["fixture"],
            "https://cdn.example.test/thumb.jpg",
            [new("https://cdn.example.test/shot.jpg", "fixture")],
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            100,
            10,
            null,
            [],
            $"https://mods.example.test/{providerModId}",
            files ?? [],
            """{"not":"persisted"}""");
    }
}
