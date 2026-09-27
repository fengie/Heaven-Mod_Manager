using MhwModManager.Core;
using System.Globalization;
using System.Text.Json;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class ChangeTimelineService(ManagerDatabase db)
{
    public async Task RecordAsync(string kind, AutomationSeverity severity, string message, object? data = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO automation_events(id,time,kind,severity,message,data_json) VALUES($i,$t,$k,$s,$m,$d)";
        cmd.Parameters.AddWithValue("$i", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$s", severity.ToString());
        cmd.Parameters.AddWithValue("$m", message);
        cmd.Parameters.AddWithValue("$d", data is null ? DBNull.Value : JsonSerializer.Serialize(data, AutomationJson.Options));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AutomationEvent>> RecentAsync(int limit = 200, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var rows = new List<AutomationEvent>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,time,kind,severity,message,data_json FROM automation_events ORDER BY time DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", Math.Clamp(limit, 1, 5000));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new(r.GetString(0), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), r.GetString(2), Enum.Parse<AutomationSeverity>(r.GetString(3), true), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return rows;
    }
}
