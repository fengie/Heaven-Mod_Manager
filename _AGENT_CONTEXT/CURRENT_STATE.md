# v8.8.92 unified disposable storage reclaim — canonical state

v8.8.92 carries forward the verified v8.8.91 storage-lifecycle baseline and adds the integrated #731 unified user-facing reclaim action across fail-closed updater staging/recovery residue and manager-owned catalog-download scratch. This canonical-state repair also aligns the release metadata that feature-candidate verification intentionally defers until integration.

## Behavior

- Settings exposes one **Reclaim disposable storage** action rather than an updater-only action.
- Updater cleanup remains limited to proven-safe orphan staging plus terminal/abandoned recovery state.
- Catalog-download scratch cleanup remains limited to manager-owned lease state with liveness, age, 512 MiB quota, unknown-file, and reparse-point protections.
- Installed Mods, archives, State, managed content-addressed blobs, active work, ambiguous ownership, unknown paths, and reparse targets remain preserved.
- The UI reports aggregate and per-domain reclaimed bytes plus removed/deferred counts.
- The v8.8.91 catalog/updater lease-lifecycle protections and all updater provenance, filesystem containment, exact-SDK, compliance, and durable-evidence safeguards remain in force.

## Verification boundary

Current hosted-Windows closure: v8.8.92 source `ecb6a80cb84c6dc4d3c54df63f986ece566847f5` passed run `37177931633` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.92-heaven-windows-closure.log`.

The tested source remains `ecb6a80cb84c6dc4d3c54df63f986ece566847f5` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

Issue #669 remains open for broader lifecycle/generation bounds and immutable-content reuse. Keep #559/#558 catalog discovery/browse scale, #350/#354 external trust/admin prerequisites, #709 reproducible NuGet restore, and remaining recovery/installed-Windows acceptance independent unless current live state proves them resolved.

Every successor must preserve the permanent continuity constitution, exact-input verification, storage ownership/cleanup invariants, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and must propagate this continuity contract to the next successor.
