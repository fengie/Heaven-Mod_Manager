using MhwModManager.Core;
using System.Diagnostics.CodeAnalysis;

namespace MhwModManager.Automation;

public sealed class CrashBisectorEngine
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Injectable diagnosis service.")]
    public async Task<CrashBisectResult> RunAsync(IReadOnlyList<string> suspects, Func<IReadOnlySet<string>, CancellationToken, Task<bool>> reproducesCrash, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(suspects);
        ArgumentNullException.ThrowIfNull(reproducesCrash);
        var remaining = suspects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (remaining.Count == 0) return new(false, [], 0, "No changed mods are available to diagnose.");
        var probes = 0;
        var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
        async Task<bool> Probe(IReadOnlyList<string> ids)
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            ct.ThrowIfCancellationRequested();
            // Length prefixes avoid collisions even for imported IDs containing separators.
            var key = string.Concat(ids.Order(StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Length}:{x}"));
            if (cache.TryGetValue(key, out var cached)) return cached;
            probes++;
            var result = await reproducesCrash(ids.ToHashSet(StringComparer.OrdinalIgnoreCase), ct);
            ct.ThrowIfCancellationRequested();
            cache[key] = result;
            return result;
        }
        if (await Probe([])) return new(false, remaining, probes, "The baseline also fails. Repair the baseline before attributing this failure to changed mods.");
        if (!await Probe(remaining)) return new(false, remaining, probes, "The complete suspect set did not reproduce the failure; no culprit was confirmed.");
        var partitions = 2;
        while (remaining.Count > 1)
        {
            ct.ThrowIfCancellationRequested();
            var chunks = remaining.Chunk((remaining.Count + partitions - 1) / partitions).ToArray();
            var reduced = false;
            foreach (var chunk in chunks)
            {
                if (!await Probe(chunk)) continue;
                remaining = [.. chunk]; partitions = 2; reduced = true; break;
            }
            if (reduced) continue;
            foreach (var chunk in chunks)
            {
                var complement = remaining.Except(chunk, StringComparer.OrdinalIgnoreCase).ToArray();
                if (complement.Length == 0 || !await Probe(complement)) continue;
                remaining = [.. complement]; partitions = Math.Max(2, partitions - 1); reduced = true; break;
            }
            if (reduced) continue;
            if (partitions >= remaining.Count) break;
            partitions = Math.Min(remaining.Count, partitions * 2);
        }
        return new(true, remaining, probes, remaining.Count == 1
            ? "One reproducible suspect was isolated against a passing baseline."
            : "A 1-minimal crashing combination was isolated: removing any one member passed. Members are not individually confirmed culprits; intermittent failures can change this result.");
    }
}
