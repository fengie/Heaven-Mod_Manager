using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class AutoModRecipeCatalogTests
{
    [Fact]
    public void Catalog_search_is_game_scoped_deterministic_and_exposes_adapter_readiness()
    {
        var catalog = AutoModRecipeCatalog.Build(
        [
            new("z", Recipe("z", "Zulu", adapterVersion: ">=2.0.0")),
            new("a", Recipe("a", "Alpha")),
            new("other", Recipe("other", "Alpha", game: "another-game")),
            new("missing", Recipe("missing", "Missing adapter", adapterId: "other.table"))
        ], CreateRegistry());

        var results = catalog.Search("monster-hunter-world");
        Assert.Equal(new[] { "a", "missing", "z" }, results.Select(x => x.SourceId));
        Assert.True(results[0].AdapterReady);
        Assert.Contains(results[1].AdapterIssues, issue => issue.Code == "adapter.missing");
        Assert.Contains(results[2].AdapterIssues, issue => issue.Code == "adapter.version");
        Assert.Equal("a", Assert.Single(catalog.Search("monster-hunter-world", "ALPHA")).SourceId);
        Assert.Equal("z", Assert.Single(catalog.Search("monster-hunter-world", "mhw.auto-mod.fixture.z")).SourceId);
        Assert.Equal("a", Assert.Single(catalog.Search("monster-hunter-world", includeUnavailable: false)).SourceId);
        Assert.Equal("other", Assert.Single(catalog.Search("another-game")).SourceId);
        Assert.Empty(catalog.Rejected);
    }

    [Fact]
    public void Duplicate_recipe_ids_quarantine_all_candidates_instead_of_selecting_by_source_order()
    {
        AutoModRecipeSource[] sources =
        [
            new("first", Recipe("duplicate", "First")),
            new("second", Recipe("duplicate", "Second", adapterVersion: ">=2.0.0")),
            new("unique", Recipe("unique", "Unique"))
        ];

        var first = AutoModRecipeCatalog.Build(sources, CreateRegistry());
        var reversed = AutoModRecipeCatalog.Build(sources.Reverse().ToArray(), CreateRegistry());

        Assert.Equal("unique", Assert.Single(first.Entries).SourceId);
        Assert.Equal("unique", Assert.Single(reversed.Entries).SourceId);
        Assert.Equal(new[] { "first", "second" }, first.Rejected.Select(x => x.SourceId));
        Assert.Equal(first.Rejected.Select(x => x.SourceId), reversed.Rejected.Select(x => x.SourceId));
        Assert.All(first.Rejected, entry =>
            Assert.Contains(entry.Issues, issue => issue.Code == "catalog.duplicate_id"));
    }

    [Fact]
    public void Invalid_documents_do_not_prevent_unrelated_recipes_from_being_available()
    {
        var valid = Recipe("valid", "Valid");
        var catalog = AutoModRecipeCatalog.Build(
        [
            new("valid", valid),
            new("malformed", "{"),
            new("null-step", valid.Replace("\"steps\":[", "\"steps\":[null,", StringComparison.Ordinal)),
            new("duplicate-property", valid[..^1] + ",\"id\":\"mhw.auto-mod.fixture.ambiguous\"}"),
            new("oversized", valid + new string(' ', AutoModRecipeParser.MaxRecipeUtf8Bytes)),
            new("unsafe-output", valid.Replace("nativePC/synthetic/table.json", "../escape.bin", StringComparison.Ordinal))
        ], CreateRegistry());

        Assert.True(Assert.Single(catalog.Entries).AdapterReady);
        Assert.Equal(5, catalog.Rejected.Count);
        Assert.Contains(catalog.Rejected, entry => entry.SourceId == "unsafe-output" &&
            entry.Issues.Any(issue => issue.Code == "output.path"));
        Assert.All(catalog.Rejected.Where(entry => entry.SourceId != "unsafe-output"),
            entry => Assert.Contains(entry.Issues, issue => issue.Code == "catalog.parse"));
    }

    [Theory]
    [InlineData("\"inputs\":{", "\"inputs\":{\"bad\":null,")]
    [InlineData("\"outputs\":[", "\"outputs\":[null,")]
    [InlineData("\"adapters\":[", "\"adapters\":[null,")]
    public void Null_recipe_elements_fail_during_parsing(string original, string replacement)
    {
        var json = Recipe("valid", "Valid");
        var invalid = json.Replace(original, replacement, StringComparison.Ordinal);
        Assert.NotEqual(json, invalid);
        Assert.Throws<JsonException>(() => AutoModRecipeParser.Parse(invalid));
    }

    [Fact]
    public void Source_inventory_limit_and_ambiguous_source_ids_fail_before_loading()
    {
        var sources = Enumerable.Range(0, AutoModRecipeCatalog.MaxSources + 1)
            .Select(i => new AutoModRecipeSource(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "{"))
            .ToArray();
        Assert.Throws<ArgumentException>(() => AutoModRecipeCatalog.Build(sources, CreateRegistry()));

        Assert.Throws<ArgumentException>(() => AutoModRecipeCatalog.Build(
            [new("same", Recipe("a", "A")), new("same", Recipe("b", "B"))], CreateRegistry()));
    }

    private static string Recipe(
        string id, string name, string game = "monster-hunter-world",
        string adapterId = "synthetic.table", string adapterVersion = ">=1.0.0 <2.0.0")
    {
        return JsonSerializer.Serialize(new
        {
            schema = AutoModConstants.RecipeSchemaV1,
            id = "mhw.auto-mod.fixture." + id,
            version = "1.0.0",
            name,
            game,
            requires = new { adapters = new[] { new { id = adapterId, version = adapterVersion } } },
            inputs = new { value = new { type = "integer", required = true } },
            steps = new[] { new { id = "set_value", adapter = adapterId, op = "set_field",
                source = "table", target = "1", field = "value", value = "${value}" } },
            outputs = new[] { new { source = "table", path = "nativePC/synthetic/table.json" } }
        });
    }

    private static AutoModAdapterRegistry CreateRegistry()
    {
        var registry = new AutoModAdapterRegistry();
        registry.Register(new FixtureAdapter());
        return registry;
    }

    private sealed class FixtureAdapter : IAutoModFormatAdapter
    {
        public string Id => "synthetic.table";
        public string Version => "1.2.0";
        public IReadOnlySet<AutoModOperationKind> SupportedOperations { get; } =
            new HashSet<AutoModOperationKind> { AutoModOperationKind.SetField };
    }
}
