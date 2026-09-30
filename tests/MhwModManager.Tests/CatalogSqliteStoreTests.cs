using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class CatalogSqliteStoreTests
{
    [Fact]
    public async Task Round_trip_persists_normalized_mod_cache_files_and_provenance()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new CatalogSqliteStore(Path.Combine(directory, "catalog.db"));
            await store.InitializeAsync();
            await store.UpsertSourceAsync(CreateCompliance("fixture"), "Fixture Provider");

            var fetchedAt = new DateTimeOffset(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);
            var cached = new CachedCatalogMod(
                CreateMod(
                    providerId: "fixture",
                    providerModId: "101",
                    name: "Dragon Rebalance",
                    providerMetadata: "raw-provider-payload-must-not-persist"),
                new CatalogCacheMetadata(
                    FetchedAt: fetchedAt,
                    ExpiresAt: fetchedAt.AddHours(6),
                    ETag: "\"fixture-v1\"",
                    LastModified: fetchedAt.AddMinutes(-5),
                    SourceFingerprint: "sha256:fixture"));

            await store.UpsertModAsync(cached);

            var loaded = await store.GetAsync("fixture:101");
            Assert.NotNull(loaded);
            Assert.Equal("Dragon Rebalance", loaded!.Mod.Name);
            Assert.Equal(new[] { "balance", "elder-dragon" }, loaded.Mod.Tags);
            Assert.Single(loaded.Mod.Files);
            Assert.Equal("main.zip", loaded.Mod.Files[0].FileName);
            Assert.Null(loaded.Mod.ProviderMetadata);
            Assert.Null(loaded.Mod.Files[0].ProviderMetadata);
            Assert.Equal(fetchedAt, loaded.Cache.FetchedAt);
            Assert.Equal("\"fixture-v1\"", loaded.Cache.ETag);
            Assert.Equal("sha256:fixture", loaded.Cache.SourceFingerprint);

            var provenance = await store.GetProvenanceAsync("fixture:101");
            var entry = Assert.Single(provenance);
            Assert.Equal("fixture", entry.ProviderId);
            Assert.Equal("101", entry.ProviderModId);
            Assert.Equal("https://mods.example.test/mods/101", entry.SourceUrl);
            Assert.Equal(fetchedAt, entry.FetchedAt);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Fts_search_indexes_normalized_fields_and_replacement_removes_stale_terms_and_files()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new CatalogSqliteStore(Path.Combine(directory, "catalog.db"));
            await store.InitializeAsync();
            await store.UpsertSourceAsync(CreateCompliance("fixture"), "Fixture Provider");

            var first = new CachedCatalogMod(
                CreateMod(
                    providerId: "fixture",
                    providerModId: "202",
                    name: "Ancient Dragon Balance",
                    description: "Rebalances elder dragon armor and quests.",
                    fileId: "old-file",
                    fileName: "old.zip"),
                new CatalogCacheMetadata(new DateTimeOffset(2026, 9, 29, 20, 0, 0, TimeSpan.Zero)));
            await store.UpsertModAsync(first);

            var initialSearch = await store.SearchAsync("dragon balance", gameId: "monsterhunterworld");
            Assert.Single(initialSearch);
            Assert.Equal("fixture:202", initialSearch[0].Mod.CanonicalId);

            var replacement = new CachedCatalogMod(
                CreateMod(
                    providerId: "fixture",
                    providerModId: "202",
                    name: "Weapon Polish",
                    description: "Improves weapon texture clarity.",
                    tags: new[] { "weapons", "visual" },
                    fileId: "new-file",
                    fileName: "new.zip"),
                new CatalogCacheMetadata(new DateTimeOffset(2026, 9, 29, 21, 0, 0, TimeSpan.Zero)));
            await store.UpsertModAsync(replacement);

            Assert.Empty(await store.SearchAsync("dragon"));
            var weaponSearch = await store.SearchAsync("weapon");
            var loaded = Assert.Single(weaponSearch);
            Assert.Single(loaded.Mod.Files);
            Assert.Equal("new-file", loaded.Mod.Files[0].ProviderFileId);

            var provenance = await store.GetProvenanceAsync("fixture:202");
            Assert.Equal(2, provenance.Count);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_state_round_trips_provider_cursor_and_typed_failure_without_raw_error_text()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new CatalogSqliteStore(Path.Combine(directory, "catalog.db"));
            await store.InitializeAsync();
            await store.UpsertSourceAsync(CreateCompliance("fixture"), "Fixture Provider");

            var state = new CatalogSyncState(
                ProviderId: "fixture",
                Cursor: "page:17",
                LastAttemptAt: new DateTimeOffset(2026, 9, 29, 21, 5, 0, TimeSpan.Zero),
                LastSuccessAt: new DateTimeOffset(2026, 9, 29, 20, 55, 0, TimeSpan.Zero),
                ConsecutiveFailures: 2,
                LastFailureKind: CatalogSyncFailureKind.RateLimited);

            await store.UpsertSyncStateAsync(state);
            var loaded = await store.GetSyncStateAsync("FIXTURE");

            Assert.NotNull(loaded);
            Assert.Equal("fixture", loaded!.ProviderId);
            Assert.Equal("page:17", loaded.Cursor);
            Assert.Equal(2, loaded.ConsecutiveFailures);
            Assert.Equal(CatalogSyncFailureKind.RateLimited, loaded.LastFailureKind);
            Assert.Equal(state.LastSuccessAt, loaded.LastSuccessAt);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Catalog_data_fails_closed_when_provider_source_is_not_registered()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new CatalogSqliteStore(Path.Combine(directory, "catalog.db"));
            await store.InitializeAsync();

            var cached = new CachedCatalogMod(
                CreateMod(providerId: "missing", providerModId: "303", name: "Missing Source"),
                new CatalogCacheMetadata(DateTimeOffset.UtcNow));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.UpsertModAsync(cached));

            Assert.Contains("must be registered", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static CatalogProviderCompliance CreateCompliance(string providerId)
    {
        return new CatalogProviderCompliance(
            ProviderId: providerId,
            SourceKind: CatalogSourceKind.OfficialApi,
            DocumentationUri: new Uri("https://docs.example.test/api"),
            TermsUri: new Uri("https://docs.example.test/terms"),
            TermsReviewedOn: DateOnly.FromDateTime(DateTime.UtcNow),
            ReviewIntervalDays: 30,
            AllowsCatalogDiscovery: true,
            AllowsDirectDownload: false,
            AllowsHtmlParsing: false,
            AttributionRequired: false);
    }

    private static CatalogMod CreateMod(
        string providerId,
        string providerModId,
        string name,
        string description = "Rebalances elder dragon armor and quests.",
        IReadOnlyList<string>? tags = null,
        string fileId = "main-file",
        string fileName = "main.zip",
        string? providerMetadata = null)
    {
        var file = new CatalogModFile(
            ProviderId: providerId,
            ProviderModId: providerModId,
            ProviderFileId: fileId,
            Name: "Main File",
            FileName: fileName,
            Category: CatalogFileCategory.Main,
            Version: "1.0.0",
            SizeBytes: 1234,
            Description: "Main archive",
            UploadedAt: new DateTimeOffset(2026, 9, 29, 19, 0, 0, TimeSpan.Zero),
            Required: true,
            Recommended: true,
            Dependencies: Array.Empty<CatalogDependency>(),
            ProviderMetadata: providerMetadata);

        return new CatalogMod(
            CanonicalId: CatalogMod.BuildCanonicalId(providerId, providerModId),
            ProviderId: providerId,
            ProviderModId: providerModId,
            GameId: "monsterhunterworld",
            Name: name,
            Summary: "A deterministic catalog fixture.",
            Description: description,
            Author: "Fixture Author",
            Version: "1.0.0",
            Category: "Gameplay",
            Tags: tags ?? new[] { "balance", "elder-dragon" },
            Thumbnail: "https://images.example.test/thumb.jpg",
            Screenshots: new[] { new CatalogImage("https://images.example.test/shot.jpg", "Shot") },
            CreatedAt: new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 9, 29, 19, 0, 0, TimeSpan.Zero),
            Downloads: 42,
            Endorsements: 7,
            Rating: 4.5,
            Dependencies: Array.Empty<CatalogDependency>(),
            SourceUrl: $"https://mods.example.test/mods/{providerModId}",
            Files: new[] { file },
            ProviderMetadata: providerMetadata);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mhw-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
