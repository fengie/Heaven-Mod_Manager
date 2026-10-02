using MhwModManager.Core;
using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
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
        var recorded = false;
        try
        {
            var source = FindSaveFile(game);
            var copied = 0;
            if (source is not null)
            {
                var saves = Path.Combine(root, "save");
                Directory.CreateDirectory(saves);
                await CopyStableFileAsync(source, Path.Combine(saves, Path.GetFileName(source)), ct);
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
            recorded = true;
            await PruneAsync(30, ct);
            return new(true, id, root, source, copied, source is null ? $"Snapshot created for {game.DisplayName}; no configured save file was found, so mod/deployment state only was captured." : $"{game.DisplayName} save + mod/deployment snapshot created.");
        }
        catch
        {
            if (!recorded) TryDeleteSnapshotDirectory(root);
            throw;
        }
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

    internal static async Task CopyStableFileAsync(
        string source,
        string destination,
        CancellationToken ct,
        Func<int, CancellationToken, Task>? afterInitialFingerprint = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                var before = await FingerprintAsync(source, ct);
                if (afterInitialFingerprint is not null)
                    await afterInitialFingerprint(attempt, ct);

                await CopyFileOnceAsync(source, temporary, ct);

                var after = await FingerprintAsync(source, ct);
                var copiedHash = await HashFileAsync(temporary, FileShare.Read, ct);
                if (before.Length == after.Length
                    && before.LastWriteTimeUtc == after.LastWriteTimeUtc
                    && CryptographicOperations.FixedTimeEquals(before.Sha256, after.Sha256)
                    && CryptographicOperations.FixedTimeEquals(after.Sha256, copiedHash))
                {
                    File.Move(temporary, destination);
                    return;
                }
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                // The game/Steam may have replaced or rewritten the save mid-read.
                // Retry from a fresh source observation instead of certifying a torn copy.
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; the snapshot is never indexed unless capture succeeds.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup; preserve the primary capture failure.
                }
            }
        }

        throw new IOException($"Save file '{source}' did not remain stable long enough to create a coherent snapshot.");
    }

    private static async Task<FileFingerprint> FingerprintAsync(string path, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists)
            throw new FileNotFoundException("Save file disappeared while a snapshot was being captured.", path);

        var length = info.Length;
        var lastWriteTimeUtc = info.LastWriteTimeUtc;
        var sha256 = await HashFileAsync(path, FileShare.ReadWrite | FileShare.Delete, ct);

        info.Refresh();
        if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != lastWriteTimeUtc)
            throw new IOException($"Save file '{path}' changed while it was being fingerprinted.");

        return new FileFingerprint(length, lastWriteTimeUtc, sha256);
    }

    private static async Task<byte[]> HashFileAsync(string path, FileShare share, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, share, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return await sha.ComputeHashAsync(input, ct);
    }

    private static async Task CopyFileOnceAsync(string source, string destination, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, 1024 * 1024, ct);
        await output.FlushAsync(ct);
    }

    private static void TryDeleteSnapshotDirectory(string root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        catch (IOException)
        {
            // Cleanup must not mask the capture failure; no successful DB row was written.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not mask the capture failure; no successful DB row was written.
        }
    }

    private sealed record FileFingerprint(long Length, DateTime LastWriteTimeUtc, byte[] Sha256);

    private async Task PruneAsync(int keep, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentOutOfRangeException.ThrowIfNegative(keep);

        var fullSnapshotRoot = Path.GetFullPath(SnapshotRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(SnapshotRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(SnapshotRoot))
                directories.Add(Path.GetFullPath(dir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var snapshots = new List<(string Id, string RootPath)>();
        await using (var c = await db.OpenAsync(ct))
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id,root_path FROM save_snapshots ORDER BY created_at DESC,id DESC";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) snapshots.Add((r.GetString(0), r.GetString(1)));
        }

        var retained = 0;
        foreach (var snapshot in snapshots)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryNormalizeOwnedSnapshotDirectory(fullSnapshotRoot, snapshot.RootPath, out var fullPath)
                || !directories.Contains(fullPath))
            {
                await DeleteSnapshotRecordAsync(snapshot.Id, ct);
                continue;
            }

            try
            {
                if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                {
                    await DeleteSnapshotRecordAsync(snapshot.Id, ct);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (retained++ < keep) continue;

            try
            {
                Directory.Delete(fullPath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            await DeleteSnapshotRecordAsync(snapshot.Id, ct);
            directories.Remove(fullPath);
        }
    }

    private static bool TryNormalizeOwnedSnapshotDirectory(string fullSnapshotRoot, string candidate, out string fullPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        fullPath = string.Empty;
        try
        {
            var normalized = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(normalized);
            if (parent is null
                || !string.Equals(
                    parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    fullSnapshotRoot,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            fullPath = normalized;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task DeleteSnapshotRecordAsync(string id, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM save_snapshots WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
