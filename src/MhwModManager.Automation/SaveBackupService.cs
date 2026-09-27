using MhwModManager.Core;
using System.Globalization;
using System.Text.Json;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class SaveBackupService(ManagerDatabase db, string stateRoot, GameProfile game)
{
    public string SnapshotRoot { get; } = Path.Combine(stateRoot, "Automation", "Snapshots");

    public async Task<SaveSnapshotResult> CreateAsync(string reason, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var root = Path.Combine(SnapshotRoot, id);
        Directory.CreateDirectory(root);
        var source = FindSaveFile(game);
        var copied = 0;
        if (source is not null)
        {
            var saves = Path.Combine(root, "save");
            Directory.CreateDirectory(saves);
            await CopyFileAsync(source, Path.Combine(saves, Path.GetFileName(source)), ct);
            copied++;
        }

        var mods = await db.GetModsAsync(ct);
        var state = mods.ToDictionary(x => x.Id, x => new ModState(x.Enabled, x.Priority), StringComparer.OrdinalIgnoreCase);
        var build = await db.GetGameBuildFingerprintAsync(ct);
        var manifest = await ReadManifestAsync(ct);
        var metadata = new
        {
            id,
            reason,
            createdAt = DateTimeOffset.UtcNow,
            saveSource = source,
            gameBuildSha256 = build?.Sha256,
            mods = state,
            manifest
        };
        await File.WriteAllTextAsync(Path.Combine(root, "snapshot.json"), JsonSerializer.Serialize(metadata, AutomationJson.Options), ct);
        await RecordSnapshotAsync(id, reason, root, source, state, build?.Sha256, manifest, ct);
        Prune(30);
        return new(true, id, root, source, copied, source is null ? $"Snapshot created for {game.DisplayName}; no configured save file was found, so mod/deployment state only was captured." : $"{game.DisplayName} save + mod/deployment snapshot created.");
    }

    public static string? FindSaveFile(GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"game={game.DisplayName}");
        if (!string.IsNullOrWhiteSpace(game.SavePath) && File.Exists(game.SavePath)) return game.SavePath;
        var explicitPath = Environment.GetEnvironmentVariable("MOD_MANAGER_SAVE_PATH") ?? Environment.GetEnvironmentVariable("MHW_SAVE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;
        if (!game.AdapterId.Equals("mhw", StringComparison.OrdinalIgnoreCase)) return null;
        var steamRoots = new List<string>();
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86)) steamRoots.Add(Path.Combine(pf86, "Steam", "userdata"));
        var steam = Environment.GetEnvironmentVariable("STEAM_PATH");
        if (!string.IsNullOrWhiteSpace(steam)) steamRoots.Add(Path.Combine(steam, "userdata"));
        foreach (var root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var user in Directory.EnumerateDirectories(root))
            {
                var candidate = Path.Combine(user, "582010", "remote", "SAVEDATA1000");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private async Task<Dictionary<string, string?>> ReadManifestAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path,expected_live_sha256 FROM deployment_manifest ORDER BY path";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) map[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
        return map;
    }

    private async Task RecordSnapshotAsync(string id, string reason, string root, string? saveSource, Dictionary<string, ModState> state, string? buildSha, object manifest, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO save_snapshots(id,created_at,reason,root_path,save_source,mod_state_json,game_build_sha256,manifest_json,success) VALUES($i,$t,$r,$p,$s,$m,$g,$f,1)";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$r", reason);
        cmd.Parameters.AddWithValue("$p", root);
        cmd.Parameters.AddWithValue("$s", (object?)saveSource ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$m", JsonSerializer.Serialize(state, AutomationJson.Options));
        cmd.Parameters.AddWithValue("$g", (object?)buildSha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", JsonSerializer.Serialize(manifest, AutomationJson.Options));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, 1024 * 1024, ct);
        await output.FlushAsync(ct);
    }

    private void Prune(int keep)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!Directory.Exists(SnapshotRoot)) return;
        foreach (var dir in Directory.EnumerateDirectories(SnapshotRoot).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).Skip(keep))
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
