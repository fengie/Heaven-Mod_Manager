using System.Globalization;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;
namespace MhwModManager.Storage;

public sealed record ProfileSummary(string Id, string Name, int EnabledMods, DateTimeOffset UpdatedAt, string? ParentId = null);

public sealed class ProfileRepository(ManagerDatabase db)
{
    public async Task<IReadOnlyDictionary<string, (bool enabled, int priority)>> LoadAsync(string profileId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        return await ResolveAsync(c, tx, profileId, new(StringComparer.OrdinalIgnoreCase), ct);
    }

    private static async Task<Dictionary<string, (bool enabled, int priority)>> ResolveAsync(SqliteConnection c, SqliteTransaction tx, string id, HashSet<string> visited, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!visited.Add(id)) throw new InvalidDataException("Profile inheritance contains a cycle.");
        if (visited.Count > 128) throw new InvalidDataException("Profile inheritance exceeds 128 levels.");
        string? parent;
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT pp.parent_id FROM profiles p LEFT JOIN profile_parents pp ON pp.profile_id=p.id WHERE p.id=$p";
            cmd.Parameters.AddWithValue("$p", id);
            var value = await cmd.ExecuteScalarAsync(ct);
            if (value is null) throw new KeyNotFoundException("Profile does not exist: " + id);
            parent = value is DBNull ? null : (string)value;
        }
        var map = parent is null ? new Dictionary<string, (bool enabled, int priority)>(StringComparer.OrdinalIgnoreCase)
            : await ResolveAsync(c, tx, parent, visited, ct);
        await using var rows = c.CreateCommand();
        rows.Transaction = tx;
        rows.CommandText = "SELECT mod_id,enabled,priority FROM profile_mods WHERE profile_id=$p";
        rows.Parameters.AddWithValue("$p", id);
        await using var reader = await rows.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) map[reader.GetString(0)] = (reader.GetInt64(1) != 0, reader.GetInt32(2));
        return map;
    }

    public async Task<IReadOnlyList<ProfileSummary>> ListAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows = new List<ProfileSummary>();
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT p.id,p.name,p.updated_at,pp.parent_id FROM profiles p LEFT JOIN profile_parents pp ON pp.profile_id=p.id ORDER BY p.name COLLATE NOCASE";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rows.Add(new(r.GetString(0), r.GetString(1), 0, DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture), r.IsDBNull(3) ? null : r.GetString(3)));
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var resolved = await ResolveAsync(c, tx, rows[i].Id, new(StringComparer.OrdinalIgnoreCase), ct);
            rows[i] = rows[i] with { EnabledMods = resolved.Count(x => x.Value.enabled) };
        }
        return rows;
    }

    public async Task SaveCurrentAsync(string name, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        // Keep a saved child's parent when updating it through the original Profiles screen.
        var existing = (await ListAsync(ct)).FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, name));
        await SaveAsync(name, mods.ToDictionary(m => m.Id, m => (m.Enabled, m.Priority), StringComparer.OrdinalIgnoreCase), existing?.ParentId, ct);
    }

    public async Task<string> SaveAsync(string name, IReadOnlyDictionary<string, (bool enabled, int priority)> state, string? parentId = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        return await db.InTransactionAsync(async (c, tx, token) =>
        {
            var id = Guid.NewGuid().ToString("N");
            await using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO profiles(id,name,created_at,updated_at) VALUES($i,$n,$u,$u) ON CONFLICT(name) DO UPDATE SET updated_at=excluded.updated_at RETURNING id";
                cmd.Parameters.AddWithValue("$i", id); cmd.Parameters.AddWithValue("$n", name.Trim()); cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
                id = (string)(await cmd.ExecuteScalarAsync(token))!;
            }
            var inherited = parentId is null ? new Dictionary<string, (bool enabled, int priority)>(StringComparer.OrdinalIgnoreCase)
                : await ResolveAsync(c, tx, parentId, new(StringComparer.OrdinalIgnoreCase) { id }, token);
            await using (var clear = c.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM profile_mods WHERE profile_id=$p; DELETE FROM profile_parents WHERE profile_id=$p;";
                clear.Parameters.AddWithValue("$p", id); await clear.ExecuteNonQueryAsync(token);
            }
            if (parentId is not null)
            {
                await using var parent = c.CreateCommand(); parent.Transaction = tx;
                parent.CommandText = "INSERT INTO profile_parents(profile_id,parent_id) VALUES($p,$b)";
                parent.Parameters.AddWithValue("$p", id); parent.Parameters.AddWithValue("$b", parentId); await parent.ExecuteNonQueryAsync(token);
            }
            // An omitted inherited mod is explicitly disabled, so saving a complete setup never enables it accidentally.
            var complete = new Dictionary<string, (bool enabled, int priority)>(state, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in inherited) complete.TryAdd(entry.Key, (false, entry.Value.priority));
            foreach (var entry in complete)
            {
                if (inherited.TryGetValue(entry.Key, out var old) && old == entry.Value) continue;
                await using var row = c.CreateCommand(); row.Transaction = tx;
                row.CommandText = "INSERT INTO profile_mods(profile_id,mod_id,enabled,priority) VALUES($p,$m,$e,$r)";
                row.Parameters.AddWithValue("$p", id); row.Parameters.AddWithValue("$m", entry.Key); row.Parameters.AddWithValue("$e", entry.Value.enabled); row.Parameters.AddWithValue("$r", entry.Value.priority);
                await row.ExecuteNonQueryAsync(token);
            }
            return id;
        }, ct);
    }
}
