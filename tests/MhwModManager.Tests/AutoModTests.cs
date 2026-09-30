using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class AutoModTests
{
    private const string RecipeJson = """
    {
      "schema": "mhw-auto-mod-recipe/v1",
      "id": "mhw.auto-mod.builtin.synthetic-table",
      "version": "1.0.0",
      "name": "Synthetic Table Edit",
      "game": "monster-hunter-world",
      "requires": {
        "adapters": [
          { "id": "synthetic.table", "version": ">=1.0.0 <2.0.0" }
        ]
      },
      "inputs": {
        "record": {
          "type": "entity",
          "label": "Record",
          "required": true,
          "catalog": "synthetic.records"
        },
        "defense": {
          "type": "integer",
          "label": "Defense",
          "required": true,
          "min": 0,
          "max": 9999
        }
      },
      "steps": [
        {
          "id": "select_record",
          "adapter": "synthetic.table",
          "op": "select_record",
          "source": "table",
          "target": "${record.id}"
        },
        {
          "id": "set_defense",
          "adapter": "synthetic.table",
          "op": "set_field",
          "source": "table",
          "target": "${record.id}",
          "field": "defense",
          "value": "${defense}"
        }
      ],
      "outputs": [
        { "source": "table", "path": "nativePC/synthetic/table.json" }
      ]
    }
    """;

    [Fact]
    public void Public_recipe_fixture_parses_with_production_options()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-table.recipe.json");
        var recipe = AutoModRecipeParser.Parse(File.ReadAllText(path));

        Assert.Equal(AutoModConstants.RecipeSchemaV1, recipe.Schema);
        Assert.Empty(AutoModRecipeValidator.Validate(recipe).Issues);
    }

    [Fact]
    public void Recipe_contract_parses_and_validates()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson);
        var registry = CreateRegistry(AutoModOperationKind.SelectRecord, AutoModOperationKind.SetField);

        var result = AutoModRecipeValidator.Validate(recipe, registry);

        Assert.Empty(result.Issues);
        Assert.Equal("mhw.auto-mod.builtin.synthetic-table", recipe.Id);
        Assert.Equal(2, recipe.Inputs.Count);
    }

    [Fact]
    public void Planner_resolves_entity_property_and_scalar_input()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson);
        var registry = CreateRegistry(AutoModOperationKind.SelectRecord, AutoModOperationKind.SetField);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["record"] = ParseValue("""{"id":42,"name":"Synthetic"}"""),
            ["defense"] = ParseValue("180")
        };

        var plan = AutoModPatchPlanner.Build(recipe, inputs, registry);

        Assert.Equal(2, plan.Operations.Count);
        Assert.Equal("42", plan.Operations[0].Target);
        Assert.True(plan.Operations[1].Value.HasValue);
        Assert.Equal(180, plan.Operations[1].Value.GetValueOrDefault().GetInt32());
        Assert.Equal(@"nativePC\synthetic\table.json", plan.Outputs[0].Path);
    }

    [Fact]
    public void Unsafe_outputs_fail_closed()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson) with
        {
            Outputs = new[] { new AutoModOutputDescriptor("table", "../escape.bin") }
        };

        var result = AutoModRecipeValidator.Validate(recipe);

        Assert.Contains(result.Issues, issue => issue.Code == "output.path");
    }


    [Fact]
    public void Missing_operation_fails_during_parse()
    {
        var invalid = RecipeJson.Replace(
            "\"op\": \"select_record\",",
            "\"not_op\": \"select_record\",",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AutoModRecipeParser.Parse(invalid));
    }

    [Fact]
    public void Mixed_interpolation_is_rejected()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson);
        var steps = recipe.Steps.ToArray();
        steps[0] = steps[0] with { Target = "prefix-${record.id}" };
        recipe = recipe with { Steps = steps };

        var result = AutoModRecipeValidator.Validate(recipe);

        Assert.Contains(result.Issues, issue => issue.Code == "step.target");
    }

    [Fact]
    public void Planner_applies_declared_defaults_before_expression_resolution()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson);
        var inputs = recipe.Inputs.ToArray();
        var defenseIndex = Array.FindIndex(inputs, input => input.Key == "defense");
        inputs[defenseIndex] = inputs[defenseIndex] with
        {
            Required = false,
            DefaultValue = ParseValue("250")
        };
        recipe = recipe with { Inputs = inputs };

        var registry = CreateRegistry(AutoModOperationKind.SelectRecord, AutoModOperationKind.SetField);
        var supplied = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["record"] = ParseValue("""{"id":42,"name":"Synthetic"}""")
        };

        var plan = AutoModPatchPlanner.Build(recipe, supplied, registry);

        var plannedValue = plan.Operations[1].Value;
        Assert.True(plannedValue.HasValue);
        Assert.Equal(250, plannedValue.GetValueOrDefault().GetInt32());
    }

    [Fact]
    public void Adapter_capabilities_are_enforced()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson);
        var registry = CreateRegistry(AutoModOperationKind.SelectRecord);

        var result = AutoModRecipeValidator.Validate(recipe, registry);

        Assert.Contains(result.Issues, issue => issue.Code == "step.capability" && issue.Subject == "set_defense");
    }

    [Fact]
    public void Adapter_version_ranges_are_bounded()
    {
        Assert.True(AutoModVersionRange.IsSatisfied(">=1.0.0 <2.0.0", "1.8.4"));
        Assert.False(AutoModVersionRange.IsSatisfied(">=1.0.0 <2.0.0", "2.0.0"));
        Assert.False(AutoModVersionRange.IsSatisfied(">=1.0.0 <2.0.0", "not-a-version"));
    }

    [Fact]
    public void Manifest_serialization_is_stable_for_dictionary_order()
    {
        var recipe = AutoModRecipeParser.Parse(RecipeJson);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["record"] = ParseValue("""{"id":42}"""),
            ["defense"] = ParseValue("180")
        };

        var first = AutoModManifestSerializer.Serialize(
            recipe,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["z.adapter"] = "1.0.0",
                ["a.adapter"] = "1.0.0"
            },
            "synthetic-build",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["z"] = "sha256:2",
                ["a"] = "sha256:1"
            },
            inputs,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativePC/z"] = "sha256:4",
                ["nativePC/a"] = "sha256:3"
            });

        var second = AutoModManifestSerializer.Serialize(
            recipe,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a.adapter"] = "1.0.0",
                ["z.adapter"] = "1.0.0"
            },
            "synthetic-build",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a"] = "sha256:1",
                ["z"] = "sha256:2"
            },
            inputs,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativePC/a"] = "sha256:3",
                ["nativePC/z"] = "sha256:4"
            });

        Assert.Equal(first, second);
        Assert.Contains("\"format\": \"mhw-auto-mod-output\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_sandbox_contains_outputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "mhw-auto-mod-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sandbox = new AutoModBuildSandbox(root, new AutoModBuildLimits(MaxFiles: 2, MaxBytes: 32));
            Assert.Throws<ArgumentException>(() => sandbox.ResolveOutputPath("../escape.bin"));

            var relative = await sandbox.WriteAsync(
                "nativePC/synthetic/output.bin",
                new byte[] { 1, 2, 3 },
                TestContext.Current.CancellationToken);

            Assert.Equal(@"nativePC\synthetic\output.bin", relative);
            Assert.True(File.Exists(sandbox.ResolveOutputPath(relative)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static AutoModAdapterRegistry CreateRegistry(params AutoModOperationKind[] operations)
    {
        var registry = new AutoModAdapterRegistry();
        registry.Register(new SyntheticAdapter(operations));
        return registry;
    }

    private static JsonElement ParseValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class SyntheticAdapter(params AutoModOperationKind[] operations) : IAutoModFormatAdapter
    {
        public string Id => "synthetic.table";

        public string Version => "1.2.0";

        public IReadOnlySet<AutoModOperationKind> SupportedOperations { get; } = new HashSet<AutoModOperationKind>(operations);
    }
}
