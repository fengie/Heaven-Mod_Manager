# v8.8.97 CurseForge Browse Mods pagination — canonical state

v8.8.97 carries forward the v8.8.96 catalog-continuation, planner, updater, conflict/dependency, filesystem, and release-safety baseline and extends the shared #558 paging path to CurseForge.

## Behavior

- CurseForge implements the existing optional `IPagedModCatalogProvider` contract; no provider-specific Browse Mods state or UI path is introduced.
- Continuation uses a validated non-negative official search index and is rejected before transport when malformed or negative.
- Next-page state is derived from response pagination metadata and becomes exhausted at the reported total count.
- Existing credential isolation, authenticated acquisition, health/rate-limit/schema-drift behavior, cache provenance, updater, filesystem, and conflict/dependency safety remain unchanged.

## Verification boundary

The source-only v8.8.97 head `b0db52d5b2032af496717d78e675d85c3abc2705` passed Workflow Feature PR Gate `37244704017`. Release/continuity synchronization changes the exact candidate and therefore requires fresh exact-final-head PR gates.

The last closed canonical hosted-Windows source remains v8.8.96 `7c079eaec9f71d93eced66f320ca78f1747bdd11` / run `37239871430`; fresh post-integration Windows verification is required for v8.8.97.

## Remaining independent work

Issue #558 remains open for remaining multi-provider breadth and deterministic end-to-end scale/performance acceptance. Issue #735 remains independently open for installed enable/disable + Auto Populate timing/responsiveness acceptance. Other recovery, updater-trust, discovery UX, and release-administration work remains governed by the live project plan.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and must propagate this continuity contract to the next successor.
