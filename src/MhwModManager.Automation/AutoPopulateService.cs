using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed record AutoPopulateDecision(string ModId, string ModName, bool Enabled, string Reason);

public sealed record AutoPopulateResult(
    IReadOnlyDictionary<string,ModState> State,
    IReadOnlyList<AutoPopulateDecision> Decisions,
    int EnabledMods,
    int SkippedConflicts,
    int SkippedRequirements,
    string Summary);

internal sealed record ModDependencyRequirement(
    string Token,
    bool Required = true,
    string? ExactVersion = null,
    string? MinVersion = null,
    string? MaxVersion = null);

internal sealed record ModRequirementSpec(
    string ModId,
    IReadOnlyList<ModDependencyRequirement> ModRequirements,
    IReadOnlyList<string> RequiredPaths,
    IReadOnlyList<string> RequiredTexturePaths,
    bool RequiresNativeLoader,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Errors);

internal static class ModRequirementReader
{
    private static readonly string[] SidecarNames = ["mod-manager.requirements.json", "mhw-manager.requirements.json"];

    public static async Task<ModRequirementSpec> ReadAsync(
        ModDescriptor mod,
        IReadOnlyList<ModFileDescriptor> files,
        bool isMonsterHunterWorld,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var modRequirements = new Dictionary<string,ModDependencyRequirement>(StringComparer.OrdinalIgnoreCase);
        var requiredPaths = new HashSet<string>(PathRules.Comparer);
        var requiredTextures = new HashSet<string>(PathRules.Comparer);
        var evidence = new List<string>();
        var errors = new List<string>();

        var requiresLoader = isMonsterHunterWorld && files.Any(f =>
            (f.FileClass == FileClass.Plugin || f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) &&
            !IsLoaderPath(f.Path));
        if (requiresLoader)
            evidence.Add("Contains an MHW native plugin/DLL and therefore requires a native plugin loader.");

        var sidecar = SidecarNames.Select(name => Path.Combine(mod.SourcePath, name)).FirstOrDefault(File.Exists);
        if (sidecar is not null)
        {
            try
            {
                await using var stream = new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                ReadPathArray(root, "files", requiredPaths, errors);
                ReadPathArray(root, "textures", requiredTextures, errors);
                ReadDependencyArray(root, "mods", modRequirements, errors);
                ReadDependencyArray(root, "dependencies", modRequirements, errors);
                ReadDependencyArray(root, "requires", modRequirements, errors);

                if (requiredPaths.Count > 0)
                    evidence.Add($"Explicit sidecar file requirements: {string.Join(", ", requiredPaths)}");
                if (requiredTextures.Count > 0)
                    evidence.Add($"Explicit sidecar texture requirements: {string.Join(", ", requiredTextures)}");
                var hardRequirements = modRequirements.Values.Where(x => x.Required).OrderBy(x => x.Token, StringComparer.OrdinalIgnoreCase).ToArray();
                var optionalRequirements = modRequirements.Values.Where(x => !x.Required).OrderBy(x => x.Token, StringComparer.OrdinalIgnoreCase).ToArray();
                if (hardRequirements.Length > 0)
                    evidence.Add($"Explicit hard mod requirements: {string.Join(", ", hardRequirements.Select(DescribeRequirement))}");
                if (optionalRequirements.Length > 0)
                    evidence.Add($"Optional mod relationships: {string.Join(", ", optionalRequirements.Select(DescribeRequirement))}");
            }
            catch (JsonException ex)
            {
                errors.Add("Invalid requirements sidecar: " + ex.Message);
            }
            catch (IOException ex)
            {
                errors.Add("Could not read requirements sidecar: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add("Could not read requirements sidecar: " + ex.Message);
            }
        }

        return new(
            mod.Id,
            modRequirements.Values.OrderBy(x => x.Token, StringComparer.OrdinalIgnoreCase).ToArray(),
            requiredPaths.Order(PathRules.Comparer).ToArray(),
            requiredTextures.Order(PathRules.Comparer).ToArray(),
            requiresLoader,
            evidence,
            errors);
    }

    public static bool MatchesToken(ModDescriptor mod, string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (StringComparer.OrdinalIgnoreCase.Equals(mod.Id, token) ||
            StringComparer.OrdinalIgnoreCase.Equals(mod.NexusModId, token) ||
            StringComparer.OrdinalIgnoreCase.Equals(mod.NexusModUuid, token))
            return true;

        if (token.StartsWith("nexus:", StringComparison.OrdinalIgnoreCase))
        {
            var value = token["nexus:".Length..];
            return StringComparer.OrdinalIgnoreCase.Equals(mod.NexusModId, value) ||
                   StringComparer.OrdinalIgnoreCase.Equals(mod.NexusModUuid, value);
        }

        return false;
    }

    public static string LivePath(string gameRoot, string normalized)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var relative = normalized.StartsWith("root\\", StringComparison.OrdinalIgnoreCase)
            ? normalized["root\\".Length..]
            : normalized;
        return Path.Combine(gameRoot, relative.Replace('\\', Path.DirectorySeparatorChar));
    }

