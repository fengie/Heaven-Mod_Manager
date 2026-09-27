using MhwModManager.Core;
using System.Globalization;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class ModTrustService(ManagerDatabase db)
{
    public async Task RecordLaunchAsync(IEnumerable<string> enabledModIds, bool success, bool rollback, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await c.BeginTransactionAsync(ct);
        foreach (var id in enabledModIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT INTO mod_trust(mod_id,successful_launches,failed_launches,rollback_count,last_success_at,last_failure_at)
            VALUES($m,$s,$f,$r,$ls,$lf)
            ON CONFLICT(mod_id) DO UPDATE SET
              successful_launches=successful_launches+excluded.successful_launches,
              failed_launches=failed_launches+excluded.failed_launches,
              rollback_count=rollback_count+excluded.rollback_count,
              last_success_at=COALESCE(excluded.last_success_at,last_success_at),
              last_failure_at=COALESCE(excluded.last_failure_at,last_failure_at)
            """;
            cmd.Parameters.AddWithValue("$m", id); cmd.Parameters.AddWithValue("$s", success ? 1 : 0); cmd.Parameters.AddWithValue("$f", success ? 0 : 1); cmd.Parameters.AddWithValue("$r", rollback ? 1 : 0);
            cmd.Parameters.AddWithValue("$ls", success ? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
            cmd.Parameters.AddWithValue("$lf", success ? DBNull.Value : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<TrustSnapshot?> GetAsync(string modId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT successful_launches,failed_launches,rollback_count,last_success_at,last_failure_at FROM mod_trust WHERE mod_id=$m"; cmd.Parameters.AddWithValue("$m", modId);
        await using var r = await cmd.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) return null;
        return new(modId, r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3)?null:DateTimeOffset.Parse(r.GetString(3),CultureInfo.InvariantCulture), r.IsDBNull(4)?null:DateTimeOffset.Parse(r.GetString(4),CultureInfo.InvariantCulture));
    }
}
