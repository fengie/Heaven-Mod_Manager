# v8.8.99 hermetic SDK-isolation verification — canonical state

v8.8.99 is the canonical MHW Manual Mod Manager baseline. It preserves v8.8.98's exhaustive Browse Mods first-run indexing and makes the concurrent .NET SDK isolation regression hermetic without weakening exact SDK or supply-chain enforcement.

## Behavior

- Pageable providers with no saved cursor seed page 1 and follow durable continuations to exhaustion; saved cursors resume and exhausted scopes are local no-ops.
- Repeated cursors and empty continuation pages fail closed.
- Shared GameBanana/CurseForge continuation, serialized Load More, credential isolation, acquisition, health and schema-drift boundaries remain intact.
- #558 remains open for provider/result breadth and deterministic end-to-end scale/performance acceptance.

## Verification boundary

Current hosted-Windows closure: v8.8.99 source `c08b31c5327522c5896704d7a24b5ddc6f70b9c4` passed run `38006379575` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.99-heaven-windows-closure.log`.

The tested source remains `c08b31c5327522c5896704d7a24b5ddc6f70b9c4` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Next required action

Refresh main, ownership and `CURRENT_REVISION.json`. While current-source Windows closure is absent, explicitly dispatch the Windows Release Gate on canonical `main` with `force_publish=false`; context-only changes do not trigger its push paths. Allow the trusted gate to synchronize evidence only after success. Once closure exists, follow the revision ledger and project plan instead of reintegrating stale PRs or repeating an already-verified source for an evidence-only commit.

## Unresolved risk and independent work

- #558 remains open for catalog breadth and deterministic end-to-end scale/performance acceptance.
- #735 remains open for installed enable/disable and Auto Populate UI/persistence timings, transitive recomputation evidence and a responsiveness budget.
- Recovery, updater trust, discovery UX, release administration and external signing/ruleset work remain governed by the live project plan.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy and durable-evidence privacy. The next agent must propagate this continuity contract to the agent after it.
