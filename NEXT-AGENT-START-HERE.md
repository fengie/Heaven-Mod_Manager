# v8.8.92 unified disposable storage reclaim — handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #669

## v8.8.92 behavior

- Settings exposes one **Reclaim disposable storage** action rather than an updater-only action.
- The action composes the existing fail-closed `UpdateStorageMaintenance` and `CatalogDownloadMaintenance` boundaries.
- Updater cleanup remains limited to proven-safe orphan staging and terminal/abandoned recovery state.
- Catalog scratch cleanup remains limited to manager-owned lease state with liveness, age, 512 MiB quota, unknown-file, and reparse-point protections.
- Installed Mods, archives, State, managed content-addressed blobs, active work, ambiguous ownership, and unknown paths remain preserved.
- The UI reports aggregate/per-domain reclaimed bytes plus removed/deferred counts.

## Verification boundary

Current hosted-Windows closure: v8.8.92 source `ecb6a80cb84c6dc4d3c54df63f986ece566847f5` passed run `37177931633` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.92-heaven-windows-closure.log`.

The tested source remains `ecb6a80cb84c6dc4d3c54df63f986ece566847f5` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk

- v8.8.92 is not verified or released until the final exact-head feature/repository gates pass, the exact candidate is integrated, and the required post-integration verification succeeds on canonical `main`.
- Issue #669 remains open after this reclaim slice: lifecycle/generation bounds and immutable-content reuse still have acceptance work beyond the unified reclaim action.

## Next action

Run the narrow storage/UI regressions first, then the required exact-head MHW gates. Integrate only the exact green head after refreshing `main`, read back remote `main`, reconcile issue #669, and retire the temporary branch after unique work is proven integrated.

## Execution/offload note

When a callable Heaven/Agent Control execution surface is unavailable in the current session, use repository CI/GitHub evidence and do not fabricate local execution.

## Successor obligation

As the successor, you must propagate this continuity contract to the next successor.

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, catalog/updater ownership and cleanup invariants, updater publication/parity/provenance, exact SDK policy, filesystem containment, and durable-evidence privacy, and propagate the same obligation to the next successor.

**Do not break the chain.**
