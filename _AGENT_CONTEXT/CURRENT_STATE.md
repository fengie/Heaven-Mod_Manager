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

PR #737 exact head `a61335fe612fd46a0432effc23add513bd29f1c4` passed Workflow Feature PR Gate run `37189506341`: the repository verifier completed 26/26 stages, including product/update/release security policy, 112 automation tests, and 381 integration/fault-injection tests.

The squash integration source `3e3702db2b04005693845a40713f0301fb11cdd1` reached canonical main. Windows Release Gate run `37214194618` re-ran the substantive build/security/test work successfully but correctly failed canonical metadata parity because `Directory.Build.props` and continuity surfaces still carried 8.8.92. That continuity/version drift is being repaired without changing the performance implementation. v8.8.94 is not release-closed until a fresh exact-main Windows Release Gate passes after that repair.

## Remaining independent work

Issue #735 remains open. The integrated slice proves bounded Auto Populate planner-pass complexity, but it does not yet prove user-visible before/after latency, single enable/disable transitive recomputation, or UI responsiveness under the real workload. Re-measure the exact post-main scenario and choose any next optimization only from the measured dominant cost; do not begin a broad performance rewrite.

Other independent backlog, recovery, updater-trust, catalog-scale, and release-administration work remains governed by the live project plan and current issue/ownership state.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and must propagate this continuity contract to the next successor.
