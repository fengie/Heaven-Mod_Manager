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
        var control = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        probes++;
        if (await reproducesCrash(control, ct))
            return new(false, remaining, probes, "The current control state reproduces the failure, so the diagnosis baseline is invalid and no culprit can be isolated.");

        ct.ThrowIfCancellationRequested();
        var full = remaining.ToHashSet(StringComparer.OrdinalIgnoreCase);
        probes++;
        if (!await reproducesCrash(full, ct))
            return new(false, remaining, probes, "The failure does not reproduce with the full current suspect set, so no culprit can be isolated.");

        // Reduce to a 1-minimal reproducing set. Removing one suspect at a time is
        // deliberate: failures can require an interaction whose members land in opposite
        // halves, which a plain binary split cannot discover.
        var changed = true;
        while (changed && remaining.Count > 1)
        {
            changed = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = remaining
                    .Where((_, index) => index != i)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (candidate.Count == 0) continue;

                probes++;
                if (!await reproducesCrash(candidate, ct)) continue;

                remaining = candidate.Order(StringComparer.OrdinalIgnoreCase).ToList();
                changed = true;
                break;
            }
        }

        var isolated = remaining.Count == 1;
        return new(
            isolated,
            remaining,
            probes,
            isolated
                ? "Isolated a single reproducible culprit after validating the control and full-suspect probes."
                : "Reduced the failure to a 1-minimal interacting suspect set; no single culprit reproduces the failure alone.");
    }
}
