using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MhwModManager.Core;

public static class AutoModRecipeParser
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static AutoModRecipeV1 Parse(string json)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var dto = JsonSerializer.Deserialize<RecipeDto>(json, Options)
            ?? throw new JsonException("Auto Mod recipe JSON did not contain an object.");

        var requires = dto.Requires ?? throw new JsonException("Auto Mod recipe requires is required.");
        var adapterDtos = requires.Adapters ?? throw new JsonException("Auto Mod recipe requires.adapters is required.");
        var inputDtos = dto.Inputs ?? throw new JsonException("Auto Mod recipe inputs is required.");
        var stepDtos = dto.Steps ?? throw new JsonException("Auto Mod recipe steps is required.");
        var outputDtos = dto.Outputs ?? throw new JsonException("Auto Mod recipe outputs is required.");

        var adapters = new AutoModAdapterRequirement[adapterDtos.Count];
        for (var i = 0; i < adapterDtos.Count; i++)
        {
            var requirement = adapterDtos[i];
            adapters[i] = new AutoModAdapterRequirement(requirement.Id ?? string.Empty, requirement.Version ?? string.Empty);
        }

        var inputKeys = inputDtos.Keys.ToArray();
        Array.Sort(inputKeys, StringComparer.Ordinal);
        var inputs = new AutoModInputDescriptor[inputKeys.Length];
        for (var i = 0; i < inputKeys.Length; i++)
        {
            var key = inputKeys[i];
            var input = inputDtos[key];
            var inputType = input.Type ?? throw new JsonException(string.Concat("Auto Mod input type is required: ", key));
            inputs[i] = new AutoModInputDescriptor(
                key,
                inputType,
                input.Label,
                input.Required,
                CloneNullable(input.Default),
                input.Min,
                input.Max,
                input.Pattern,
                input.Options.ToArray(),
                input.Catalog,
                input.Advanced);
        }

        var steps = new AutoModRecipeStep[stepDtos.Count];
        for (var i = 0; i < stepDtos.Count; i++)
        {
            var step = stepDtos[i];
            var operation = step.Op ?? throw new JsonException(string.Concat("Auto Mod step op is required: ", step.Id));
            steps[i] = new AutoModRecipeStep(
                step.Id ?? string.Empty,
                step.Adapter ?? string.Empty,
                operation,
                step.Source ?? string.Empty,
                step.Target,
                step.Field,
                CloneNullable(step.Expect),
                CloneNullable(step.Value));
        }

        var outputs = new AutoModOutputDescriptor[outputDtos.Count];
        for (var i = 0; i < outputDtos.Count; i++)
        {
            var output = outputDtos[i];
            outputs[i] = new AutoModOutputDescriptor(output.Source ?? string.Empty, output.Path ?? string.Empty);
        }

        return new AutoModRecipeV1(
            dto.Schema ?? string.Empty,
            dto.Id ?? string.Empty,
            dto.Version ?? string.Empty,
            dto.Name ?? string.Empty,
            dto.Game ?? string.Empty,
            adapters,
            inputs,
            steps,
            outputs);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    private static JsonElement? CloneNullable(JsonElement? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value?.Clone();
    }

    private sealed class RecipeDto
    {
        public string? Schema { get; set; }
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string? Name { get; set; }
        public string? Game { get; set; }
        public RequiresDto? Requires { get; set; }
        public Dictionary<string, InputDto>? Inputs { get; set; }
        public List<StepDto>? Steps { get; set; }
        public List<OutputDto>? Outputs { get; set; }
    }

    private sealed class RequiresDto
    {
        public List<AdapterDto>? Adapters { get; set; }
    }

    private sealed class AdapterDto
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
    }

    private sealed class InputDto
    {
        public AutoModInputKind? Type { get; set; }
        public string? Label { get; set; }
        public bool Required { get; set; }
        public JsonElement? Default { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string? Pattern { get; set; }
        public List<string> Options { get; set; } = [];
        public string? Catalog { get; set; }
        public bool Advanced { get; set; }
    }

    private sealed class StepDto
    {
        public string? Id { get; set; }
        public string? Adapter { get; set; }
        public AutoModOperationKind? Op { get; set; }
        public string? Source { get; set; }
        public string? Target { get; set; }
        public string? Field { get; set; }
        public JsonElement? Expect { get; set; }
        public JsonElement? Value { get; set; }
    }

    private sealed class OutputDto
    {
        public string? Source { get; set; }
        public string? Path { get; set; }
    }
}

