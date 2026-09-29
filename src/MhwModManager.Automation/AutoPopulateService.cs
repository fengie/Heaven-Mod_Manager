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

internal sealed record ModRequirementSpec(
    string ModId,
    IReadOnlyList<string> RequiredModTokens,
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
        var requiredMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                ReadDependencyArray(root, "mods", requiredMods);
                ReadDependencyArray(root, "dependencies", requiredMods);
                ReadDependencyArray(root, "requires", requiredMods);

                if (requiredPaths.Count > 0)
                    evidence.Add($"Explicit sidecar file requirements: {string.Join(", ", requiredPaths)}");
                if (requiredTextures.Count > 0)
                    evidence.Add($"Explicit sidecar texture requirements: {string.Join(", ", requiredTextures)}");
                if (requiredMods.Count > 0)
                    evidence.Add($"Explicit sidecar mod requirements: {string.Join(", ", requiredMods)}");
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
            requiredMods.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            requiredPaths.Order(PathRules.Comparer).ToArray(),
            requiredTextures.Order(PathRules.Comparer).ToArray(),
            requiresLoader,
            evidence,
            errors);
    }

    public static bool MatchesToken(ModDescriptor mod, string token)
    {
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
        var relative = normalized.StartsWith("root\\", StringComparison.OrdinalIgnoreCase)
            ? normalized["root\\".Length..]
            : normalized;
        return Path.Combine(gameRoot, relative.Replace('\\', Path.DirectorySeparatorChar));
    }

    public static bool IsLoaderPath(string path)
    {
        string normalized;
        try { normalized = PathRules.Normalize(path); }
        catch (ArgumentException) { return false; }

        return normalized.Equals(@"root\dinput8.dll", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(@"root\loader.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadPathArray(JsonElement root, string propertyName, HashSet<string> output, List<string> errors)
    {
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
        var normalized = value.Replace('/', '\\').Trim();
        if (!normalized.StartsWith("nativePC\\", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("root\\", StringComparison.OrdinalIgnoreCase))
            normalized = "root\\" + normalized.TrimStart('\\');
        return PathRules.Normalize(normalized);
    }

    private static void ReadDependencyArray(JsonElement root, string propertyName, HashSet<string> output)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var token = item.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                    output.Add(token.Trim());
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (item.TryGetProperty("required", out var required) &&
                required.ValueKind is JsonValueKind.False)
                continue;

            foreach (var key in new[] { "modId", "id", "nexusModId", "nexusModUuid" })
            {
                if (!item.TryGetProperty(key, out var tokenValue) || tokenValue.ValueKind != JsonValueKind.String)
                    continue;
                var token = tokenValue.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    output.Add(token.Trim());
                    break;
                }
            }
        }
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

        var finalDependencyStatus = await dependencies.ScanStageAsync(selected, ct);
        var failedIds = finalDependencyStatus
            .Where(x => !x.Ready)
            .Select(x => x.ModId)
            .ToHashSet(PathRules.Comparer);

        if (failedIds.Count > 0)
        {
            // This should normally be prevented by ResolveClosure. Keep the final invariant strict if
            // a live-file requirement changes during the calculation.
            foreach (var id in failedIds)
            {
                if (!modsById.TryGetValue(id, out var mod))
                    continue;
                selected.Remove(id);
                requirementSkips++;
                var status = finalDependencyStatus.First(x => PathRules.Comparer.Equals(x.ModId, id));
                skipped[id] = new(id, mod.DisplayName, false, "Skipped after final dependency validation: " + string.Join("; ", status.Missing));
            }

            // Dependency removal can invalidate dependents. Rebuild conservatively from the survivors
            // and only keep packages that still validate as a group.
            var survivorStatus = await dependencies.ScanStageAsync(selected, ct);
            foreach (var status in survivorStatus.Where(x => !x.Ready))
            {
                selected.Remove(status.ModId);
                if (modsById.TryGetValue(status.ModId, out var mod))
                    skipped[status.ModId] = new(status.ModId, mod.DisplayName, false, "Skipped after dependency closure changed: " + string.Join("; ", status.Missing));
            }
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
        IReadOnlySet<string> alreadySelected,
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyDictionary<string,ModDescriptor> modsById,
        IReadOnlyDictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod,
        IReadOnlyDictionary<string,ModFileDescriptor[]> providersByPath,
        IReadOnlyDictionary<string,string> resourceProviders,
        IReadOnlyDictionary<string,ModContentStats> contentStats,
        IReadOnlyDictionary<string,ModRequirementSpec> requirements)
    {
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

            foreach (var token in spec.RequiredModTokens)
            {
                var dependency = ChooseModTokenProvider(token, mods);
                if (dependency is null)
                    return ClosureResult.Fail($"'{mod.DisplayName}' requires installed mod '{token}', but no matching package is installed.");
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

    private DeploymentPlan BuildPlan(PlannerSnapshot snapshot, IReadOnlySet<string> selected)
    {
        var staged = snapshot.Mods
            .Select(m => m with { Enabled = selected.Contains(m.Id) && !m.IsSuperseded })
            .ToArray();
        return planner.Build(snapshot with { Mods = staged });
    }

    private bool HasLiveLoader() =>
        File.Exists(Path.Combine(gameRoot, "dinput8.dll")) ||
        File.Exists(Path.Combine(gameRoot, "loader.dll"));

    private static bool HasSelectedLoader(
        IReadOnlySet<string> selected,
        IReadOnlySet<string> closure,
        IReadOnlyDictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod) =>
        selected.Concat(closure).Any(id =>
            filesByMod.TryGetValue(id, out var files) && files.Any(f => ModRequirementReader.IsLoaderPath(f.Path)));

    private static ModDescriptor? ChooseLoaderProvider(
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyDictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod) =>
        mods.Where(m => filesByMod.TryGetValue(m.Id, out var files) && files.Any(f => ModRequirementReader.IsLoaderPath(f.Path)))
            .OrderByDescending(m => m.Enabled)
            .ThenBy(RoleRank)
            .ThenByDescending(m => m.ProvenanceScore)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static bool IsPathSatisfiedBySelection(
        string path,
        IReadOnlySet<string> selected,
        IReadOnlySet<string> closure,
        IReadOnlyDictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod) =>
        selected.Concat(closure).Any(id =>
            filesByMod.TryGetValue(id, out var files) &&
            files.Any(f => PathRules.Comparer.Equals(f.Path, path)));

    private static ModDescriptor? ChooseModTokenProvider(string token, IReadOnlyList<ModDescriptor> mods) =>
        mods.Where(m => ModRequirementReader.MatchesToken(m, token) && !m.IsSuperseded)
            .OrderBy(RoleRank)
            .ThenByDescending(m => m.Enabled)
            .ThenByDescending(m => m.ProvenanceScore)
            .ThenByDescending(m => m.NexusUploadedAt)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static ModDescriptor? FindRequiredMain(ModDescriptor mod, IReadOnlyList<ModDescriptor> mods)
    {
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

    private static bool IsDependentRole(ModDescriptor mod) =>
        mod.NexusCategory is NexusFileCategory.Optional or NexusFileCategory.Update ||
        ContainsRole(mod.FamilyRole, "optional", "update", "patch", "addon", "add-on", "child");

    private static bool IsMainRole(ModDescriptor mod) =>
        mod.NexusCategory == NexusFileCategory.Main ||
        ContainsRole(mod.FamilyRole, "main", "base", "core", "root");

    private static bool ContainsRole(string? value, params string[] roles) =>
        !string.IsNullOrWhiteSpace(value) &&
        roles.Any(role => value.Contains(role, StringComparison.OrdinalIgnoreCase));

    private static int RoleRank(ModDescriptor mod)
    {
        if (IsMainRole(mod))
            return 0;
        if (IsDependentRole(mod))
            return 2;
        return 1;
    }

    private static ModDescriptor? ChoosePathProvider(
        string path,
        IReadOnlyDictionary<string,ModFileDescriptor[]> providersByPath,
        IReadOnlyDictionary<string,ModDescriptor> modsById,
        IReadOnlyDictionary<string,ModContentStats> contentStats,
        IReadOnlyDictionary<string,string> resourceProviders)
    {
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

    private static IReadOnlyDictionary<string,ModContentStats> BuildContentStats(
        IReadOnlyDictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod) =>
        filesByMod.ToDictionary(
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

    private sealed record ClosureResult(bool Success, IReadOnlySet<string> ModIds, string Reason)
    {
        public static ClosureResult Ok(IReadOnlySet<string> ids) => new(true, ids, string.Empty);
        public static ClosureResult Fail(string reason) => new(false, new HashSet<string>(PathRules.Comparer), reason);
    }
}