    public static bool IsLoaderPath(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        string normalized;
        try { normalized = PathRules.Normalize(path); }
        catch (ArgumentException) { return false; }

        return normalized.Equals(@"root\dinput8.dll", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(@"root\loader.dll", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(@"root\loader-config.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadPathArray(JsonElement root, string propertyName, HashSet<string> output, List<string> errors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"Requirement '{propertyName}' contains a non-string entry.");
                continue;
            }

            var raw = item.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            try
            {
                output.Add(NormalizeRequirementPath(raw));
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Unsafe requirement path '{raw}': {ex.Message}");
            }
        }
    }

    private static string NormalizeRequirementPath(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = value.Replace('/', '\\').Trim();
        if (!normalized.StartsWith("nativePC\\", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("root\\", StringComparison.OrdinalIgnoreCase))
            normalized = "root\\" + normalized.TrimStart('\\');
        return PathRules.Normalize(normalized);
    }

    public static bool VersionSatisfies(ModDescriptor mod, ModDependencyRequirement requirement, out string reason)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(requirement.ExactVersion) &&
            string.IsNullOrWhiteSpace(requirement.MinVersion) &&
            string.IsNullOrWhiteSpace(requirement.MaxVersion))
            return true;

        var actual = NormalizeVersionText(mod.NexusVersion);
        if (actual is null)
        {
            reason = $"Installed provider '{mod.DisplayName}' has no trustworthy version metadata for requirement {DescribeRequirement(requirement)}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requirement.ExactVersion) &&
            !StringComparer.OrdinalIgnoreCase.Equals(actual, NormalizeVersionText(requirement.ExactVersion)))
        {
            reason = $"Installed provider '{mod.DisplayName}' is version '{mod.NexusVersion}', but {DescribeRequirement(requirement)} is required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requirement.MinVersion))
        {
            if (!TryParseComparableVersion(actual, out var actualVersion) ||
                !TryParseComparableVersion(requirement.MinVersion!, out var minimum))
            {
                reason = $"Cannot safely compare installed version '{mod.NexusVersion}' with minimum version '{requirement.MinVersion}' for '{requirement.Token}'.";
                return false;
            }
            if (actualVersion < minimum)
            {
                reason = $"Installed provider '{mod.DisplayName}' is version '{mod.NexusVersion}', below required minimum '{requirement.MinVersion}'.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(requirement.MaxVersion))
        {
            if (!TryParseComparableVersion(actual, out var actualVersion) ||
                !TryParseComparableVersion(requirement.MaxVersion!, out var maximum))
            {
                reason = $"Cannot safely compare installed version '{mod.NexusVersion}' with maximum version '{requirement.MaxVersion}' for '{requirement.Token}'.";
                return false;
            }
            if (actualVersion > maximum)
            {
                reason = $"Installed provider '{mod.DisplayName}' is version '{mod.NexusVersion}', above supported maximum '{requirement.MaxVersion}'.";
                return false;
            }
        }

        return true;
    }

    public static string DescribeRequirement(ModDependencyRequirement requirement)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(requirement.ExactVersion)) clauses.Add($"version={requirement.ExactVersion}");
        if (!string.IsNullOrWhiteSpace(requirement.MinVersion)) clauses.Add($"minVersion={requirement.MinVersion}");
        if (!string.IsNullOrWhiteSpace(requirement.MaxVersion)) clauses.Add($"maxVersion={requirement.MaxVersion}");
        return clauses.Count == 0 ? requirement.Token : $"{requirement.Token} ({string.Join(", ", clauses)})";
    }

    private static void ReadDependencyArray(
        JsonElement root,
        string propertyName,
        Dictionary<string,ModDependencyRequirement> output,
        List<string> errors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var token = item.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                    MergeRequirement(output, new(token.Trim()), errors);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"Requirement '{propertyName}' contains an unsupported dependency entry.");
                continue;
            }

