using MhwModManager.Core;
using System.Text.Json;
using MhwModManager.Storage;

namespace MhwModManager.Automation;

public sealed class LastKnownGoodService(ManagerDatabase db)
{
    private const string Key = "automation:last-known-good";

    public async Task RecordAsync(string? snapshotRoot, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var mods = await db.GetModsAsync(ct);
        var build = await db.GetGameBuildFingerprintAsync(ct);
        var state = new LastKnownGoodState(DateTimeOffset.UtcNow, snapshotRoot, build?.Sha256,
            mods.ToDictionary(x => x.Id, x => new ModState(x.Enabled, x.Priority), StringComparer.OrdinalIgnoreCase));
        await db.SetSettingAsync(Key, JsonSerializer.Serialize(state, AutomationJson.Options), ct);
    }

    public async Task<LastKnownGoodState?> LoadAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var json = await db.GetSettingAsync(Key, ct);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LastKnownGoodState>(json, AutomationJson.Options);
    }

    public async Task<int> StageRestoreAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var state = await LoadAsync(ct) ?? throw new InvalidOperationException("No last-known-good launch has been recorded yet.");
        var mapped = state.Mods.ToDictionary(x => x.Key, x => (x.Value.Enabled, x.Value.Priority), StringComparer.OrdinalIgnoreCase);
        await db.SetEnabledAndPriorityAsync(mapped, ct);
        return mapped.Count;
    }

    public async Task<IReadOnlyList<string>> ChangedSinceLastGoodAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var known = await LoadAsync(ct);
        if (known is null) return [];
        var current = await db.GetModsAsync(ct);
        var currentById = current.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        return known.Mods.Keys
            .Concat(currentById.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id =>
            {
                var hasKnown = known.Mods.TryGetValue(id, out var old);
                var hasCurrent = currentById.TryGetValue(id, out var now);
                return !hasKnown || !hasCurrent || old!.Enabled != now!.Enabled || old.Priority != now.Priority;
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