public static class AutoModRecipeValidator
{
    public static AutoModValidationResult Validate(AutoModRecipeV1 recipe, AutoModAdapterRegistry? registry = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(recipe);
        var issues = new List<AutoModValidationIssue>();

        if (!string.Equals(recipe.Schema, AutoModConstants.RecipeSchemaV1, StringComparison.Ordinal))
            issues.Add(new AutoModValidationIssue("recipe.schema", "Unsupported Auto Mod recipe schema.", recipe.Schema));

        if (!IsRecipeId(recipe.Id))
            issues.Add(new AutoModValidationIssue("recipe.id", "Recipe ID must use the mhw.auto-mod.* stable-ID namespace.", recipe.Id));

        if (!IsDottedVersion(recipe.Version))
            issues.Add(new AutoModValidationIssue("recipe.version", "Recipe version must be a dotted numeric version.", recipe.Version));

        if (string.IsNullOrWhiteSpace(recipe.Name))
            issues.Add(new AutoModValidationIssue("recipe.name", "Recipe name is required."));

        if (string.IsNullOrWhiteSpace(recipe.Game))
            issues.Add(new AutoModValidationIssue("recipe.game", "Game identifier is required."));

        if (recipe.Inputs.Count > AutoModConstants.MaxInputs)
            issues.Add(new AutoModValidationIssue("recipe.input_limit", "Recipe declares too many inputs."));

        if (recipe.Steps.Count > AutoModConstants.MaxSteps)
            issues.Add(new AutoModValidationIssue("recipe.step_limit", "Recipe declares too many steps."));

        if (recipe.Outputs.Count == 0 || recipe.Outputs.Count > AutoModConstants.MaxOutputs)
            issues.Add(new AutoModValidationIssue("recipe.output_limit", "Recipe must declare between 1 and the configured maximum number of outputs."));

        ValidateAdapters(recipe, registry, issues);
        ValidateInputs(recipe, issues);
        ValidateSteps(recipe, registry, issues);
        ValidateOutputs(recipe, issues);

        return new AutoModValidationResult(issues);
    }

