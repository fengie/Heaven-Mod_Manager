# v8.8.97 CurseForge Browse Mods pagination — handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Primary active catalog issue: #558
Integration PR: #773

## Candidate behavior

- v8.8.97 keeps v8.8.96's provider-neutral continuation and serialized **Load More** path and adds CurseForge as another real paged provider.
- CurseForge continuation is the official search index, persisted as an opaque non-negative cursor and rejected before transport when malformed or negative.
- Next-page availability is derived from authoritative response pagination metadata and stops at the reported total count.
- Existing CurseForge search/acquisition, capability checks, credential isolation, rate-limit health, schema-drift handling, and unsupported-provider behavior remain unchanged.
- #558 remains open for remaining provider breadth and deterministic end-to-end scale/performance acceptance.

## Verification boundary

The v8.8.97 source-only head `b0db52d5b2032af496717d78e675d85c3abc2705` passed Workflow Feature PR Gate run `37244704017` after repairing CA1861 without weakening analyzers.

Release and continuity synchronization changes the candidate SHA, so that earlier green run is historical evidence only. Require fresh exact-final-head required PR gates before integration and fresh canonical hosted-Windows verification after integration. The last closed canonical Windows source remains v8.8.96 `7c079eaec9f71d93eced66f320ca78f1747bdd11` / run `37239871430`.

## Unresolved risk and remaining #558 acceptance

- v8.8.96 delivered durable GameBanana continuation and the shared Browse Mods Load More path.
- v8.8.97 extends that same continuation contract to CurseForge rather than creating parallel UI/state machinery.
- Keep #558 open for remaining provider/result breadth and deterministic end-to-end scale/performance acceptance.

## Unresolved risk

- #558 remains open after this tranche: remaining provider/result breadth and deterministic end-to-end scale/performance acceptance are not yet proven.
- v8.8.97 is not canonically closed until the exact final PR head is admitted, integrated, and the resulting canonical source passes fresh hosted-Windows verification.

## Independent open work

- #735 remains open for installed enable/disable + Auto Populate UI/persistence timing, transitive recomputation evidence, and a durable responsiveness budget.
- Preserve updater, filesystem, conflict/dependency/override, exact-SDK, release-provenance, and durable-evidence safety invariants.

## Next action

1. Require fresh exact-final-head Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership admission for PR #773.
2. Reconcile current canonical main immediately before integration; merge only the exact admitted v8.8.97 candidate and verify remote-main tree/readback.
3. Obtain and persist fresh canonical hosted-Windows verification for integrated v8.8.97, then publish immediately if the repository's release gate makes the version release-ready.
4. Keep #558 open until its remaining multi-provider breadth and end-to-end scale/performance acceptance is actually evidenced.

## Successor obligation

As the successor, you must preserve and propagate this continuity contract to the agent after you, and require that agent to pass it to the next successor. Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW main; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and all safety invariants above. Before finishing, update the repository handoff with the same explicit recursive requirement.

**Do not break the chain.**
