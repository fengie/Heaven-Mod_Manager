using MhwModManager.Core;
using System.Diagnostics.CodeAnalysis;

namespace MhwModManager.Automation;

public sealed class CrashBisectorEngine
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The bisector is intentionally injectable/testable as a service.")]
    public async Task<CrashBisectResult> RunAsync(IReadOnlyList<string> suspects, Func<IReadOnlySet<string>, CancellationToken, Task<bool>> reproducesCrash, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"suspects={suspects.Count}");
        if (suspects.Count == 0) return new(false, [], 0, "No changed mods are available to bisect.");
        var remaining = suspects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var probes = 0;

        ct.ThrowIfCancellationRequested();
        probes++;
        if (await reproducesCrash(new HashSet<string>(StringComparer.OrdinalIgnoreCase), ct))
            return new(false, [], probes, "The baseline/control reproduces the failure, so the diagnosis is invalid and no culprit can be confirmed.");

        ct.ThrowIfCancellationRequested();
        var fullSet = remaining.ToHashSet(StringComparer.OrdinalIgnoreCase);
        probes++;
        if (!await reproducesCrash(fullSet, ct))
            return new(false, [], probes, "The full suspect set does not reproduce the failure, so the issue is not currently reproducible and no culprit can be confirmed.");

        while (remaining.Count > 1)
        {
            ct.ThrowIfCancellationRequested();
            var take = Math.Max(1, remaining.Count / 2);
            var left = remaining.Take(take).ToHashSet(StringComparer.OrdinalIgnoreCase);
            probes++;
            if (await reproducesCrash(left, ct)) { remaining = left.ToList(); continue; }
            var right = remaining.Skip(take).ToHashSet(StringComparer.OrdinalIgnoreCase);
            probes++;
            if (right.Count > 0 && await reproducesCrash(right, ct)) { remaining = right.ToList(); continue; }
            return new(false, remaining, probes, "The failure does not reproduce with either half independently; this suggests an interaction between mods rather than one isolated culprit.");
        }
        return new(true, remaining, probes, "Isolated the smallest reproducible suspect set.");
    }
}
