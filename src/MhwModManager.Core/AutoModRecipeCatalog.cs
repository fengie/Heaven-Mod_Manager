using System.Text.Json;

namespace MhwModManager.Core;

public sealed record AutoModRecipeSource(string SourceId, string Json);

public sealed record AutoModRecipeCatalogEntry(
    string SourceId,
    AutoModRecipeV1 Recipe,
    IReadOnlyList<AutoModValidationIssue> AdapterIssues)
{
    // Readiness describes the registered adapter contract, not a verified build.
    public bool AdapterReady
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return AdapterIssues.Count == 0;
        }
    }
}

public sealed record AutoModRecipeCatalogRejection(
    string SourceId,
    IReadOnlyList<AutoModValidationIssue> Issues);

public sealed record AutoModRecipeCatalogSnapshot(
    IReadOnlyList<AutoModRecipeCatalogEntry> Entries,
    IReadOnlyList<AutoModRecipeCatalogRejection> Rejected)
{
    public IReadOnlyList<AutoModRecipeCatalogEntry> Search(
        string gameId, string? query = null, bool includeUnavailable = true)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var term = query?.Trim();
        return Entries.Where(entry =>
            string.Equals(entry.Recipe.Game, gameId, StringComparison.OrdinalIgnoreCase)
            && (includeUnavailable || entry.AdapterReady)
            && (string.IsNullOrEmpty(term)
                || entry.Recipe.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || entry.Recipe.Id.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}

/// <summary>
/// Bounded read-only catalog over explicitly supplied declarative sources.
/// Never discovers or loads executable adapters, reads files, or builds mods.
/// </summary>
public static class AutoModRecipeCatalog
{
    public const int MaxSources = 128;

    public static AutoModRecipeCatalogSnapshot Build(
        IReadOnlyList<AutoModRecipeSource> sources, AutoModAdapterRegistry registry)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(registry);
        if (sources.Count > MaxSources)
            throw new ArgumentException("Auto Mod recipe catalog exceeds the source-count limit.", nameof(sources));

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.SourceId)
                || !sourceIds.Add(source.SourceId))
                throw new ArgumentException("Recipe source IDs must be nonempty and unique.", nameof(sources));
        }

        var parsed = new List<(string SourceId, AutoModRecipeV1 Recipe)>();
        var rejected = new List<AutoModRecipeCatalogRejection>();
        foreach (var source in sources)
        {
            AutoModRecipeV1 recipe;
            try
            {
                recipe = AutoModRecipeParser.Parse(source.Json);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                rejected.Add(new(source.SourceId, Array.AsReadOnly(new[]
                {
                    new AutoModValidationIssue("catalog.parse",
                        "Recipe JSON is malformed or exceeds its admission limits.")
                })));
                continue;
            }

            var structural = AutoModRecipeValidator.Validate(recipe);
            if (structural.Issues.Count > 0)
            {
                rejected.Add(new(source.SourceId, Array.AsReadOnly(structural.Issues.ToArray())));
                continue;
            }
            parsed.Add((source.SourceId, recipe));
        }

        var entries = new List<AutoModRecipeCatalogEntry>();
        foreach (var group in parsed.GroupBy(item => item.Recipe.Id, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            if (members.Length > 1)
            {
                // Never pick a winner by source order, version, or adapter availability.
                foreach (var member in members)
                    rejected.Add(new(member.SourceId, Array.AsReadOnly(new[]
                    {
                        new AutoModValidationIssue("catalog.duplicate_id",
                            "Multiple recipe sources declare the same stable ID.", group.Key)
                    })));
                continue;
            }

            var candidate = members[0];
            var availability = AutoModRecipeValidator.Validate(candidate.Recipe, registry);
            entries.Add(new(candidate.SourceId, candidate.Recipe,
                Array.AsReadOnly(availability.Issues.ToArray())));
        }

        return new(
            Array.AsReadOnly(entries.OrderBy(entry => entry.Recipe.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Recipe.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(rejected.OrderBy(entry => entry.SourceId, StringComparer.Ordinal).ToArray()));
    }
}
