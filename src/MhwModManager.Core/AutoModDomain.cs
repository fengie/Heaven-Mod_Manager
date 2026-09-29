using System.Text.Json;

namespace MhwModManager.Core;

public static class AutoModConstants
{
    public const string RecipeSchemaV1 = "mhw-auto-mod-recipe/v1";
    public const string GeneratedManifestFormat = "mhw-auto-mod-output";
    public const int GeneratedManifestFormatVersion = 1;
    public const int MaxInputs = 64;
    public const int MaxSteps = 256;
    public const int MaxOutputs = 64;
}

// These enum names are serialized as stable Auto Mod Recipe v1 wire tokens via
// JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower). Renaming them would
// break the public recipe contract, so suppress CA1720 only for this enum.
#pragma warning disable CA1720
public enum AutoModInputKind
{
    Integer,
    Decimal,
    String,
    Boolean,
    Enum,
    Entity,
    File,
    Image,
    Color,
    List,
    Group
}
#pragma warning restore CA1720

public enum AutoModOperationKind
{
    SelectRecord,
    AssertField,
    SetField,
    SetBitField,
    ReplaceEnum,
    ReplaceReference,
    InsertRecord,
    DeleteRecord,
    CopyRecord,
    ReplaceAsset,
    WriteOutput
}

public sealed record AutoModAdapterRequirement(string Id, string VersionRange);

public sealed record AutoModInputDescriptor(
    string Key,
    AutoModInputKind Kind,
    string? Label,
    bool Required,
    JsonElement? DefaultValue,
    decimal? Minimum,
    decimal? Maximum,
    string? Pattern,
    IReadOnlyList<string> Options,
    string? Catalog,
    bool Advanced);

public sealed record AutoModRecipeStep(
    string Id,
    string AdapterId,
    AutoModOperationKind Operation,
    string Source,
    string? Target,
    string? Field,
    JsonElement? ExpectedValue,
    JsonElement? Value);

public sealed record AutoModOutputDescriptor(string Source, string Path);

public sealed record AutoModRecipeV1(
    string Schema,
    string Id,
    string Version,
    string Name,
    string Game,
    IReadOnlyList<AutoModAdapterRequirement> AdapterRequirements,
    IReadOnlyList<AutoModInputDescriptor> Inputs,
    IReadOnlyList<AutoModRecipeStep> Steps,
    IReadOnlyList<AutoModOutputDescriptor> Outputs);

public sealed record AutoModValidationIssue(string Code, string Message, string? Subject = null);

public sealed record AutoModValidationResult(IReadOnlyList<AutoModValidationIssue> Issues);

public sealed record AutoModPatchOperation(
    int Sequence,
    string StepId,
    string AdapterId,
    AutoModOperationKind Operation,
    string Source,
    string? Target,
    string? Field,
    JsonElement? ExpectedValue,
    JsonElement? Value);

public sealed record AutoModPatchPlan(
    string RecipeId,
    string RecipeVersion,
    IReadOnlyList<AutoModPatchOperation> Operations,
    IReadOnlyList<AutoModOutputDescriptor> Outputs);

public sealed record AutoModGeneratedManifestV1(
    string Format,
    int FormatVersion,
    string RecipeId,
    string RecipeVersion,
    IReadOnlyDictionary<string, string> AdapterVersions,
    string? GameBuild,
    IReadOnlyDictionary<string, string> SourceFingerprints,
    IReadOnlyDictionary<string, JsonElement> Inputs,
    IReadOnlyDictionary<string, string> OutputFingerprints);

public sealed record AutoModBuildLimits(int MaxFiles = 128, long MaxBytes = 268_435_456);

public interface IAutoModFormatAdapter
{
    string Id { get; }

    string Version { get; }

    IReadOnlySet<AutoModOperationKind> SupportedOperations { get; }
}

public sealed class AutoModAdapterRegistry
{
    private readonly Dictionary<string, IAutoModFormatAdapter> _adapters = new(StringComparer.Ordinal);

    public void Register(IAutoModFormatAdapter adapter)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.Version);

        if (!_adapters.TryAdd(adapter.Id, adapter))
            throw new InvalidOperationException(string.Concat("Auto Mod adapter already registered: ", adapter.Id));
    }

    public bool TryResolve(string id, out IAutoModFormatAdapter? adapter)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _adapters.TryGetValue(id, out adapter);
    }

    public IAutoModFormatAdapter Resolve(string id)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!TryResolve(id, out var adapter) || adapter is null)
            throw new KeyNotFoundException(string.Concat("Auto Mod adapter is not registered: ", id));
        return adapter;
    }
}

public static class AutoModVersionRange
{
    public static bool IsSatisfied(string versionRange, string candidateVersion)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(versionRange);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateVersion);

        if (!System.Version.TryParse(candidateVersion, out var candidate))
            return false;

        var tokens = versionRange.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return false;

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!IsSatisfiedToken(candidate, tokens[i]))
                return false;
        }

        return true;
    }

    private static bool IsSatisfiedToken(System.Version candidate, string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var opLength = token.StartsWith(">=", StringComparison.Ordinal) ||
                       token.StartsWith("<=", StringComparison.Ordinal)
            ? 2
            : token.StartsWith('>') || token.StartsWith('<') || token.StartsWith('=')
                ? 1
                : 0;

        var versionText = token[opLength..];
        if (!System.Version.TryParse(versionText, out var required))
            return false;

        var comparison = candidate.CompareTo(required);
        return opLength switch
        {
            0 => comparison == 0,
            1 when token[0] == '>' => comparison > 0,
            1 when token[0] == '<' => comparison < 0,
            1 when token[0] == '=' => comparison == 0,
            2 when token[0] == '>' => comparison >= 0,
            2 when token[0] == '<' => comparison <= 0,
            _ => false
        };
    }
}
