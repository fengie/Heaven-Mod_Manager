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

Current hosted-Windows closure: v8.8.94 source `c8465fc28c48878ae9efc57b417493a3e9285b5a` passed run `37215524276` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.94-heaven-windows-closure.log`.

The tested source remains `c8465fc28c48878ae9efc57b417493a3e9285b5a` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

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
