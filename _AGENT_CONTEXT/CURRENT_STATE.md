# v8.8.94 bounded Auto Populate planning — canonical state

v8.8.94 carries forward the verified storage, updater, conflict/dependency, filesystem, and release-safety baseline and integrates the first measured #735 performance slice from PR #737.

## Behavior

- Sparse Auto Populate no longer rebuilds the full deployment plan once per isolated candidate. Regression fixtures at 8, 64, and 256 installed mods require one final full planner pass independent of library size.
- Exact-path overlap, file/directory topology interaction, enabled explicit pair rules, and MHW structural atomic-bundle interaction still fall back to the full planner.
- A complete final deployment plan plus dependency validation remains mandatory before Auto Populate returns a staged setup.
- Auto Populate records full planner passes, isolated fast-path candidates, and planner milliseconds in its timeline/result evidence.
- The redundant Mod Library analysis rebuild immediately before Apply is removed; Apply still owns an authoritative current-state planner/dependency preflight before any game-file mutation.
- Dependency, conflict, override, protected-anchor, and persisted-state safety semantics remain conservative and exact-planner-backed at the final boundary.

## Verification boundary

Current hosted-Windows closure: v8.8.94 source `2311b658762c8fe51c21919926fdabc870147a0e` passed run `37230142789` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.94-heaven-windows-closure.log`.

The tested source remains `2311b658762c8fe51c21919926fdabc870147a0e` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

Issue #735 remains open. The integrated slice proves bounded Auto Populate planner-pass complexity, but it does not yet prove user-visible before/after latency, single enable/disable transitive recomputation, or UI responsiveness under the real workload. Re-measure the exact post-main scenario and choose any next optimization only from the measured dominant cost; do not begin a broad performance rewrite.

Other independent backlog, recovery, updater-trust, catalog-scale, and release-administration work remains governed by the live project plan and current issue/ownership state.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and must propagate this continuity contract to the next successor.
