using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class ModIssueFallbackService(ManagerDatabase db, ChangeTimelineService timeline, ModTrustService trust)
{
    private sealed record LaunchSnapshot(string Id, DateTimeOffset StartedAt, bool Success, IReadOnlyDictionary<string, ModState> State);

    public async Task<IReadOnlyList<ModIssueSuspect>> GetActiveAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows = new List<ModIssueSuspect>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT s.mod_id,m.display_name,s.issue_kind,s.score,s.reason,s.first_seen,s.last_seen,s.failure_count,s.confirmed,s.last_launch_id
            FROM mod_issue_suspects s
            JOIN mods m ON m.id=s.mod_id
            WHERE s.active=1
            ORDER BY s.confirmed DESC,s.score DESC,s.last_seen DESC
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new(
                r.GetString(0), r.GetString(1), ParseKind(r.GetString(2)), r.GetInt32(3), r.GetString(4),
                DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(r.GetString(6), CultureInfo.InvariantCulture),
                r.GetInt32(7), r.GetInt32(8) != 0, r.IsDBNull(9) ? null : r.GetString(9)));
        }
        return rows;
    }

    public async Task<IssueDiagnosisResult> AnalyzeLatestLaunchAsync(ModIssueKind kind, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var latest = await ReadLatestLaunchAsync(null, ct) ?? throw new InvalidOperationException("No game launch has been recorded yet.");
        return await AnalyzeAndPersistAsync(latest, kind, ct);
    }

    public async Task<IssueDiagnosisResult> RecordLaunchFailureAsync(string launchId, ModIssueKind kind, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var launch = await ReadLatestLaunchAsync(launchId, ct) ?? throw new InvalidOperationException("The failed launch could not be found in launch history.");
        return await AnalyzeAndPersistAsync(launch, kind, ct);
    }

    public async Task MarkBisectResultAsync(IReadOnlyList<string> modIds, string? launchId = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (modIds.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        await using var c = await db.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        foreach (var id in modIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await UpsertAsync(c, tx, id, ModIssueKind.BisectIsolated, 99,
                "Automatic crash bisection isolated this mod as part of the reproducing suspect set.", now, launchId, true, ct);
        }
        await tx.CommitAsync(ct);
        await timeline.RecordAsync("diagnosis.issue-suspect", AutomationSeverity.Blocker,
            $"Crash bisection marked {modIds.Count} mod(s) as isolated suspects.", new { modIds, launchId }, ct);
        UnifiedDebugLog.Write("MOD-ISSUE", $"BISECT isolated={string.Join(',', modIds)}; launch={launchId ?? "<none>"}");
    }

    public async Task ClearAsync(string modId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE mod_issue_suspects SET active=0,resolved_at=$t WHERE mod_id=$m AND active=1";
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$m", modId);
        await cmd.ExecuteNonQueryAsync(ct);
        UnifiedDebugLog.Write("MOD-ISSUE", $"CLEAR mod={modId}");
    }

    public async Task RecordSuccessfulLaunchAsync(IReadOnlyCollection<string> enabledModIds, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (enabledModIds.Count == 0) return;
        var ids = enabledModIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await using var c = await db.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        for (var offset = 0; offset < ids.Length; offset += 300)
        {
            var batch = ids.Skip(offset).Take(300).ToArray();
            await using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            var names = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                names[i] = "$m" + i.ToString(CultureInfo.InvariantCulture);
                cmd.Parameters.AddWithValue(names[i], batch[i]);
            }
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            cmd.CommandText = $"UPDATE mod_issue_suspects SET active=0,resolved_at=$t WHERE confirmed=0 AND active=1 AND mod_id IN ({string.Join(',', names)})";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task<IssueDiagnosisResult> AnalyzeAndPersistAsync(LaunchSnapshot launch, ModIssueKind kind, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var baseline = await ReadPreviousSuccessfulLaunchAsync(launch, ct);
        var mods = await db.GetModsAsync(ct);
        var files = await db.GetModFilesAsync(ct);
        var modById = mods.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var filesByMod = files.GroupBy(x => x.ModId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var currentEnabled = launch.State.Where(x => x.Value.Enabled).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baselineState = baseline?.State ?? new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase);

        var candidates = currentEnabled.Where(id =>
        {
            if (!baselineState.TryGetValue(id, out var old) || !old.Enabled) return true;
            return launch.State.TryGetValue(id, out var now) && now.Priority != old.Priority;
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (candidates.Count == 0)
        {
            foreach (var id in currentEnabled) candidates.Add(id);
        }

        var scored = new List<ModIssueSuspect>();
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var id in candidates)
        {
            if (!modById.TryGetValue(id, out var mod)) continue;
            baselineState.TryGetValue(id, out var old);
            var newlyEnabled = old is null || !old.Enabled;
            var priorityChanged = old is not null && old.Enabled && launch.State[id].Priority != old.Priority;
            var modFiles = filesByMod.TryGetValue(id, out var found) ? found : [];
            var texture = modFiles.Count(x => x.FileClass == FileClass.Texture);
            var structural = modFiles.Count(x => x.FileClass == FileClass.Structural);
            var gameData = modFiles.Count(x => x.FileClass == FileClass.GameData);
            var plugin = modFiles.Count(x => x.FileClass == FileClass.Plugin);
            var executable = modFiles.Count(x => x.FileClass == FileClass.Executable);
            var score = newlyEnabled ? 58 : priorityChanged ? 43 : 24;
            var evidence = new List<string>();
            if (newlyEnabled) evidence.Add("enabled since the previous successful launch");
            if (priorityChanged) evidence.Add("priority changed since the previous successful launch");
            if (!newlyEnabled && !priorityChanged) evidence.Add("enabled during the failing launch; no changed-only candidate was available");

            switch (kind)
            {
                case ModIssueKind.GpuGraphicsCrash:
                    if (texture > 0) { score += Math.Min(28, 12 + texture / 4); evidence.Add($"contains {texture} texture file(s)"); }
                    if (plugin > 0 || executable > 0) { score += 14; evidence.Add("contains renderer-sensitive plugin/executable content"); }
                    if (structural > 0) { score += 7; evidence.Add($"contains {structural} structural asset(s)"); }
                    break;
                case ModIssueKind.StartupCrash:
                case ModIssueKind.GameCrash:
                    if (plugin > 0) { score += 25; evidence.Add($"contains {plugin} plugin file(s)"); }
                    if (executable > 0) { score += 24; evidence.Add($"contains {executable} executable/root component(s)"); }
                    if (gameData > 0) { score += 18; evidence.Add($"contains {gameData} game-data file(s)"); }
                    if (structural > 0) { score += 14; evidence.Add($"contains {structural} structural asset(s)"); }
                    if (texture > 0) { score += 5; evidence.Add($"contains {texture} texture file(s)"); }
                    break;
                case ModIssueKind.BisectIsolated:
                    score = 99;
                    break;
            }

            var history = await trust.GetAsync(id, ct);
            if (history is not null)
            {
                if (history.FailedLaunches > 0)
                {
                    score += Math.Min(12, history.FailedLaunches * 3);
                    evidence.Add($"{history.FailedLaunches} prior failed launch(es)");
                }
                if (history.SuccessfulLaunches >= 3)
                {
                    score -= Math.Min(18, history.SuccessfulLaunches / 2);
                    evidence.Add($"{history.SuccessfulLaunches} prior successful launch(es) reduce suspicion");
                }
            }

            score = Math.Clamp(score, 10, 99);
            var reason = string.Join("; ", evidence);
            scored.Add(new(id, mod.DisplayName, kind, score, reason, nowUtc, nowUtc, 1, false, launch.Id));
        }

        var selected = scored.OrderByDescending(x => x.Score).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        await using (var c = await db.OpenAsync(ct))
        await using (var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct))
        {
            foreach (var suspect in selected)
            {
                await UpsertAsync(c, tx, suspect.ModId, suspect.Kind, suspect.Score, suspect.Reason, nowUtc, launch.Id, false, ct);
            }
            await tx.CommitAsync(ct);
        }

        var baselineLabel = baseline is null ? "no previous successful launch was available" : $"compared with successful launch {baseline.StartedAt.LocalDateTime:g}";
        var message = selected.Length == 0
            ? $"No enabled mod candidates could be scored for the reported {Describe(kind)}."
            : $"Marked {selected.Length} suspect mod(s) for the reported {Describe(kind)}; {baselineLabel}. Highest: {selected[0].DisplayName} ({selected[0].Score}%).";
        await timeline.RecordAsync("diagnosis.issue-fallback", AutomationSeverity.Warning, message,
            new { launchId = launch.Id, kind = kind.ToString(), baseline = baseline?.Id, suspects = selected.Select(x => new { x.ModId, x.DisplayName, x.Score, x.Reason }).ToArray() }, ct);
        UnifiedDebugLog.Write("MOD-ISSUE", $"REPORT kind={kind}; launch={launch.Id}; baseline={baseline?.Id ?? "<none>"}; suspects={string.Join(" | ", selected.Select(x => $"{x.ModId}:{x.Score}"))}");
        return new(kind, launch.Id, baseline?.Id, selected, message);
    }

    private async Task<LaunchSnapshot?> ReadLatestLaunchAsync(string? launchId, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        if (string.IsNullOrWhiteSpace(launchId))
        {
            cmd.CommandText = "SELECT id,started_at,success,state_json FROM launch_history WHERE mode='Modded' ORDER BY started_at DESC LIMIT 1";
        }
        else
        {
            cmd.CommandText = "SELECT id,started_at,success,state_json FROM launch_history WHERE id=$i LIMIT 1";
            cmd.Parameters.AddWithValue("$i", launchId);
        }
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadLaunch(r);
    }

    private async Task<LaunchSnapshot?> ReadPreviousSuccessfulLaunchAsync(LaunchSnapshot launch, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,started_at,success,state_json FROM launch_history WHERE mode='Modded' AND success=1 AND started_at < $t AND id <> $i ORDER BY started_at DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$t", launch.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$i", launch.Id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadLaunch(r) : null;
    }

    private static LaunchSnapshot ReadLaunch(SqliteDataReader r)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var state = JsonSerializer.Deserialize<Dictionary<string, ModState>>(r.GetString(3), AutomationJson.Options)
            ?? new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase);
        return new(r.GetString(0), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), r.GetInt32(2) != 0,
            new Dictionary<string, ModState>(state, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task UpsertAsync(SqliteConnection c, SqliteTransaction tx, string modId, ModIssueKind kind, int score, string reason,
        DateTimeOffset now, string? launchId, bool confirmed, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO mod_issue_suspects(mod_id,issue_kind,score,reason,first_seen,last_seen,failure_count,last_launch_id,active,confirmed,resolved_at)
            VALUES($m,$k,$s,$r,$t,$t,1,$l,1,$c,NULL)
            ON CONFLICT(mod_id,issue_kind) DO UPDATE SET
                score=MAX(mod_issue_suspects.score,excluded.score),
                reason=excluded.reason,
                last_seen=excluded.last_seen,
                failure_count=mod_issue_suspects.failure_count+1,
                last_launch_id=excluded.last_launch_id,
                active=1,
                confirmed=MAX(mod_issue_suspects.confirmed,excluded.confirmed),
                resolved_at=NULL
            """;
        cmd.Parameters.AddWithValue("$m", modId);
        cmd.Parameters.AddWithValue("$k", kind.ToString());
        cmd.Parameters.AddWithValue("$s", score);
        cmd.Parameters.AddWithValue("$r", reason);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$l", (object?)launchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", confirmed ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ModIssueKind ParseKind(string value) => Enum.TryParse<ModIssueKind>(value, true, out var kind) ? kind : ModIssueKind.GameCrash;
    private static string Describe(ModIssueKind kind) => kind switch
    {
        ModIssueKind.GpuGraphicsCrash => "GPU/graphics crash",
        ModIssueKind.StartupCrash => "startup crash",
        ModIssueKind.BisectIsolated => "bisected crash",
        _ => "game crash"
    };
}
