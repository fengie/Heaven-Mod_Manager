# v8.8.97 CurseForge Browse Mods pagination — canonical state

v8.8.97 carries forward the v8.8.96 catalog-continuation, planner, updater, conflict/dependency, filesystem, and release-safety baseline and extends the shared #558 paging path to CurseForge.

## Behavior

- CurseForge implements the existing optional `IPagedModCatalogProvider` contract; no provider-specific Browse Mods state or UI path is introduced.
- Continuation uses a validated non-negative official search index and is rejected before transport when malformed or negative.
- Next-page state is derived from response pagination metadata and becomes exhausted at the reported total count.
- Existing credential isolation, authenticated acquisition, health/rate-limit/schema-drift behavior, cache provenance, updater, filesystem, and conflict/dependency safety remain unchanged.

## Verification boundary

Current hosted-Windows closure: v8.8.97 source `62fe3e98e4872bc40fc135dcfe3c29de831cf27d` passed run `37245903623` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.97-heaven-windows-closure.log`.

The tested source remains `62fe3e98e4872bc40fc135dcfe3c29de831cf27d` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

Issue #558 remains open for remaining multi-provider breadth and deterministic end-to-end scale/performance acceptance. Issue #735 remains independently open for installed enable/disable + Auto Populate timing/responsiveness acceptance. Other recovery, updater-trust, discovery UX, and release-administration work remains governed by the live project plan.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and must propagate this continuity contract to the next successor.