    private static void ValidateAdapters(
        AutoModRecipeV1 recipe,
        AutoModAdapterRegistry? registry,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < recipe.AdapterRequirements.Count; i++)
        {
            var requirement = recipe.AdapterRequirements[i];
            if (string.IsNullOrWhiteSpace(requirement.Id) || !ids.Add(requirement.Id))
            {
                issues.Add(new AutoModValidationIssue("adapter.id", "Adapter IDs must be non-empty and unique.", requirement.Id));
                continue;
            }

            if (string.IsNullOrWhiteSpace(requirement.VersionRange))
                issues.Add(new AutoModValidationIssue("adapter.version_range", "Adapter version range is required.", requirement.Id));

            if (registry is null)
                continue;

            if (!registry.TryResolve(requirement.Id, out var adapter) || adapter is null)
            {
                issues.Add(new AutoModValidationIssue("adapter.missing", "Required adapter is not registered.", requirement.Id));
                continue;
            }

            if (!AutoModVersionRange.IsSatisfied(requirement.VersionRange, adapter.Version))
                issues.Add(new AutoModValidationIssue("adapter.version", "Registered adapter does not satisfy the recipe version range.", requirement.Id));
        }
    }

    private static void ValidateInputs(AutoModRecipeV1 recipe, List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < recipe.Inputs.Count; i++)
        {
            var input = recipe.Inputs[i];
            if (!IsInputKey(input.Key) || !keys.Add(input.Key))
                issues.Add(new AutoModValidationIssue("input.key", "Input keys must be unique lower_snake_case identifiers.", input.Key));

            if (input.Minimum.HasValue && input.Maximum.HasValue && input.Minimum.Value > input.Maximum.Value)
                issues.Add(new AutoModValidationIssue("input.range", "Input minimum cannot exceed maximum.", input.Key));

            if (input.Kind == AutoModInputKind.Entity && string.IsNullOrWhiteSpace(input.Catalog))
                issues.Add(new AutoModValidationIssue("input.catalog", "Entity inputs must declare a catalog.", input.Key));

            if (input.Kind == AutoModInputKind.Enum && input.Options.Count == 0)
                issues.Add(new AutoModValidationIssue("input.options", "Enum inputs must declare at least one option.", input.Key));

            if (!string.IsNullOrWhiteSpace(input.Pattern) && !IsValidRegex(input.Pattern))
                issues.Add(new AutoModValidationIssue("input.pattern", "Input regex is invalid.", input.Key));
        }
    }

    private static void ValidateSteps(
        AutoModRecipeV1 recipe,
        AutoModAdapterRegistry? registry,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < recipe.Steps.Count; i++)
        {
            var step = recipe.Steps[i];

            if (!IsInputKey(step.Id) || !ids.Add(step.Id))
                issues.Add(new AutoModValidationIssue("step.id", "Step IDs must be unique lower_snake_case identifiers.", step.Id));

            if (string.IsNullOrWhiteSpace(step.AdapterId) || !HasDeclaredAdapter(recipe, step.AdapterId))
                issues.Add(new AutoModValidationIssue("step.adapter", "Each step must reference a declared adapter.", step.Id));

            if (string.IsNullOrWhiteSpace(step.Source) || !IsInputKey(step.Source))
                issues.Add(new AutoModValidationIssue("step.source", "Step source aliases must be lower_snake_case identifiers.", step.Id));

            ValidateScalarExpression(step.Target, "step.target", step.Id, issues);
            ValidateScalarExpression(step.Field, "step.field", step.Id, issues);
            ValidateElementExpression(step.ExpectedValue, "step.expect", step.Id, issues);
            ValidateElementExpression(step.Value, "step.value", step.Id, issues);

            if (RequiresField(step.Operation) && string.IsNullOrWhiteSpace(step.Field))
                issues.Add(new AutoModValidationIssue("step.field_required", "This operation requires a field.", step.Id));

            if (registry is not null &&
                registry.TryResolve(step.AdapterId, out var adapter) &&
                adapter is not null &&
                !adapter.SupportedOperations.Contains(step.Operation))
            {
                issues.Add(new AutoModValidationIssue("step.capability", "Adapter does not support the requested operation.", step.Id));
            }
        }
    }

    private static void ValidateOutputs(AutoModRecipeV1 recipe, List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var paths = new HashSet<string>(PathRules.Comparer);

        for (var i = 0; i < recipe.Outputs.Count; i++)
        {
            var output = recipe.Outputs[i];
            if (string.IsNullOrWhiteSpace(output.Source) || !IsInputKey(output.Source))
                issues.Add(new AutoModValidationIssue("output.source", "Output source aliases must be lower_snake_case identifiers.", output.Source));

            try
            {
                var normalized = PathRules.Normalize(output.Path);
                if (!paths.Add(normalized))
                    issues.Add(new AutoModValidationIssue("output.duplicate", "Recipe declares the same output path more than once.", normalized));
            }
            catch (ArgumentException)
            {
                issues.Add(new AutoModValidationIssue("output.path", "Output path is unsafe or outside a supported mod root.", output.Path));
            }
        }
    }

    private static void ValidateScalarExpression(
        string? value,
        string code,
        string subject,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!string.IsNullOrEmpty(value) &&
            value.Contains("${", StringComparison.Ordinal) &&
            !AutoModExpressionResolver.IsValidReferenceExpression(value))
        {
            issues.Add(new AutoModValidationIssue(code, "Expression must be one bounded input/catalog property reference.", subject));
        }
    }

    private static void ValidateElementExpression(
        JsonElement? value,
        string code,
        string subject,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!value.HasValue || value.Value.ValueKind != JsonValueKind.String)
            return;

        ValidateScalarExpression(value.Value.GetString(), code, subject, issues);
    }

    private static bool HasDeclaredAdapter(AutoModRecipeV1 recipe, string adapterId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        for (var i = 0; i < recipe.AdapterRequirements.Count; i++)
        {
            if (string.Equals(recipe.AdapterRequirements[i].Id, adapterId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool RequiresField(AutoModOperationKind operation)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return operation is AutoModOperationKind.AssertField
            or AutoModOperationKind.SetField
            or AutoModOperationKind.SetBitField
            or AutoModOperationKind.ReplaceEnum
            or AutoModOperationKind.ReplaceReference;
    }

    private static bool IsDottedVersion(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value.Contains('.', StringComparison.Ordinal) && System.Version.TryParse(value, out _);
    }

    private static bool IsRecipeId(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        const string prefix = "mhw.auto-mod.";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length <= prefix.Length)
            return false;

        for (var i = prefix.Length; i < value.Length; i++)
        {
            var c = value[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '-')
                continue;
            return false;
        }

        return true;
    }

    private static bool IsInputKey(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsIdentifierStart(value[0]))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (!IsIdentifierPart(c))
                return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (c >= 'a' && c <= 'z') || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }

    private static bool IsValidRegex(string pattern)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public static class AutoModInputValidator
{
    public static AutoModValidationResult Validate(
        AutoModRecipeV1 recipe,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(values);
        var issues = new List<AutoModValidationIssue>();
        var known = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < recipe.Inputs.Count; i++)
        {
            var descriptor = recipe.Inputs[i];
            known.Add(descriptor.Key);

            if (!values.TryGetValue(descriptor.Key, out var value))
            {
                if (descriptor.Required && !descriptor.DefaultValue.HasValue)
                    issues.Add(new AutoModValidationIssue("input.required", "Required input is missing.", descriptor.Key));
                continue;
            }

            ValidateValue(descriptor, value, issues);
        }

        foreach (var pair in values)
        {
            if (!known.Contains(pair.Key))
                issues.Add(new AutoModValidationIssue("input.unknown", "Input is not declared by this recipe.", pair.Key));
        }

        return new AutoModValidationResult(issues);
    }

    private static void ValidateValue(
        AutoModInputDescriptor descriptor,
        JsonElement value,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!HasExpectedKind(descriptor.Kind, value))
        {
            issues.Add(new AutoModValidationIssue("input.type", "Input value has the wrong JSON type.", descriptor.Key));
            return;
        }

        if (descriptor.Kind is AutoModInputKind.Integer or AutoModInputKind.Decimal)
            ValidateNumber(descriptor, value, issues);

        if (descriptor.Kind is AutoModInputKind.String or AutoModInputKind.File or AutoModInputKind.Image or AutoModInputKind.Color)
            ValidateString(descriptor, value, issues);

        if (descriptor.Kind == AutoModInputKind.Enum)
            ValidateEnum(descriptor, value, issues);

        if (descriptor.Kind == AutoModInputKind.Entity &&
            (!value.TryGetProperty("id", out var id) || id.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
        {
            issues.Add(new AutoModValidationIssue("input.entity_id", "Entity input must contain a non-null id property.", descriptor.Key));
        }
    }

    private static bool HasExpectedKind(AutoModInputKind kind, JsonElement value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return kind switch
        {
            AutoModInputKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            AutoModInputKind.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            AutoModInputKind.String or AutoModInputKind.Enum or AutoModInputKind.File or
                AutoModInputKind.Image or AutoModInputKind.Color => value.ValueKind == JsonValueKind.String,
            AutoModInputKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            AutoModInputKind.Entity or AutoModInputKind.Group => value.ValueKind == JsonValueKind.Object,
            AutoModInputKind.List => value.ValueKind == JsonValueKind.Array,
            _ => false
        };
    }

    private static void ValidateNumber(
        AutoModInputDescriptor descriptor,
        JsonElement value,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!value.TryGetDecimal(out var number))
            return;

        if (descriptor.Minimum.HasValue && number < descriptor.Minimum.Value)
            issues.Add(new AutoModValidationIssue("input.min", "Input is below the allowed minimum.", descriptor.Key));

        if (descriptor.Maximum.HasValue && number > descriptor.Maximum.Value)
            issues.Add(new AutoModValidationIssue("input.max", "Input exceeds the allowed maximum.", descriptor.Key));
    }

    private static void ValidateString(
        AutoModInputDescriptor descriptor,
        JsonElement value,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var text = value.GetString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(descriptor.Pattern) &&
            !Regex.IsMatch(text, descriptor.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            issues.Add(new AutoModValidationIssue("input.pattern", "Input does not match the required pattern.", descriptor.Key));
        }
    }

    private static void ValidateEnum(
        AutoModInputDescriptor descriptor,
        JsonElement value,
        List<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var selected = value.GetString() ?? string.Empty;
        for (var i = 0; i < descriptor.Options.Count; i++)
        {
            if (string.Equals(descriptor.Options[i], selected, StringComparison.Ordinal))
                return;
        }

        issues.Add(new AutoModValidationIssue("input.enum", "Input is not one of the declared enum values.", descriptor.Key));
    }
}


