# v8.8.94 bounded Auto Populate planning — handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Primary open performance issue: #735

## v8.8.94 behavior

- PR #737 bounded sparse Auto Populate planning so 8/64/256-mod isolated-library regressions use one mandatory final full planner pass rather than candidate-by-candidate full replanning.
- Exact-path/topology interactions, explicit enabled pair rules, and MHW structural atomic-bundle interactions retain the full-planner fallback.
- Final dependency validation and a complete full deployment plan remain mandatory.
- Planner passes, isolated fast-path candidates, and planner milliseconds are recorded for post-main measurement.
- The duplicate Mod Library analysis immediately before Apply is gone; Apply still re-plans and validates current state before game-file mutation.

## Verification boundary

PR #737 exact head `a61335fe612fd46a0432effc23add513bd29f1c4` passed Workflow Feature PR Gate run `37189506341`, including 26/26 repository verification stages, product/update/release security policy, 112 automation tests, and 381 integration/fault-injection tests.

The squash integration source `3e3702db2b04005693845a40713f0301fb11cdd1` reached canonical main. Windows Release Gate run `37214194618` repeated the substantive checks successfully but failed the canonical handoff invariant because version/continuity metadata still reported 8.8.92. The convergence repair aligns those surfaces to 8.8.94 without widening performance implementation. Do not call v8.8.94 release-verified until a fresh exact-main Windows Release Gate passes after the repair.

## Unresolved risk

- #735 is **not closed** by #737. The slice proves bounded Auto Populate planner-pass complexity, not the full user-visible performance objective.
- Real post-main before/after latency for enable, disable, and Auto Populate is still required.
- Single enable/disable transitive recomputation and UI responsiveness remain acceptance gaps.
- Do not select another optimization until profiling identifies the post-#737 dominant cost.
- Preserve dependency, conflict, override, protected-anchor, persisted-state, filesystem, updater, and release-safety invariants while measuring.

## Next action

1. Refresh Toolbox main, MHW canonical main, open PRs/issues, and ownership.
2. Require a fresh exact-main Windows Release Gate for the 8.8.94 metadata-parity repair and verify remote-main readback.
3. Profile the actual #735 enable/disable + Auto Populate workflow on the integrated build using the new planner pass/millisecond evidence plus phase timings for dependency/conflict work, persistence/I/O, Apply, and UI refresh.
4. If latency is acceptable, reconcile/close #735 against every acceptance criterion. If material slowness remains, choose only the measured dominant bottleneck and land the next smallest coherent slice.

## Execution/offload note

When a callable Heaven/Agent Control execution surface is unavailable in the current session, use repository CI/GitHub evidence and do not fabricate local execution.

## Successor obligation

As the successor, you must propagate this continuity contract to the next successor.

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW main; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and all safety invariants above. Before finishing, update the repository handoff and require your successor to inherit, preserve, and recursively propagate these same rules.

**Do not break the chain.**