            string? token = null;
            foreach (var key in new[] { "modId", "id", "nexusModId", "nexusModUuid" })
            {
                if (!item.TryGetProperty(key, out var tokenValue) || tokenValue.ValueKind != JsonValueKind.String)
                    continue;
                token = tokenValue.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    break;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                errors.Add($"Requirement '{propertyName}' contains a dependency object without a supported mod identity.");
                continue;
            }

            var required = !item.TryGetProperty("required", out var requiredValue) ||
                           requiredValue.ValueKind != JsonValueKind.False;
            var exactVersion = ReadString(item, "version") ?? ReadString(item, "exactVersion");
            var minVersion = ReadString(item, "minVersion");
            var maxVersion = ReadString(item, "maxVersion");
            MergeRequirement(output, new(token, required, exactVersion, minVersion, maxVersion), errors);
        }
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static void MergeRequirement(
        Dictionary<string,ModDependencyRequirement> output,
        ModDependencyRequirement incoming,
        List<string> errors)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!output.TryGetValue(incoming.Token, out var existing))
        {
            output[incoming.Token] = incoming;
            return;
        }

        static string? MergeConstraint(string token, string label, string? left, string? right, List<string> targetErrors)
        {
            if (string.IsNullOrWhiteSpace(left)) return right;
            if (string.IsNullOrWhiteSpace(right)) return left;
            if (StringComparer.OrdinalIgnoreCase.Equals(left, right)) return left;
            targetErrors.Add($"Dependency '{token}' declares conflicting {label} constraints '{left}' and '{right}'.");
            return left;
        }

        output[incoming.Token] = new(
            incoming.Token,
            existing.Required || incoming.Required,
            MergeConstraint(incoming.Token, "exact-version", existing.ExactVersion, incoming.ExactVersion, errors),
            MergeConstraint(incoming.Token, "minimum-version", existing.MinVersion, incoming.MinVersion, errors),
            MergeConstraint(incoming.Token, "maximum-version", existing.MaxVersion, incoming.MaxVersion, errors));
    }

    private static string? NormalizeVersionText(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        var separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0) normalized = normalized[..separator];
        return normalized.Trim();
    }

    private static bool TryParseComparableVersion(string value, out Version version)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized = NormalizeVersionText(value);
        if (normalized is null)
        {
            version = new Version(0, 0);
            return false;
        }

        return Version.TryParse(normalized, out version!);
    }
}