public static class AutoModInputResolver
{
    public static IReadOnlyDictionary<string, JsonElement> ResolveWithDefaults(
        AutoModRecipeV1 recipe,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(values);

        var validation = AutoModInputValidator.Validate(recipe, values);
        if (validation.Issues.Count != 0)
            throw new InvalidOperationException(BuildValidationMessage(validation.Issues));

        var resolved = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i < recipe.Inputs.Count; i++)
        {
            var descriptor = recipe.Inputs[i];
            if (values.TryGetValue(descriptor.Key, out var supplied))
            {
                resolved.Add(descriptor.Key, supplied.Clone());
                continue;
            }

            if (descriptor.DefaultValue.HasValue)
                resolved.Add(descriptor.Key, descriptor.DefaultValue.Value.Clone());
        }

        return resolved;
    }

    private static string BuildValidationMessage(IReadOnlyList<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var builder = new StringBuilder("Auto Mod input validation failed:");
        for (var i = 0; i < issues.Count; i++)
        {
            builder.Append(' ');
            builder.Append(issues[i].Code);
            builder.Append('=');
            builder.Append(issues[i].Message);
        }

        return builder.ToString();
    }
}

public static class AutoModExpressionResolver
{
    public static bool IsValidReferenceExpression(string text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (text.Length < 4 || text.Length > 132 ||
            !text.StartsWith("${", StringComparison.Ordinal) ||
            !text.EndsWith('}'))
        {
            return false;
        }

        var inner = text[2..^1];
        var segments = inner.Split('.', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Length > 4)
            return false;

        for (var i = 0; i < segments.Length; i++)
        {
            if (!IsIdentifier(segments[i]))
                return false;
        }

        return true;
    }

