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

        var selected = enabledModIds
            .Where(id => modsById.TryGetValue(id, out var mod) && !mod.IsSuperseded)
            .ToHashSet(PathRules.Comparer);
        var results = new List<DependencyStatus>();

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

            if (spec.RequiresNativeLoader && !HasLoader() && !SelectedProvidesLoader(selected, filesByMod))
                missing.Add("Stracker/native plugin loader (no dinput8.dll or loader.dll is enabled or present in the game root)");

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
                if (File.Exists(ModRequirementReader.LivePath(gameRoot, requiredPath)))
                    continue;
                missing.Add(requiredPath);
            }

            if (missing.Count > 0 || evidence.Count > 0)
                results.Add(new(mod.Id, mod.DisplayName, missing.Count == 0, missing, evidence));
        }

        return results;
    }

    private bool HasLoader()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return File.Exists(Path.Combine(gameRoot, "dinput8.dll")) ||
               File.Exists(Path.Combine(gameRoot, "loader.dll"));
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
