# v8.8.99 hermetic SDK-isolation verification — canonical state

v8.8.99 is the canonical MHW Manual Mod Manager baseline. It preserves v8.8.98's exhaustive Browse Mods first-run indexing and makes the concurrent .NET SDK isolation regression hermetic without weakening exact SDK or supply-chain enforcement.

## Behavior

- Pageable providers with no saved cursor seed page 1 and follow durable continuations to exhaustion; saved cursors resume and exhausted scopes are local no-ops.
- Repeated cursors and empty continuation pages fail closed.
- Shared GameBanana/CurseForge continuation, serialized Load More, credential isolation, acquisition, health and schema-drift boundaries remain intact.
- #558 remains open for provider/result breadth and deterministic end-to-end scale/performance acceptance.

## Verification boundary

The latest completed hosted-Windows closure remains v8.8.97 source `54d3a740e47559d1a280a563f4cd4f929c2a0859`, run `37255940374`, with 0 failed checks. Evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.97-heaven-windows-closure.log`.

This is historical proof, not v8.8.99 closure. The earlier v8.8.99 source `8b7add1181f3ffcec642c2d524cf1b51bc12e9b4` stopped at continuity verification. The repaired canonical source needs exact-source Windows closure before release completion. Evidence-only persistence must preserve the tested SHA/run.

## Next required action

Refresh main, ownership and `CURRENT_REVISION.json`. While current-source Windows closure is absent, explicitly dispatch the Windows Release Gate on canonical `main` with `force_publish=false`; context-only changes do not trigger its push paths. Allow the trusted gate to synchronize evidence only after success. Once closure exists, follow the revision ledger and project plan instead of reintegrating stale PRs or repeating an already-verified source for an evidence-only commit.

## Unresolved risk and independent work

- #558 remains open for catalog breadth and deterministic end-to-end scale/performance acceptance.
- #735 remains open for installed enable/disable and Auto Populate UI/persistence timings, transitive recomputation evidence and a responsiveness budget.
- Recovery, updater trust, discovery UX, release administration and external signing/ruleset work remain governed by the live project plan.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy and durable-evidence privacy. The next agent must propagate this continuity contract to the agent after it.
