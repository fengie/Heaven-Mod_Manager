using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public enum LaunchObservationPersistenceResult
{
    Persisted,
    AlreadyPersisted
}

public sealed class LaunchObservationRepository(ManagerDatabase db)
{
    public async Task<LaunchObservationPersistenceResult> PersistAsync(LaunchObservationRecord observation)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Id);

        var state = CopyState(observation.State);
        var stateJson = JsonSerializer.Serialize(state, AutomationJson.Options);
        var ct = CancellationToken.None;

        await using var connection = await db.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        var existing = await ReadExistingAsync(connection, transaction, observation.Id, ct);
        if (existing is not null)
        {
            if (!Matches(existing, observation, state))
                throw new InvalidOperationException($"Launch observation '{observation.Id}' already exists with conflicting immutable evidence.");

            await transaction.CommitAsync(ct);
            return LaunchObservationPersistenceResult.AlreadyPersisted;
        }

        await InsertHistoryAsync(connection, transaction, observation, stateJson, ct);
        await ModTrustService.ApplyLaunchAsync(
            connection,
            transaction,
            state.Where(x => x.Value.Enabled).Select(x => x.Key),
            observation.Success,
            rollback: false,
            observation.EndedAt,
            ct);
        await transaction.CommitAsync(ct);
        return LaunchObservationPersistenceResult.Persisted;
    }

    private static Dictionary<string, ModState> CopyState(IReadOnlyDictionary<string, ModState> source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(source);

        var copy = new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, state) in source)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Launch observation state contains an empty mod ID.", nameof(source));
            if (!copy.TryAdd(id, state))
                throw new ArgumentException($"Launch observation state contains duplicate mod ID '{id}' under case-insensitive identity.", nameof(source));
        }

        return copy;
    }

    private static async Task<ExistingLaunch?> ReadExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT started_at,ended_at,mode,game_build_sha256,success,exit_code,startup_survived,state_json,details
            FROM launch_history
            WHERE id=$id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt64(4) != 0,
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetInt64(6) != 0,
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static async Task InsertHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LaunchObservationRecord observation,
        string stateJson,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO launch_history(
                id,started_at,ended_at,mode,game_build_sha256,success,exit_code,startup_survived,state_json,details)
            VALUES($i,$s,$e,$m,$g,$ok,$x,$v,$j,$d)
            """;
        cmd.Parameters.AddWithValue("$i", observation.Id);
        cmd.Parameters.AddWithValue("$s", observation.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$e", observation.EndedAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$m", observation.Mode.ToString());
        cmd.Parameters.AddWithValue("$g", (object?)observation.GameBuildSha256 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ok", observation.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("$x", (object?)observation.ExitCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$v", observation.StartupSurvived ? 1 : 0);
        cmd.Parameters.AddWithValue("$j", stateJson);
        cmd.Parameters.AddWithValue("$d", observation.Details);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static bool Matches(
        ExistingLaunch existing,
        LaunchObservationRecord observation,
        IReadOnlyDictionary<string, ModState> expectedState)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return string.Equals(existing.StartedAt, observation.StartedAt.ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            && string.Equals(existing.EndedAt, observation.EndedAt.ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            && string.Equals(existing.Mode, observation.Mode.ToString(), StringComparison.Ordinal)
            && string.Equals(existing.GameBuildSha256, observation.GameBuildSha256, StringComparison.Ordinal)
            && existing.Success == observation.Success
            && existing.ExitCode == observation.ExitCode
            && existing.StartupSurvived == observation.StartupSurvived
            && string.Equals(existing.Details, observation.Details, StringComparison.Ordinal)
            && StateMatches(existing.StateJson, expectedState);
    }

    private static bool StateMatches(string json, IReadOnlyDictionary<string, ModState> expected)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, ModState>>(json, AutomationJson.Options);
            if (parsed is null || parsed.Count != expected.Count)
                return false;

            var normalized = new Dictionary<string, ModState>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, state) in parsed)
            {
                if (!normalized.TryAdd(id, state))
                    return false;
            }

            if (normalized.Count != expected.Count)
                return false;

            foreach (var (id, state) in expected)
            {
                if (!normalized.TryGetValue(id, out var actual) || actual != state)
                    return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ExistingLaunch(
        string StartedAt,
        string? EndedAt,
        string Mode,
        string? GameBuildSha256,
        bool Success,
        int? ExitCode,
        bool StartupSurvived,
        string StateJson,
        string? Details);
}
