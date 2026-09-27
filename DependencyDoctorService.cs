using System.Text.Json;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class DependencyDoctorService(ManagerDatabase db, string gameRoot, GameProfile? game = null)
{
    public async Task<IReadOnlyList<DependencyStatus>> ScanAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var results = new List<DependencyStatus>();
        foreach (var mod in mods.Where(x => x.Enabled && !x.IsSuperseded))
        {
            ct.ThrowIfCancellationRequested();
            var missing = new List<string>();
            var evidence = new List<string>();
            var files = await PathsForAsync(mod.Id, ct);
            var hasNativePlugin = files.Any(p => p.Contains("nativepc\\plugins\\", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (hasNativePlugin && (game is null || game.IsMonsterHunterWorld))
            {
                evidence.Add("Contains an MHW native plugin/DLL.");
                if (!HasLoader()) missing.Add("Stracker/native plugin loader (no dinput8.dll or loader.dll detected in the game root)");
            }
            var sidecar = Path.Combine(mod.SourcePath, "mod-manager.requirements.json");
            if(!File.Exists(sidecar))sidecar = Path.Combine(mod.SourcePath, "mhw-manager.requirements.json");
            if (File.Exists(sidecar))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(sidecar, ct));
                    if (doc.RootElement.TryGetProperty("files", out var required) && required.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in required.EnumerateArray())
                        {
                            var rel = item.GetString();
                            if (string.IsNullOrWhiteSpace(rel)) continue;
                            evidence.Add("Explicit sidecar requirement: " + rel);
                            if (!File.Exists(Path.Combine(gameRoot, rel))) missing.Add(rel);
                        }
                    }
                }
                catch (JsonException ex) { missing.Add("Invalid requirements sidecar: " + ex.Message); }
            }
            if (missing.Count > 0 || evidence.Count > 0) results.Add(new(mod.Id, mod.DisplayName, missing.Count == 0, missing, evidence));
        }
        return results;
    }

    private bool HasLoader()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return File.Exists(Path.Combine(gameRoot, "dinput8.dll")) || File.Exists(Path.Combine(gameRoot, "loader.dll"));
    }

    private async Task<List<string>> PathsForAsync(string modId, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var paths = new List<string>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path FROM mod_files WHERE mod_id=$m";
        cmd.Parameters.AddWithValue("$m", modId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) paths.Add(r.GetString(0));
        return paths;
    }
}
