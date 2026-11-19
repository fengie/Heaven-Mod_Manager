using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogScaleRegressionTests : IDisposable
{
    private const int FixtureRowCount = 10_000;
    private const int BrowseCapacity = 1_000;
    private static readonly DateTimeOffset FixtureBaseTime =
        new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "mhwmm-catalog-scale-" + Guid.NewGuid().ToString("N"));

    public CatalogScaleRegressionTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Ten_thousand_row_browse_uses_game_sort_index_and_returns_full_capacity()
    {
        var db = new ManagerDatabase(Path.Combine(root, "scale.db"));
        await db.InitializeAsync(TestToken);
        var repository = new CatalogRepository(db);
        await repository.UpsertSourceAsync(
            "Nexus Mods",
            CatalogComplianceFixture.Active("nexus"),
            TestToken);

        await SeedCatalogRowsAsync(db, FixtureRowCount);

        var plan = await ReadPlainBrowseQueryPlanAsync(db);
        Assert.Contains(
            plan,
            detail => detail.Contains(
                "ix_catalog_items_game_sort",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            plan,
            detail => detail.Contains(
                "USE TEMP B-TREE FOR ORDER BY",
                StringComparison.OrdinalIgnoreCase));

        var stopwatch = Stopwatch.StartNew();
        var results = await repository.SearchAsync(
            query: null,
            gameId: "monsterhunterworld",
            includeStale: true,
            limit: BrowseCapacity,
            now: FixtureBaseTime.AddDays(1),
            ct: TestToken);
        stopwatch.Stop();

        Assert.Equal(BrowseCapacity, results.Count);
        Assert.Equal("nexus:scale-09999", results[0].Mod.CanonicalId);
        Assert.Equal("nexus:scale-09000", results[^1].Mod.CanonicalId);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"10k cached browse exceeded the generous regression ceiling: {stopwatch.Elapsed}.");
        // Keep the 1,000-row hard cap even when callers request more.
        var overRequested = await repository.SearchAsync(
            query: null,
            gameId: "monsterhunterworld",
            includeStale: true,
            limit: BrowseCapacity + 1,
            now: FixtureBaseTime.AddDays(1),
            ct: TestToken);
        Assert.Equal(BrowseCapacity, overRequested.Count);

        // Existence-only probe makes truncation visible without loading a
        // 1,001st result, widening the normal limit or touching provider APIs.
        Assert.True(await repository.HasAdditionalMatchesAsync(
            query: null, gameId: "monsterhunterworld",
            visibleLimit: BrowseCapacity, ct: TestToken));
        Assert.False(await repository.HasAdditionalMatchesAsync(
            query: null, gameId: "another-game",
            visibleLimit: BrowseCapacity, ct: TestToken));
        // Local browse paging may cross the 1,000-row hydration cap, but
        // every individual query must remain bounded. Both the first and last
        // pages must be reachable without provider requests or duplicate IDs.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var offset = 0; offset < FixtureRowCount; offset += BrowseCapacity)
        {
            var page = await repository.SearchAsync(
                query: null,
                gameId: "monsterhunterworld",
                includeStale: true,
                limit: BrowseCapacity,
                now: FixtureBaseTime.AddDays(1),
                ct: TestToken,
                offset: offset);
            Assert.Equal(BrowseCapacity, page.Count);
            Assert.Equal($"nexus:scale-{FixtureRowCount - offset - 1:D5}", page[0].Mod.CanonicalId);
            Assert.Equal($"nexus:scale-{FixtureRowCount - offset - BrowseCapacity:D5}", page[^1].Mod.CanonicalId);
            foreach (var row in page)
                Assert.True(seen.Add(row.Mod.CanonicalId), $"Duplicate paged result: {row.Mod.CanonicalId}");
        }

        Assert.Equal(FixtureRowCount, seen.Count);
        Assert.True(await repository.HasAdditionalMatchesAsync(
            query: null, gameId: "monsterhunterworld",
            visibleLimit: FixtureRowCount - 1, ct: TestToken));
        Assert.False(await repository.HasAdditionalMatchesAsync(
            query: null, gameId: "monsterhunterworld",
            visibleLimit: FixtureRowCount, ct: TestToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.HasAdditionalMatchesAsync(
                query: null, gameId: "monsterhunterworld",
                visibleLimit: -1, ct: TestToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.SearchAsync(
                query: null, gameId: "monsterhunterworld",
                offset: -1, ct: TestToken));
    }

    [Fact]
    public void Browse_mods_exposes_separate_local_cache_paging_controls()
    {
        var rootPath = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            rootPath, "src", "MhwModManager.App", "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(
            rootPath, "src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.Catalog.cs"));

        Assert.Contains("Command=\"{Binding LoadMoreCachedCatalogCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding CatalogLoadMoreCachedVisibility}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Show more cached catalog results\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private async Task LoadMoreCachedCatalog()", viewModel, StringComparison.Ordinal);
        Assert.Contains("offset: offset", viewModel, StringComparison.Ordinal);
        // Provider continuation remains a distinct, user-initiated operation.
        Assert.Contains("Command=\"{Binding LoadMoreCatalogCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("[RelayCommand]\n    private async Task LoadMoreCatalog()",
            viewModel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, "0 results")]
    [InlineData(1, false, "1 result")]
    [InlineData(7, false, "7 results")]
    [InlineData(1000, true, "cached matches")]
    public void Browse_count_distinguishes_complete_results_from_truncated_local_cache(
        int visibleCount, bool hasAdditional, string expected)
    {
        var label = CatalogBrowseResultState.FormatResultCountLabel(
            visibleCount, hasAdditional);
        Assert.Contains(expected, label, StringComparison.OrdinalIgnoreCase);
        if (hasAdditional)
        {
            Assert.Contains("first", label, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shown", label, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Ten_thousand_row_publication_uses_one_collection_reset()
    {
        var collection = new ObservableRangeCollection<int>();
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => changes.Add(args.Action);

        collection.ReplaceAll(Enumerable.Range(0, FixtureRowCount));

        Assert.Equal(FixtureRowCount, collection.Count);
        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Reset },
            changes);
    }

    [Fact]
    public void Browse_mods_results_grid_keeps_recycling_virtualization_contract()
    {
        var rootPath = FindRepositoryRoot();
        var xaml = File.ReadAllText(
            Path.Combine(rootPath, "src", "MhwModManager.App", "MainWindow.xaml"));

        const string binding = "ItemsSource=\"{Binding CatalogItems}\"";
        var start = xaml.IndexOf(binding, StringComparison.Ordinal);
        Assert.True(start >= 0, "Browse Mods catalog DataGrid binding was not found.");

        var end = xaml.IndexOf("</DataGrid>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Browse Mods catalog DataGrid closing tag was not found.");

        var catalogGrid = xaml[start..end];
        Assert.Contains("EnableRowVirtualization=\"True\"", catalogGrid, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", catalogGrid, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", catalogGrid, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", catalogGrid, StringComparison.Ordinal);
    }

    private static async Task SeedCatalogRowsAsync(ManagerDatabase db, int count)
    {
        await using var connection = await db.OpenAsync(TestToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(TestToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalog_items(
                canonical_id,provider_id,provider_mod_id,game_id,
                name,summary,description,author,
                tags_json,screenshots_json,dependencies_json,source_url,
                fetched_at,updated_at)
            VALUES(
                $canonical,$provider,$providerMod,$game,
                $name,$summary,$description,$author,
                '[]','[]','[]',$source,
                $fetched,$updated)
            """;

        var canonical = command.Parameters.Add("$canonical", SqliteType.Text);
        var provider = command.Parameters.Add("$provider", SqliteType.Text);
        var providerMod = command.Parameters.Add("$providerMod", SqliteType.Text);
        var game = command.Parameters.Add("$game", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var summary = command.Parameters.Add("$summary", SqliteType.Text);
        var description = command.Parameters.Add("$description", SqliteType.Text);
        var author = command.Parameters.Add("$author", SqliteType.Text);
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var fetched = command.Parameters.Add("$fetched", SqliteType.Text);
        var updated = command.Parameters.Add("$updated", SqliteType.Text);

        provider.Value = "nexus";
        game.Value = "monsterhunterworld";
        summary.Value = "Scale fixture";
        description.Value = "Deterministic ten-thousand-row Browse Mods regression.";
        author.Value = "Fixture Author";
        source.Value = "https://www.nexusmods.com/monsterhunterworld/mods/1";
        fetched.Value = FixtureBaseTime.ToString("O", CultureInfo.InvariantCulture);

        for (var i = 0; i < count; i++)
        {
            TestToken.ThrowIfCancellationRequested();
            canonical.Value = $"nexus:scale-{i:D5}";
            providerMod.Value = $"scale-{i:D5}";
            name.Value = $"Scale Fixture {i:D5}";
            updated.Value = FixtureBaseTime
                .AddSeconds(i)
                .ToString("O", CultureInfo.InvariantCulture);
            await command.ExecuteNonQueryAsync(TestToken);
        }

        await transaction.CommitAsync(TestToken);
    }

    private static async Task<IReadOnlyList<string>> ReadPlainBrowseQueryPlanAsync(
        ManagerDatabase db)
    {
        await using var connection = await db.OpenAsync(TestToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT canonical_id
            FROM catalog_items
            WHERE game_id=$game
            ORDER BY COALESCE(updated_at,fetched_at) DESC,name COLLATE NOCASE
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$game", "monsterhunterworld");
        command.Parameters.AddWithValue("$limit", BrowseCapacity);

        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestToken);
        while (await reader.ReadAsync(TestToken))
            details.Add(reader.GetString(3));

        return details;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root from test base directory.");
    }
}
