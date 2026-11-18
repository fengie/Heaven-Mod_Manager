using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class AutoCategoryService(ManagerDatabase db, GameProfile? game = null, StartupDiagnosticSession? startupDiagnostics = null)
{
    public static AutomationCategory ClassifyPaths(IEnumerable<string> paths, bool includeMhwSemantics = true)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var scores = new Dictionary<AutomationCategory, int>();
        void Add(AutomationCategory category, int score) => scores[category] = scores.GetValueOrDefault(category) + score;
        foreach (var raw in paths)
        {
            if (MasterDebugLog.DetailedDiagnosticsEnabled)
                UnifiedDebugLog.Write("AUTOCAT-PATH", $"Inspect: {raw}");
            // Classification also runs against raw source-folder contents before a mod has
            // been captured into the managed nativePC/root namespace. Root-level readmes,
            // troubleshooting notes, screenshots, and similar metadata are legitimate here.
            // Reject genuinely unsafe relative paths, but do not require a deployable prefix.
            if (!PathRules.IsSafeArchiveRelativePath(raw))
            {
                if (MasterDebugLog.DetailedDiagnosticsEnabled)
                    UnifiedDebugLog.Write("AUTOCAT-PATH", $"Rejected unsafe/non-relative classification input: {raw}");
                continue;
            }
            var p = raw.Replace('/', '\\').Trim().ToLowerInvariant();
            var ext = Path.GetExtension(p);
            if (ext is ".tex" or ".dds" or ".png" or ".tga") Add(AutomationCategory.Texture, 2);
            if (ext is ".dll" or ".asi") Add(AutomationCategory.Plugin, 5);
            if (ext is ".wem" or ".bnk") Add(AutomationCategory.Audio, 5);
            if (p.Contains("\\ui\\", StringComparison.Ordinal) || p.Contains("\\gui\\", StringComparison.Ordinal)) Add(AutomationCategory.Ui, 5);
            if (p.Contains("\\weapon\\", StringComparison.Ordinal) || p.Contains("\\wp\\", StringComparison.Ordinal)) Add(AutomationCategory.Weapon, 5);
            if (p.Contains("npc", StringComparison.Ordinal)) Add(AutomationCategory.Npc, 4);
            if (p.Contains("body", StringComparison.Ordinal) || p.Contains("skin", StringComparison.Ordinal)) Add(AutomationCategory.Body, 3);
            if (includeMhwSemantics)
            {
                if (p.Contains("handler", StringComparison.Ordinal)) Add(AutomationCategory.Handler, 6);
                if (p.Contains("otomo", StringComparison.Ordinal) || p.Contains("palico", StringComparison.Ordinal)) Add(AutomationCategory.Palico, 6);
                if (p.Contains("f_equip", StringComparison.Ordinal) || p.Contains("m_equip", StringComparison.Ordinal)) Add(AutomationCategory.Armor, 5);
                if (ext is ".quest" or ".gmd" or ".dtq" or ".lot") Add(AutomationCategory.QuestData, 4);
            }
        }
        if (scores.Count == 0)
        {
            UnifiedDebugLog.Write("AUTOCAT", "Classification result: Unknown (no scoring paths).");
            return AutomationCategory.Unknown;
        }
        var ordered = scores.OrderByDescending(x => x.Value).ToArray();
        if (ordered.Length > 1 && ordered[1].Value >= ordered[0].Value - 1 && ordered[0].Key != AutomationCategory.Texture)
        {
            UnifiedDebugLog.Write("AUTOCAT", $"Classification result: Mixed; scores={string.Join(",", ordered.Select(x => $"{x.Key}:{x.Value}"))}");
            return AutomationCategory.Mixed;
        }
        UnifiedDebugLog.Write("AUTOCAT", $"Classification result: {ordered[0].Key}; scores={string.Join(",", ordered.Select(x => $"{x.Key}:{x.Value}"))}");
        return ordered[0].Key;
    }

    public AutomationCategory Classify(IEnumerable<string> paths) => ClassifyPaths(paths, game is null || game.IsMonsterHunterWorld);

    public async Task<int> AssignMissingAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var missing = mods.Where(mod => string.IsNullOrWhiteSpace(mod.Category)).ToArray();
        if (missing.Length == 0) return 0;

        // One indexed query replaces one SQLite command/reader per mod. Read the whole
        // result before writing classifications on the same connection.
        // SQLite mods.id is a binary TEXT primary key. Do not merge case-distinct IDs.
        var indexedPaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var c = await db.OpenAsync(ct);
        await using (var read = c.CreateCommand())
        {
            read.CommandText = """
                SELECT f.mod_id, f.path
                FROM mod_files f
                JOIN mods m ON m.id = f.mod_id
                WHERE m.category IS NULL OR trim(m.category) = ''
                ORDER BY f.mod_id, f.path
                """;
            await using var rows = await read.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct))
            {
                var id = rows.GetString(0);
                if (!indexedPaths.TryGetValue(id, out var paths))
                    indexedPaths[id] = paths = [];
                paths.Add(rows.GetString(1));
            }
        }

        await using var update = c.CreateCommand();
        update.CommandText = "UPDATE mods SET category=$c WHERE id=$m AND (category IS NULL OR trim(category)='')";
        var categoryParam = update.Parameters.AddWithValue("$c", string.Empty);
        var modParam = update.Parameters.AddWithValue("$m", string.Empty);
        var assigned = 0;
        foreach (var mod in missing)
        {
            ct.ThrowIfCancellationRequested();
            var paths = indexedPaths.TryGetValue(mod.Id, out var stored) ? stored : new List<string>();
            // Preserve the pre-capture fallback without traversing already indexed packages.
            if (paths.Count == 0 && Directory.Exists(mod.SourcePath))
                paths.AddRange(Directory.EnumerateFiles(mod.SourcePath, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(mod.SourcePath, path)));
            if (MasterDebugLog.DetailedDiagnosticsEnabled)
                startupDiagnostics?.Info("startup.automation.categories.mod.paths",
                    $"ModId={mod.Id}; CandidatePaths={paths.Count}");
            var category = ClassifyPaths(paths, game is null || game.IsMonsterHunterWorld);
            if (category == AutomationCategory.Unknown) continue;
            categoryParam.Value = category.ToString();
            modParam.Value = mod.Id;
            assigned += await update.ExecuteNonQueryAsync(ct);
        }
        UnifiedDebugLog.Write("AUTOCAT", $"Assigned={assigned}; Missing={missing.Length}; IndexedMods={indexedPaths.Count}");
        return assigned;
    }
}