    public static JsonElement Resolve(
        JsonElement value,
        IReadOnlyDictionary<string, JsonElement> inputs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(inputs);

        if (value.ValueKind != JsonValueKind.String)
            return value.Clone();

        var text = value.GetString() ?? string.Empty;
        return IsValidReferenceExpression(text)
            ? ResolveReference(text, inputs)
            : value.Clone();
    }

    public static string? ResolveText(
        string? value,
        IReadOnlyDictionary<string, JsonElement> inputs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value is null || !IsValidReferenceExpression(value))
            return value;

        var resolved = ResolveReference(value, inputs);
        return resolved.ValueKind switch
        {
            JsonValueKind.String => resolved.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => resolved.GetRawText(),
            _ => throw new InvalidOperationException("Auto Mod scalar expression resolved to a non-scalar value.")
        };
    }

    private static JsonElement ResolveReference(
        string expression,
        IReadOnlyDictionary<string, JsonElement> inputs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var inner = expression[2..^1];
        var segments = inner.Split('.', StringSplitOptions.None);

        if (!inputs.TryGetValue(segments[0], out var current))
            throw new KeyNotFoundException(string.Concat("Auto Mod input is not available: ", segments[0]));

        for (var i = 1; i < segments.Length; i++)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segments[i], out var next))
            {
                throw new KeyNotFoundException(string.Concat("Auto Mod input property is not available: ", inner));
            }

            current = next;
        }

        return current.Clone();
    }

    private static bool IsIdentifier(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;

        if (!IsIdentifierStart(value[0]))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (!IsIdentifierStart(c) && (c < '0' || c > '9'))
                return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (c >= 'a' && c <= 'z') || c == '_';
    }
}

