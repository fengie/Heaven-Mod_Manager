using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

public sealed record MetadataRowChange(string Table, Dictionary<string, string?> Key,
    Dictionary<string, string?>? Before, Dictionary<string, string?>? After);

/// <summary>Journaled, optimistic row edits for update migration; fixed table/column allowlists prevent arbitrary SQL.</summary>
public static class DeploymentMetadata
{
    private static readonly Dictionary<string, string[]> Columns = new(StringComparer.Ordinal)
    {
        ["mods"] = ["id", "family_id"],
        ["mod_family_members"] = ["family_id", "mod_id", "role", "choice_group"],
        ["conflict_rules"] = ["id", "kind", "scope", "left_mod_id", "right_mod_id", "winner_mod_id", "path_pattern", "reason", "explicit", "created_at"],
        ["profile_rules"] = ["profile_id", "rule_id"],
        ["resource_providers"] = ["namespace", "mod_id"],
        ["family_preferences"] = ["family_id", "choice_group", "selected_mod_id", "updated_at"],
        ["mod_supersession"] = ["older_mod_id", "newer_mod_id", "confidence_score", "reason", "created_at"]
    };

    public static async Task<List<MetadataRowChange>> ReadRowsAsync(ManagerDatabase db, string table, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var columns = Columns[table];
        await using var c = await db.OpenAsync(ct); await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {string.Join(',', columns)} FROM {table}";
        var rows = new List<MetadataRowChange>(); await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Length; i++) row[columns[i]] = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
            var keys = table is "profile_rules" ? new[] { "profile_id", "rule_id" } : table is "mod_family_members" ? new[] { "family_id", "mod_id" } : table is "family_preferences" ? ["family_id", "choice_group"] : [columns[0]];
            rows.Add(new(table, keys.ToDictionary(k => k, k => row[k], StringComparer.Ordinal), row, new(row, StringComparer.Ordinal)));
        }
        return rows;
    }

    public static async Task ApplyAsync(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<MetadataRowChange> edits, bool tolerateAlreadyApplied, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach (var edit in edits)
        {
            if (!Columns.TryGetValue(edit.Table, out var columns) || edit.Key.Count == 0 || edit.Key.Keys.Concat(edit.Before?.Keys.AsEnumerable() ?? []).Concat(edit.After?.Keys.AsEnumerable() ?? []).Any(k => !columns.Contains(k, StringComparer.Ordinal)))
                throw new InvalidDataException("Invalid migration metadata edit.");
            await using var read = c.CreateCommand(); read.Transaction = tx;
            var where = string.Join(" AND ", edit.Key.Select((p, i) => p.Key + " IS $k" + i));
            var index = 0; foreach (var key in edit.Key) read.Parameters.AddWithValue("$k" + index++, (object?)key.Value ?? DBNull.Value);
            read.CommandText = $"SELECT {string.Join(',', columns)} FROM {edit.Table} WHERE {where}";
            Dictionary<string, string?>? current = null;
            await using (var r = await read.ExecuteReaderAsync(ct))
                if (await r.ReadAsync(ct))
                {
                    current = new(StringComparer.Ordinal);
                    for (var i = 0; i < columns.Length; i++) current[columns[i]] = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
                }
            if (tolerateAlreadyApplied && Same(current, edit.After)) continue;
            if (!Same(current, edit.Before)) throw new InvalidOperationException("Migration metadata changed since preview: " + edit.Table + ". Preview again.");
            await using var write = c.CreateCommand(); write.Transaction = tx;
            index = 0; foreach (var key in edit.Key) write.Parameters.AddWithValue("$k" + index++, (object?)key.Value ?? DBNull.Value);
            if (edit.After is null) write.CommandText = $"DELETE FROM {edit.Table} WHERE {where}";
            else
            {
                index = 0; foreach (var entry in edit.After) write.Parameters.AddWithValue("$v" + index++, (object?)entry.Value ?? DBNull.Value);
                write.CommandText = edit.Before is null
                    ? $"INSERT INTO {edit.Table}({string.Join(',', edit.After.Keys)}) VALUES({string.Join(',', edit.After.Select((_, i) => "$v" + i))})"
                    : $"UPDATE {edit.Table} SET {string.Join(',', edit.After.Select((p, i) => p.Key + "=$v" + i))} WHERE {where}";
            }
            await write.ExecuteNonQueryAsync(ct);
        }
    }

    public static bool Same(Dictionary<string, string?>? a, Dictionary<string, string?>? b)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return a is null ? b is null : b is not null && a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && StringComparer.Ordinal.Equals(p.Value, value));
    }
    public static IReadOnlyList<MetadataRowChange> Invert(IReadOnlyList<MetadataRowChange> edits)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return edits.Reverse().Select(e => new MetadataRowChange(e.Table, e.After is null ? e.Key : e.Key.ToDictionary(k => k.Key, k => e.After[k.Key], StringComparer.Ordinal), e.After, e.Before)).ToArray();
    }
    public static async Task<IReadOnlyList<MetadataRowChange>> LoadAsync(SqliteConnection c, SqliteTransaction? tx, string operationId, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT changes_json FROM operation_metadata WHERE operation_id=$o"; cmd.Parameters.AddWithValue("$o", operationId);
        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json is null ? [] : JsonSerializer.Deserialize<List<MetadataRowChange>>(json) ?? throw new InvalidDataException("Invalid metadata journal.");
    }
    public static async Task CommitAsync(SqliteConnection c, SqliteTransaction tx, string operationId, IReadOnlyList<MetadataRowChange> edits, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await ApplyAsync(c, tx, edits, false, ct);
        await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO operation_metadata(operation_id,changes_json) VALUES($o,$j)";
        cmd.Parameters.AddWithValue("$o", operationId); cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(edits)); await cmd.ExecuteNonQueryAsync(ct);
    }
}