/// <summary>
/// Builds a deterministic maximal conflict-free installed setup. A candidate is accepted only when
/// its complete dependency/required-resource closure produces a non-blocking deployment plan.
/// Existing enabled choices get first priority; unresolved alternatives are skipped rather than guessed.
/// </summary>
public sealed class AutoPopulateService(
    PlannerSnapshotRepository plannerSnapshots,
    DeploymentPlanner planner,
    DependencyDoctorService dependencies,
    string gameRoot,
    GameProfile game)
{
    public async Task<AutoPopulateResult> BuildAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snapshot = await plannerSnapshots.LoadAsync(ct);
        var mods = snapshot.Mods.Where(m => !m.IsSuperseded).ToArray();
        var modsById = mods.ToDictionary(m => m.Id, PathRules.Comparer);
        var filesByMod = snapshot.Files
            .GroupBy(f => f.ModId, PathRules.Comparer)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ModFileDescriptor>)g.ToArray(), PathRules.Comparer);
        var providersByPath = snapshot.Files
            .Where(f => modsById.ContainsKey(f.ModId))
            .GroupBy(f => f.Path, PathRules.Comparer)
            .ToDictionary(g => g.Key, g => g.ToArray(), PathRules.Comparer);
        var contentStats = BuildContentStats(filesByMod);

        var requirements = new Dictionary<string,ModRequirementSpec>(PathRules.Comparer);
        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            requirements[mod.Id] = await ModRequirementReader.ReadAsync(
                mod,
                filesByMod.GetValueOrDefault(mod.Id) ?? [],
                game.IsMonsterHunterWorld,
                ct);
        }

        var selected = new HashSet<string>(PathRules.Comparer);
        var skipped = new Dictionary<string,AutoPopulateDecision>(PathRules.Comparer);
        var conflictSkips = 0;
        var requirementSkips = 0;

        var ordered = mods
            .OrderByDescending(m => m.Enabled)
            .ThenBy(RoleRank)
            .ThenByDescending(m => m.ProvenanceScore)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (selected.Contains(candidate.Id))
                continue;

            var closureResult = ResolveClosure(
                candidate.Id,
                selected,
                mods,
                modsById,
                filesByMod,
                providersByPath,
                snapshot.ResourceProviders,
                contentStats,
                requirements);

            if (!closureResult.Success)
            {
                requirementSkips++;
                skipped[candidate.Id] = new(candidate.Id, candidate.DisplayName, false, closureResult.Reason);
                continue;
            }

            var proposed = new HashSet<string>(selected, PathRules.Comparer);
            proposed.UnionWith(closureResult.ModIds);
            var plan = BuildPlan(snapshot, proposed);
            if (plan.IsBlocked)
            {
                conflictSkips++;
                var blocker = plan.Conflicts.First(x => x.Blocking);
                skipped[candidate.Id] = new(
                    candidate.Id,
                    candidate.DisplayName,
                    false,
                    $"Skipped because enabling its complete requirement chain would conflict: {blocker.Explanation}");
                continue;
            }

            selected.UnionWith(closureResult.ModIds);
        }

        // This should normally be prevented by ResolveClosure. Keep the final invariant strict if
        // a live-file requirement changes during the calculation. Removing one failed dependency can
        // invalidate another dependent, so converge to a fixed point instead of doing a single pass.
        while (true)
        {
            var stagedPlan = BuildPlan(snapshot, selected);
            if (stagedPlan.IsBlocked)
                throw new InvalidOperationException("Auto Populate invariant failed: dependency revalidation encountered a blocking file conflict.");
            var finalDependencyStatus = await dependencies.ScanStageAsync(selected, stagedPlan, ct);
            var failed = finalDependencyStatus.Where(x => !x.Ready).ToArray();
            if (failed.Length == 0)
                break;

            var removedAny = false;
            foreach (var status in failed)
            {
                if (!selected.Remove(status.ModId))
                    continue;
                removedAny = true;
                requirementSkips++;
                if (modsById.TryGetValue(status.ModId, out var mod))
                    skipped[status.ModId] = new(
                        status.ModId,
                        mod.DisplayName,
                        false,
                        "Skipped after final dependency validation: " + string.Join("; ", status.Missing));
            }

            if (!removedAny)
                throw new InvalidOperationException("Auto Populate dependency validation could not converge.");
        }

        var finalPlan = BuildPlan(snapshot, selected);
        if (finalPlan.IsBlocked)
            throw new InvalidOperationException("Auto Populate invariant failed: the final selected set contains a blocking conflict.");

        var state = snapshot.Mods.ToDictionary(
            m => m.Id,
            m => new ModState(selected.Contains(m.Id) && !m.IsSuperseded, m.Priority),
            PathRules.Comparer);

        var decisions = mods.Select(m =>
            selected.Contains(m.Id)
                ? new AutoPopulateDecision(m.Id, m.DisplayName, true, m.Enabled
                    ? "Kept enabled; its complete requirement chain is conflict-free."
                    : "Enabled; its complete requirement chain is conflict-free.")
                : skipped.GetValueOrDefault(m.Id) ??
                  new AutoPopulateDecision(m.Id, m.DisplayName, false, "Not selected because it was superseded by the completed safe set."))
            .OrderByDescending(x => x.Enabled)
            .ThenBy(x => x.ModName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary =
            $"Auto Populate selected {selected.Count} of {mods.Length} installed package(s); " +
            $"skipped {conflictSkips} for conflicts and {requirementSkips} for unsatisfied requirements.";

        return new(state, decisions, selected.Count, conflictSkips, requirementSkips, summary);
    }

    private ClosureResult ResolveClosure(
        string rootId,
        HashSet<string> alreadySelected,
        ModDescriptor[] mods,
        Dictionary<string,ModDescriptor> modsById,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod,
        Dictionary<string,ModFileDescriptor[]> providersByPath,
        IReadOnlyDictionary<string,string> resourceProviders,
        Dictionary<string,ModContentStats> contentStats,
        Dictionary<string,ModRequirementSpec> requirements)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var closure = new HashSet<string>(PathRules.Comparer);
        var queue = new Queue<string>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (alreadySelected.Contains(id) || !closure.Add(id))
                continue;
            if (!modsById.TryGetValue(id, out var mod))
                return ClosureResult.Fail($"Required installed mod '{id}' is unavailable.");
            if (!requirements.TryGetValue(id, out var spec))
                return ClosureResult.Fail($"Requirements for '{mod.DisplayName}' could not be read.");
            if (spec.Errors.Count > 0)
                return ClosureResult.Fail($"'{mod.DisplayName}' has invalid requirements: {string.Join("; ", spec.Errors)}");

            foreach (var requirement in spec.ModRequirements.Where(x => x.Required))
            {
                var dependency = ChooseModTokenProvider(requirement, mods);
                if (dependency is null)
                    return ClosureResult.Fail($"'{mod.DisplayName}' requires {ModRequirementReader.DescribeRequirement(requirement)}, but no installed package satisfies that constraint.");
                queue.Enqueue(dependency.Id);
            }

            var inferredMain = FindRequiredMain(mod, mods);
            if (inferredMain is not null)
                queue.Enqueue(inferredMain.Id);

            foreach (var path in spec.RequiredPaths.Concat(spec.RequiredTexturePaths).Distinct(PathRules.Comparer))
            {
                if (IsPathSatisfiedBySelection(path, alreadySelected, closure, filesByMod))
                    continue;

                var provider = ChoosePathProvider(path, providersByPath, modsById, contentStats, resourceProviders);
                if (provider is not null)
                {
                    queue.Enqueue(provider.Id);
                    continue;
                }

                if (!File.Exists(ModRequirementReader.LivePath(gameRoot, path)))
                    return ClosureResult.Fail($"'{mod.DisplayName}' requires '{path}', but no unambiguous installed provider or live game file is available.");
            }

            if (spec.RequiresNativeLoader &&
                !HasLiveLoader() &&
                !HasSelectedLoader(alreadySelected, closure, filesByMod))
            {
                var loader = ChooseLoaderProvider(mods, filesByMod);
                if (loader is null)
                    return ClosureResult.Fail($"'{mod.DisplayName}' requires the MHW native plugin loader, but no loader is installed or available as a tracked package.");
                queue.Enqueue(loader.Id);
            }

            if (filesByMod.TryGetValue(id, out var modFiles))
            {
                foreach (var file in modFiles.Where(f => f.FileClass == FileClass.Texture))
                {
                    var resourceNamespace = PathRules.ResourceNamespace(file.Path);
                    if (resourceNamespace is null ||
                        !resourceProviders.TryGetValue(resourceNamespace, out var pinnedId) ||
                        PathRules.Comparer.Equals(pinnedId, id) ||
                        alreadySelected.Contains(pinnedId) ||
                        closure.Contains(pinnedId))
                        continue;
                    if (!modsById.ContainsKey(pinnedId))
                        return ClosureResult.Fail($"Pinned texture provider '{pinnedId}' for '{resourceNamespace}' is not installed.");
                    queue.Enqueue(pinnedId);
                }
            }
        }

        return ClosureResult.Ok(closure);
    }

    private DeploymentPlan BuildPlan(PlannerSnapshot snapshot, HashSet<string> selected)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var staged = snapshot.Mods
            .Select(m => m with { Enabled = selected.Contains(m.Id) && !m.IsSuperseded })
            .ToArray();
        return planner.Build(snapshot with { Mods = staged });
    }

    private bool HasLiveLoader()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return File.Exists(Path.Combine(gameRoot, "dinput8.dll")) ||
               File.Exists(Path.Combine(gameRoot, "loader.dll"));
    }

    private static bool HasSelectedLoader(
        HashSet<string> selected,
        HashSet<string> closure,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return selected.Concat(closure).Any(id =>
            filesByMod.TryGetValue(id, out var files) && files.Any(f => ModRequirementReader.IsLoaderPath(f.Path)));
    }

    private static ModDescriptor? ChooseLoaderProvider(
        ModDescriptor[] mods,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return mods.Where(m => filesByMod.TryGetValue(m.Id, out var files) && files.Any(f => ModRequirementReader.IsLoaderPath(f.Path)))
            .OrderByDescending(m => m.Enabled)
            .ThenBy(RoleRank)
            .ThenByDescending(m => m.ProvenanceScore)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsPathSatisfiedBySelection(
        string path,
        HashSet<string> selected,
        HashSet<string> closure,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return selected.Concat(closure).Any(id =>
            filesByMod.TryGetValue(id, out var files) &&
            files.Any(f => PathRules.Comparer.Equals(f.Path, path)));
    }

    private static ModDescriptor? ChooseModTokenProvider(ModDependencyRequirement requirement, ModDescriptor[] mods)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return mods.Where(m =>
                ModRequirementReader.MatchesToken(m, requirement.Token) &&
                !m.IsSuperseded &&
                ModRequirementReader.VersionSatisfies(m, requirement, out _))
            .OrderBy(RoleRank)
            .ThenByDescending(m => m.Enabled)
            .ThenByDescending(m => m.ProvenanceScore)
            .ThenByDescending(m => m.NexusUploadedAt)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static ModDescriptor? FindRequiredMain(ModDescriptor mod, ModDescriptor[] mods)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!IsDependentRole(mod))
            return null;

        var related = mods.Where(candidate =>
                !candidate.IsSuperseded &&
                !PathRules.Comparer.Equals(candidate.Id, mod.Id) &&
                ((!string.IsNullOrWhiteSpace(mod.FamilyId) &&
                  StringComparer.OrdinalIgnoreCase.Equals(candidate.FamilyId, mod.FamilyId)) ||
                 (!string.IsNullOrWhiteSpace(mod.NexusModUuid) &&
                  StringComparer.OrdinalIgnoreCase.Equals(candidate.NexusModUuid, mod.NexusModUuid)) ||
                 (!string.IsNullOrWhiteSpace(mod.NexusModId) &&
                  StringComparer.OrdinalIgnoreCase.Equals(candidate.NexusModId, mod.NexusModId))))
            .Where(IsMainRole)
            .OrderByDescending(m => m.Enabled)
            .ThenByDescending(m => m.ProvenanceScore)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return related.FirstOrDefault();
    }

    private static bool IsDependentRole(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return mod.NexusCategory is NexusFileCategory.Optional or NexusFileCategory.Update ||
               ContainsRole(mod.FamilyRole, "optional", "update", "patch", "addon", "add-on", "child");
    }

    private static bool IsMainRole(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return mod.NexusCategory == NexusFileCategory.Main ||
               ContainsRole(mod.FamilyRole, "main", "base", "core", "root");
    }

    private static bool ContainsRole(string? value, params string[] roles)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return !string.IsNullOrWhiteSpace(value) &&
               roles.Any(role => value.Contains(role, StringComparison.OrdinalIgnoreCase));
    }

    private static int RoleRank(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (IsMainRole(mod))
            return 0;
        if (IsDependentRole(mod))
            return 2;
        return 1;
    }

    private static ModDescriptor? ChoosePathProvider(
        string path,
        Dictionary<string,ModFileDescriptor[]> providersByPath,
        Dictionary<string,ModDescriptor> modsById,
        Dictionary<string,ModContentStats> contentStats,
        IReadOnlyDictionary<string,string> resourceProviders)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!providersByPath.TryGetValue(path, out var providerFiles) || providerFiles.Length == 0)
            return null;

        var providers = providerFiles
            .Where(f => modsById.ContainsKey(f.ModId))
            .Select(f => new ProviderCandidate(f.ModId, modsById[f.ModId].DisplayName, modsById[f.ModId].Priority, f))
            .ToArray();
        if (providers.Length == 0)
            return null;

        var resourceNamespace = PathRules.ResourceNamespace(path);
        if (resourceNamespace is not null &&
            resourceProviders.TryGetValue(resourceNamespace, out var pinned) &&
            providers.Any(p => PathRules.Comparer.Equals(p.ModId, pinned)))
            return modsById[pinned];

        if (providers.Select(p => p.File.BlobSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            return providers.Select(p => modsById[p.ModId])
                .OrderByDescending(m => m.Enabled)
                .ThenBy(RoleRank)
                .ThenByDescending(m => m.Priority)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .First();

        if (PathRules.ClassifyFile(path) != FileClass.Texture)
            return providers.Length == 1 ? modsById[providers[0].ModId] : null;

        var selection = AutoCompatibility.SelectTextureProvider(path, providers, modsById, contentStats);
        return selection.WinnerModId is not null && modsById.TryGetValue(selection.WinnerModId, out var winner)
            ? winner
            : null;
    }

    private static Dictionary<string,ModContentStats> BuildContentStats(
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return filesByMod.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var files = pair.Value;
                return new ModContentStats(
                    files.Count,
                    files.Count(f => f.FileClass == FileClass.Texture),
                    files.Count(f => f.FileClass == FileClass.Structural),
                    files.Count(f => f.FileClass is FileClass.GameData or FileClass.Plugin or FileClass.Executable),
                    files.Count == 0 ? DateTimeOffset.MinValue : files.Max(f => f.LastWriteUtc));
            },
            PathRules.Comparer);
    }

    private sealed record ClosureResult(bool Success, IReadOnlySet<string> ModIds, string Reason)
    {
        public static ClosureResult Ok(HashSet<string> ids)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new(true, ids, string.Empty);
        }

        public static ClosureResult Fail(string reason)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new(false, new HashSet<string>(PathRules.Comparer), reason);
        }
    }
}