public static class AutoModPatchPlanner
{
    public static AutoModPatchPlan Build(
        AutoModRecipeV1 recipe,
        IReadOnlyDictionary<string, JsonElement> inputs,
        AutoModAdapterRegistry registry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(registry);

        var recipeValidation = AutoModRecipeValidator.Validate(recipe, registry);
        if (recipeValidation.Issues.Count != 0)
            throw new InvalidOperationException(BuildValidationMessage(recipeValidation));

        var resolvedInputs = AutoModInputResolver.ResolveWithDefaults(recipe, inputs);
        var operations = new AutoModPatchOperation[recipe.Steps.Count];
        for (var i = 0; i < recipe.Steps.Count; i++)
        {
            var step = recipe.Steps[i];
            var expected = ResolveNullable(step.ExpectedValue, resolvedInputs);
            var value = ResolveNullable(step.Value, resolvedInputs);

            operations[i] = new AutoModPatchOperation(
                i,
                step.Id,
                step.AdapterId,
                step.Operation,
                step.Source,
                AutoModExpressionResolver.ResolveText(step.Target, resolvedInputs),
                AutoModExpressionResolver.ResolveText(step.Field, resolvedInputs),
                expected,
                value);
        }

        var outputs = new AutoModOutputDescriptor[recipe.Outputs.Count];
        for (var i = 0; i < recipe.Outputs.Count; i++)
        {
            var output = recipe.Outputs[i];
            outputs[i] = new AutoModOutputDescriptor(output.Source, PathRules.Normalize(output.Path));
        }

        return new AutoModPatchPlan(recipe.Id, recipe.Version, operations, outputs);
    }

    private static JsonElement? ResolveNullable(
        JsonElement? value,
        IReadOnlyDictionary<string, JsonElement> inputs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return value.HasValue ? AutoModExpressionResolver.Resolve(value.Value, inputs) : null;
    }

    private static string BuildValidationMessage(AutoModValidationResult recipeValidation)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var builder = new StringBuilder("Auto Mod plan validation failed:");
        AppendIssues(builder, recipeValidation.Issues);
        return builder.ToString();
    }

    private static void AppendIssues(StringBuilder builder, IReadOnlyList<AutoModValidationIssue> issues)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        for (var i = 0; i < issues.Count; i++)
        {
            builder.Append(' ');
            builder.Append(issues[i].Code);
            builder.Append('=');
            builder.Append(issues[i].Message);
        }
    }
}

public static class AutoModManifestSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(
        AutoModRecipeV1 recipe,
        IReadOnlyDictionary<string, string> adapterVersions,
        string? gameBuild,
        IReadOnlyDictionary<string, string> sourceFingerprints,
        IReadOnlyDictionary<string, JsonElement> inputs,
        IReadOnlyDictionary<string, string> outputFingerprints)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(adapterVersions);
        ArgumentNullException.ThrowIfNull(sourceFingerprints);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputFingerprints);

        var manifest = new AutoModGeneratedManifestV1(
            AutoModConstants.GeneratedManifestFormat,
            AutoModConstants.GeneratedManifestFormatVersion,
            recipe.Id,
            recipe.Version,
            CopySorted(adapterVersions),
            gameBuild,
            CopySorted(sourceFingerprints),
            CopySortedElements(inputs),
            CopySorted(outputFingerprints));

        return JsonSerializer.Serialize(manifest, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    private static SortedDictionary<string, string> CopySorted(IReadOnlyDictionary<string, string> values)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
            sorted.Add(pair.Key, pair.Value);
        return sorted;
    }

    private static SortedDictionary<string, JsonElement> CopySortedElements(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var sorted = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in values)
            sorted.Add(pair.Key, pair.Value.Clone());
        return sorted;
    }
}
