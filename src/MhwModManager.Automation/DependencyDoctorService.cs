using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class DependencyDoctorService(ManagerDatabase db, string gameRoot, GameProfile? game = null)
{
    public async Task<IReadOnlyList<DependencyStatus>> ScanAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var enabled = mods.Where(x => x.Enabled && !x.IsSuperseded).Select(x => x.Id).ToHashSet(PathRules.Comparer);
        return await ScanStageAsync(enabled, ct);
    }

    /// <summary>
    /// Validates a prospective enabled set without touching the live game or persisted mod state.
    /// Auto Populate uses this to prove that dependencies/resources remain satisfied before apply.
    /// </summary>
    public async Task<IReadOnlyList<DependencyStatus>> ScanStageAsync(IReadOnlySet<string> enabledModIds, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(enabledModIds);

        var mods = await db.GetModsAsync(ct);
        var modsById = mods.ToDictionary(x => x.Id, PathRules.Comparer);
        var files = await db.GetModFilesAsync(ct);
        var filesByMod = files
            .GroupBy(x => x.ModId, PathRules.Comparer)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ModFileDescriptor>)g.ToArray(), PathRules.Comparer);
        var currentManifest = (await new PlannerSnapshotRepository(db).LoadAsync([], ct)).CurrentManifest;

        // A staged identity that no longer exists (or is superseded) is itself an unsatisfied
        // dependency. Never silently drop it from validation; stale profiles must fail closed.
        var unavailable = enabledModIds
            .Where(id => !modsById.TryGetValue(id, out var mod) || mod.IsSuperseded)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var results = unavailable
            .Select(id => new DependencyStatus(
                id,
                modsById.TryGetValue(id, out var mod) ? mod.DisplayName : id,
                false,
                [$"Selected mod '{id}' is unavailable or superseded."],
                ["The staged enabled set referenced a package that cannot participate in deployment."]))
            .ToList();

        var selected = enabledModIds
            .Where(id => modsById.TryGetValue(id, out var mod) && !mod.IsSuperseded)
            .ToHashSet(PathRules.Comparer);

        foreach (var id in selected.Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!modsById.TryGetValue(id, out var mod))
                continue;

            var spec = await ModRequirementReader.ReadAsync(
                mod,
                filesByMod.GetValueOrDefault(id) ?? [],
                game is null || game.IsMonsterHunterWorld,
                ct);
            var missing = new List<string>(spec.Errors);
            var evidence = new List<string>(spec.Evidence);

            if (spec.RequiresNativeLoader && !LoaderWillRemainAvailable(selected, filesByMod, currentManifest))
                missing.Add("Stracker/native plugin loader (no unmanaged loader or selected tracked loader will remain after this staged deployment)");

            foreach (var token in spec.RequiredModTokens)
            {
                var installed = mods.Where(candidate => !candidate.IsSuperseded && ModRequirementReader.MatchesToken(candidate, token)).ToArray();
                if (installed.Length == 0)
                {
                    missing.Add($"Required mod '{token}' is not installed.");
                    continue;
                }

                if (!installed.Any(candidate => selected.Contains(candidate.Id)))
                    missing.Add($"Required mod '{token}' is installed but not enabled.");
            }

            foreach (var requiredPath in spec.RequiredPaths.Concat(spec.RequiredTexturePaths).Distinct(PathRules.Comparer))
            {
                if (SelectedProvidesPath(requiredPath, selected, filesByMod))
                    continue;

                // A currently-live manager-owned file is not a valid dependency if its provider is
                // absent from the staged set: the deployment plan is about to remove/restore it.
                // Only unmanaged/base-game live files may satisfy an otherwise-unprovided path.
                if (currentManifest.ContainsKey(requiredPath))
                {
                    missing.Add(requiredPath + " (currently supplied only by a managed provider that is not enabled in the staged set)");
                    continue;
                }

                if (File.Exists(ModRequirementReader.LivePath(gameRoot, requiredPath)))
                    continue;
                missing.Add(requiredPath);
            }

            if (missing.Count > 0 || evidence.Count > 0)
                results.Add(new(mod.Id, mod.DisplayName, missing.Count == 0, missing, evidence));
        }

        return results;
    }

    private bool LoaderWillRemainAvailable(
        HashSet<string> selected,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod,
        IReadOnlyDictionary<string,DeploymentManifestEntry> currentManifest)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (SelectedProvidesLoader(selected, filesByMod))
            return true;

        return LiveLoaderIsUnmanaged(@"root\dinput8.dll", currentManifest) ||
               LiveLoaderIsUnmanaged(@"root\loader.dll", currentManifest);
    }

    private bool LiveLoaderIsUnmanaged(
        string relativePath,
        IReadOnlyDictionary<string,DeploymentManifestEntry> currentManifest)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return !currentManifest.ContainsKey(relativePath) &&
               File.Exists(ModRequirementReader.LivePath(gameRoot, relativePath));
    }

    private static bool SelectedProvidesLoader(
        HashSet<string> selected,
        Dictionary<string,IReadOnlyList<ModFileDescriptor>> filesByMod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return selected.Any(id =>
            filesByMod.TryGetValue(id, out var files) &&
            files.Any(f => ModRequirementReader.IsLoaderPath(f.Path)));
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
}
