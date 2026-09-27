using System.Globalization;
using MhwModManager.Core;

namespace MhwModManager.Storage;

/// <summary>
/// Read-only planner input projection. Mod state is intentionally loaded through ManagerDatabase
/// first, then the remaining planner tables are read through a second connection to preserve the
/// pre-extraction connection semantics.
/// </summary>
public sealed class PlannerSnapshotRepository(ManagerDatabase db)
{
    public Task<PlannerSnapshot> LoadAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return LoadAsync(null, ct);
    }

    /// <summary>Loads all planner input, optionally restricting mod_files to the supplied mod IDs.</summary>
    public async Task<PlannerSnapshot> LoadAsync(IReadOnlyCollection<string>? fileModIds, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();

        var mods = await db.GetModsAsync(ct);
        var files = new List<ModFileDescriptor>();
        var rules = new List<ConflictRule>();
        var exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new Dictionary<string, DeploymentManifestEntry>(StringComparer.OrdinalIgnoreCase);
        var originals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        await using var c = await db.OpenAsync(ct);

        if (fileModIds is not { Count: 0 })
        {
            await using var cmd = c.CreateCommand();
            if (fileModIds is null)
            {
                cmd.CommandText = "SELECT mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files";
            }
            else
            {
                var ids = fileModIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var names = new string[ids.Length];
                for (var i = 0; i < ids.Length; i++)
                {
                    names[i] = "$m" + i.ToString(CultureInfo.InvariantCulture);
                    cmd.Parameters.AddWithValue(names[i], ids[i]);
                }
                cmd.CommandText = $"SELECT mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files WHERE mod_id IN ({string.Join(',', names)})";
            }

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                files.Add(new(
                    r.GetString(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.GetInt64(4),
                    DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture),
                    Enum.Parse<FileClass>(r.GetString(6))));
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at FROM conflict_rules";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var rule = new ConflictRule(
                    r.GetString(0),
                    Enum.Parse<RuleKind>(r.GetString(1)),
                    Enum.Parse<RuleScope>(r.GetString(2)),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetString(6),
                    r.GetString(7),
                    r.GetInt64(8) != 0,
                    DateTimeOffset.Parse(r.GetString(9), CultureInfo.InvariantCulture));
                rules.Add(rule);
                if (rule.Kind == RuleKind.ExactWinner && rule.PathPattern is not null && rule.WinnerModId is not null)
                    exact[rule.PathPattern] = rule.WinnerModId;
            }
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT namespace,mod_id FROM resource_providers";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                resources[r.GetString(0)] = r.GetString(1);
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at FROM deployment_manifest";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var entry = new DeploymentManifestEntry(
                    r.GetString(0),
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture));
                manifest[entry.Path] = entry;
            }
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT path,blob_sha256 FROM original_files";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                originals[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
        }

        return new(mods, files, rules, exact, resources, manifest, originals);
    }
}
