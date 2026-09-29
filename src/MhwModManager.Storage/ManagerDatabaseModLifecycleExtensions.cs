using System.Globalization;
using MhwModManager.Core;

namespace MhwModManager.Storage;

/// <summary>
/// Owns durable mod retirement semantics that are broader than a foreign-key row delete.
/// Historical/audit records are intentionally preserved, while live references that could
/// reactivate against a later package reusing the same ID are retired transactionally.
/// </summary>
public static class ManagerDatabaseModLifecycleExtensions
{
    public static async Task RetireModAsync(this ManagerDatabase db, string modId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={modId}");
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod ID is required.", nameof(modId));

        await db.InTransactionAsync<object?>(async (c, tx, token) =>
        {
            await using (var manifest = c.CreateCommand())
            {
                manifest.Transaction = tx;
                manifest.CommandText = "SELECT EXISTS(SELECT 1 FROM deployment_manifest WHERE provider_mod_id=$m)";
                manifest.Parameters.AddWithValue("$m", modId);
                var isDeployed = Convert.ToInt64(await manifest.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0;
                if (isDeployed)
                    throw new InvalidOperationException($"Cannot retire mod '{modId}' while it still owns files in the current deployment manifest.");
            }

            await using (var manifestRules = c.CreateCommand())
            {
                manifestRules.Transaction = tx;
                manifestRules.CommandText = """
                    UPDATE deployment_manifest
                    SET rule_id=NULL
                    WHERE rule_id IN (
                        SELECT id FROM conflict_rules
                        WHERE left_mod_id=$m OR right_mod_id=$m OR winner_mod_id=$m
                    )
                    """;
                manifestRules.Parameters.AddWithValue("$m", modId);
                await manifestRules.ExecuteNonQueryAsync(token);
            }

            await using (var rules = c.CreateCommand())
            {
                rules.Transaction = tx;
                rules.CommandText = "DELETE FROM conflict_rules WHERE left_mod_id=$m OR right_mod_id=$m OR winner_mod_id=$m";
                rules.Parameters.AddWithValue("$m", modId);
                await rules.ExecuteNonQueryAsync(token);
            }

            await using (var providers = c.CreateCommand())
            {
                providers.Transaction = tx;
                providers.CommandText = "DELETE FROM resource_providers WHERE mod_id=$m";
                providers.Parameters.AddWithValue("$m", modId);
                await providers.ExecuteNonQueryAsync(token);
            }

            await using (var settings = c.CreateCommand())
            {
                settings.Transaction = tx;
                settings.CommandText = """
                    DELETE FROM settings
                    WHERE key IN (
                        'preview:' || $m,
                        'visuals:' || $m,
                        'update:' || $m,
                        'visual-public-last:' || $m
                    )
                    """;
                settings.Parameters.AddWithValue("$m", modId);
                await settings.ExecuteNonQueryAsync(token);
            }

            await using (var delete = c.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM mods WHERE id=$m";
                delete.Parameters.AddWithValue("$m", modId);
                if (await delete.ExecuteNonQueryAsync(token) != 1)
                    throw new KeyNotFoundException($"Unknown mod '{modId}'.");
            }

            return null;
        }, ct);
    }
}
