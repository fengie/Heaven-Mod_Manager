using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class DependencyDoctorService(ManagerDatabase db, string gameRoot, GameProfile? game = null)
{
    private const string LoaderProxyPath = @"root\dinput8.dll";
    private const string LoaderCorePath = @"root\loader.dll";
    private const string LoaderConfigPath = @"root\loader-config.json";

    public async Task<IReadOnlyList<DependencyStatus>> ScanAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var enabled = mods.Where(x => x.Enabled && !x.IsSuperseded).Select(x => x.Id).ToHashSet(PathRules.Comparer);
        return await ScanStageAsync(enabled, null, ct);
    }

    /// <summary>
    /// Validates a prospective enabled set without touching the live game or persisted mod state.
    /// This overload preserves source-level validation for callers that do not yet have a deployment
    /// plan. Apply/Auto Populate should prefer the plan-aware overload below.
    /// </summary>
    public Task<IReadOnlyList<DependencyStatus>> ScanStageAsync(
        IReadOnlySet<string> enabledModIds,
        CancellationToken ct = default) =>
        ScanStageAsync(enabledModIds, null, ct);

    /// <summary>
    /// Validates hard/optional dependency constraints and required resources against the final
    /// effective filesystem described by <paramref name="effectivePlan"/>. Presence in a source
    /// package is not sufficient: a required path must survive conflict resolution as an effective
    /// provider before deployment is considered safe.
    /// </summary>
    public async Task<IReadOnlyList<DependencyStatus>> ScanStageAsync(
        IReadOnlySet<string> enabledModIds,
        DeploymentPlan? effectivePlan,
        CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(enabledModIds);

        var mods = await db.GetModsAsync(ct);
        var modsById = mods.ToDictionary(x => x.Id, PathRules.Comparer);
        var files = await db.GetModFilesAsync(ct);
        var filesByMod = files
            .GroupBy(x => x.ModId, PathRules.Comparer)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ModFileDescriptor>)g.ToArray(), PathRules.Comparer);

        var selected = enabledModIds
            .Where(id => modsById.TryGetValue(id, out var mod) && !mod.IsSuperseded)
            .ToHashSet(PathRules.Comparer);

        var specs = new Dictionary<string,ModRequirementSpec>(PathRules.Comparer);
        foreach (var id in selected.Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var mod = modsById[id];
            specs[id] = await ModRequirementReader.ReadAsync(
                mod,
                filesByMod.GetValueOrDefault(id) ?? [],
                game is null || game.IsMonsterHunterWorld,
                ct);
        }

        var cycleEvidence = BuildCycleEvidence(selected, mods, specs);
        var results = new List<DependencyStatus>();

        foreach (var id in selected.Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var mod = modsById[id];
            var spec = specs[id];
            var missing = new List<string>(spec.Errors);
            var evidence = new List<string>(spec.Evidence);

            if (cycleEvidence.TryGetValue(id, out var cycles))
                evidence.AddRange(cycles);

            if (spec.RequiresNativeLoader &&
                !HasEffectiveLoader(selected, filesByMod, effectivePlan, out var loaderEvidence))
            {
                missing.Add(loaderEvidence);
            }
            else if (spec.RequiresNativeLoader && !string.IsNullOrWhiteSpace(loaderEvidence))
            {
                evidence.Add(loaderEvidence);
            }

            foreach (var requirement in spec.ModRequirements)
            {
                var identityMatches = mods
                    .Where(candidate =>
                        !candidate.IsSuperseded &&
                        ModRequirementReader.MatchesToken(candidate, requirement.Token))
                    .ToArray();

                var compatible = identityMatches
                    .Where(candidate => ModRequirementReader.VersionSatisfies(candidate, requirement, out _))
                    .ToArray();
                var selectedCompatible = compatible.Where(candidate => selected.Contains(candidate.Id)).ToArray();

                if (!requirement.Required)
                {
                    if (selectedCompatible.Length > 0)
                        evidence.Add($"Optional dependency active: {ModRequirementReader.DescribeRequirement(requirement)} via '{selectedCompatible.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).First().DisplayName}'.");
                    else
                        evidence.Add($"Optional dependency not active: {ModRequirementReader.DescribeRequirement(requirement)}. This does not block deployment.");
                    continue;
                }

                if (identityMatches.Length == 0)
                {
                    missing.Add($"Required mod '{requirement.Token}' is not installed.");
                    continue;
                }

                if (compatible.Length == 0)
                {
                    var reasons = identityMatches
                        .Select(candidate =>
                        {
                            ModRequirementReader.VersionSatisfies(candidate, requirement, out var reason);
                            return reason;
                        })
                        .Where(reason => !string.IsNullOrWhiteSpace(reason))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    missing.Add($"No installed package satisfies {ModRequirementReader.DescribeRequirement(requirement)}. {string.Join(" ", reasons)}");
                    continue;
                }

                if (selectedCompatible.Length == 0)
                    missing.Add($"Required dependency {ModRequirementReader.DescribeRequirement(requirement)} is installed but no compatible provider is enabled.");
            }

            foreach (var requiredPath in spec.RequiredPaths.Concat(spec.RequiredTexturePaths).Distinct(PathRules.Comparer))
            {
                if (effectivePlan is not null)
                {
                    var decision = effectivePlan.Conflicts.FirstOrDefault(x => PathRules.Comparer.Equals(x.Path, requiredPath));
                    if (decision is not null)
                    {
                        if (decision.Blocking || string.IsNullOrWhiteSpace(decision.WinnerModId))
                        {
                            missing.Add($"Required path '{requiredPath}' has no safe effective provider after conflict resolution.");
                            continue;
                        }

                        if (!selected.Contains(decision.WinnerModId))
                        {
                            missing.Add($"Required path '{requiredPath}' resolves to provider '{decision.WinnerModId}', which is not enabled.");
                            continue;
                        }

                        evidence.Add($"Effective required path: {requiredPath} <- {decision.WinnerModId} ({decision.ReasonCode}).");
                        continue;
                    }
                }

                if (SelectedProvidesPath(requiredPath, selected, filesByMod))
                    continue;
                if (File.Exists(ModRequirementReader.LivePath(gameRoot, requiredPath)))
                    continue;
                missing.Add(requiredPath);
            }

            if (missing.Count > 0 || evidence.Count > 0)
                results.Add(new(mod.Id, mod.DisplayName, missing.Count == 0, missing, evidence));
        }

        return results;
    }

    private bool HasEffectiveLoader(
        HashSet<string> selected,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod,
        DeploymentPlan? effectivePlan,
        out string detail)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        detail = string.Empty;

        if (effectivePlan is null)
        {
            if (HasCompleteLiveLoader())
            {
                detail = "A complete native loader pair is present in the game root.";
                return true;
            }

            var provider = FindCompleteSelectedLoaderProvider(selected, filesByMod);
            if (provider is not null)
            {
                detail = $"Selected loader package '{provider}' provides both dinput8.dll and loader.dll.";
                return true;
            }

            detail = "Stracker/native plugin loader is incomplete: deployment requires a compatible dinput8.dll + loader.dll pair from one provider, or an already complete loader in the game root.";
            return false;
        }

        var proxyWinner = EffectiveWinner(effectivePlan, LoaderProxyPath);
        var coreWinner = EffectiveWinner(effectivePlan, LoaderCorePath);
        if (proxyWinner is null && coreWinner is null)
        {
            if (HasCompleteLiveLoader())
            {
                detail = "Existing game-root dinput8.dll + loader.dll satisfy the native loader capability.";
                return true;
            }

            detail = "Stracker/native plugin loader is missing from the effective plan and game root.";
            return false;
        }

        if (proxyWinner is null || coreWinner is null)
        {
            detail = "Native loader preflight found a partial effective loader: dinput8.dll and loader.dll must survive together.";
            return false;
        }

        if (!PathRules.Comparer.Equals(proxyWinner, coreWinner))
        {
            detail = $"Native loader preflight would mix bootstrap binaries from '{proxyWinner}' and '{coreWinner}'. Loader generations must come from one effective provider.";
            return false;
        }

        if (!selected.Contains(proxyWinner))
        {
            detail = $"Native loader effective provider '{proxyWinner}' is not enabled.";
            return false;
        }

        if (filesByMod.TryGetValue(proxyWinner, out var providerFiles) &&
            providerFiles.Any(f => PathRules.Comparer.Equals(f.Path, LoaderConfigPath)))
        {
            var configDecision = effectivePlan.Conflicts.FirstOrDefault(x => PathRules.Comparer.Equals(x.Path, LoaderConfigPath));
            if (configDecision is not null &&
                !configDecision.Blocking &&
                configDecision.WinnerModId is not null &&
                !PathRules.Comparer.Equals(configDecision.WinnerModId, proxyWinner) &&
                configDecision.Kind != ConflictKind.Identical)
            {
                detail = $"Native loader config would be supplied by '{configDecision.WinnerModId}' while loader binaries come from '{proxyWinner}'.";
                return false;
            }
        }

        detail = $"Effective native loader is internally consistent: dinput8.dll + loader.dll <- {proxyWinner}.";
        return true;
    }

    private bool HasCompleteLiveLoader()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return File.Exists(Path.Combine(gameRoot, "dinput8.dll")) &&
               File.Exists(Path.Combine(gameRoot, "loader.dll"));
    }

    private static string? FindCompleteSelectedLoaderProvider(
        HashSet<string> selected,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var id in selected.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!filesByMod.TryGetValue(id, out var files))
                continue;
            var hasProxy = files.Any(f => PathRules.Comparer.Equals(f.Path, LoaderProxyPath));
            var hasCore = files.Any(f => PathRules.Comparer.Equals(f.Path, LoaderCorePath));
            if (hasProxy && hasCore)
                return id;
        }
        return null;
    }

    private static string? EffectiveWinner(DeploymentPlan plan, string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var decision = plan.Conflicts.FirstOrDefault(x => PathRules.Comparer.Equals(x.Path, path));
        return decision is { Blocking: false, WinnerModId: not null } ? decision.WinnerModId : null;
    }

    private static bool SelectedProvidesPath(
        string path,
        HashSet<string> selected,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return selected.Any(id =>
            filesByMod.TryGetValue(id, out var files) &&
            files.Any(f => PathRules.Comparer.Equals(f.Path, path)));
    }

    private static Dictionary<string,List<string>> BuildCycleEvidence(
        HashSet<string> selected,
        IReadOnlyList<ModDescriptor> mods,
        IReadOnlyDictionary<string,ModRequirementSpec> specs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var graph = selected.ToDictionary(id => id, _ => new List<string>(), PathRules.Comparer);
        foreach (var id in selected.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!specs.TryGetValue(id, out var spec))
                continue;

            foreach (var requirement in spec.ModRequirements.Where(x => x.Required))
            {
                var target = mods
                    .Where(candidate =>
                        selected.Contains(candidate.Id) &&
                        !candidate.IsSuperseded &&
                        ModRequirementReader.MatchesToken(candidate, requirement.Token) &&
                        ModRequirementReader.VersionSatisfies(candidate, requirement, out _))
                    .OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (target is not null)
                    graph[id].Add(target.Id);
            }
        }

        var index = 0;
        var indexes = new Dictionary<string,int>(PathRules.Comparer);
        var low = new Dictionary<string,int>(PathRules.Comparer);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(PathRules.Comparer);
        var components = new List<string[]>();

        void StrongConnect(string node)
        {
            indexes[node] = index;
            low[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var next in graph[node].Distinct(PathRules.Comparer).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!indexes.ContainsKey(next))
                {
                    StrongConnect(next);
                    low[node] = Math.Min(low[node], low[next]);
                }
                else if (onStack.Contains(next))
                {
                    low[node] = Math.Min(low[node], indexes[next]);
                }
            }

            if (low[node] != indexes[node])
                return;

            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            }
            while (!PathRules.Comparer.Equals(current, node));

            var selfCycle = component.Count == 1 && graph[component[0]].Any(x => PathRules.Comparer.Equals(x, component[0]));
            if (component.Count > 1 || selfCycle)
                components.Add(component.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        foreach (var node in graph.Keys.Order(StringComparer.OrdinalIgnoreCase))
            if (!indexes.ContainsKey(node))
                StrongConnect(node);

        var evidence = new Dictionary<string,List<string>>(PathRules.Comparer);
        foreach (var component in components.OrderBy(x => string.Join("|", x), StringComparer.OrdinalIgnoreCase))
        {
            var message = $"Hard dependency cycle detected and validated as one strongly connected group: {string.Join(" <-> ", component)}. Every edge must remain version-compatible and enabled.";
            foreach (var id in component)
            {
                if (!evidence.TryGetValue(id, out var list))
                    evidence[id] = list = [];
                list.Add(message);
            }
        }

        return evidence;
    }
}
