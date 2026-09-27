using System.Globalization;
using MhwModManager.Core;

namespace MhwModManager.Storage;

/// <summary>
/// Read-only presentation projections. Keeping these queries outside WPF prevents views/viewmodels
/// from becoming a second persistence layer while leaving ManagerDatabase's connection/transaction
/// infrastructure authoritative.
/// </summary>
public sealed class PresentationReadRepository(ManagerDatabase db)
{
    public async Task<IReadOnlyList<ActivityEntry>> GetRecentActivityAsync(int limit = 300, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"limit={limit}");
        if (limit <= 0) return Array.Empty<ActivityEntry>();

        var rows = new List<ActivityEntry>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id,state,description,started_at FROM operations
            UNION ALL
            SELECT id,severity,message,time FROM automation_events
            ORDER BY started_at DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return rows;
    }

    public async Task<IReadOnlyList<OutfitCoverageEntry>> GetOutfitCoverageAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows = new List<OutfitCoverageEntry>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT a.name,a.model_id,
                   COUNT(DISTINCT mfa.mod_id),
                   GROUP_CONCAT(DISTINCT CASE WHEN dm.path IS NOT NULL THEN mfa.component END),
                   GROUP_CONCAT(DISTINCT pm.display_name),
                   (SELECT st.value
                    FROM mod_file_armor px
                    JOIN mods mx ON mx.id=px.mod_id
                    JOIN settings st ON st.key='preview:'||mx.id
                    WHERE px.model_id=a.model_id AND length(st.value)>0
                    ORDER BY mx.enabled DESC,mx.priority DESC
                    LIMIT 1)
            FROM armor_catalog a
            LEFT JOIN mod_file_armor mfa ON mfa.model_id=a.model_id
            LEFT JOIN mods pm ON pm.id=mfa.mod_id
            LEFT JOIN deployment_manifest dm ON dm.path=mfa.path
            GROUP BY a.series_id,a.name,a.model_id
            ORDER BY a.series_id
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new(
                r.GetString(0),
                r.GetString(1),
                r.GetInt32(2),
                r.IsDBNull(3) ? string.Empty : r.GetString(3),
                r.IsDBNull(4) ? string.Empty : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5)));
        }
        return rows;
    }
}

public sealed record ActivityEntry(string Id, string State, string Description, string StartedAt);
public sealed record OutfitCoverageEntry(string Armor, string ModelId, int AvailableProviders, string WinningPieces, string Providers, string? PreviewPath);
