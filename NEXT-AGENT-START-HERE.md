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

Current hosted-Windows closure: v8.8.97 source `54d3a740e47559d1a280a563f4cd4f929c2a0859` passed run `37255940374` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.97-heaven-windows-closure.log`.

The tested source remains `54d3a740e47559d1a280a563f4cd4f929c2a0859` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

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
