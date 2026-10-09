# v8.8.99 canonical continuity and Windows closure — handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical branch: `main`
Current product version: **8.8.99**

## Current behavior

- v8.8.99 makes concurrent SDK-root isolation verification hermetic while preserving exact SDK pinning and supply-chain enforcement.
- v8.8.98's exhaustive Browse Mods first-run indexing remains integrated: a pageable provider with no saved cursor starts at page 1, resumes persisted continuations, and rejects repeated/empty continuation states.
- #558 remains open for provider breadth and deterministic end-to-end catalog scale/performance acceptance. #735 remains open for installed enable/disable and Auto Populate timing/responsiveness acceptance.

## Verification boundary

Current hosted-Windows closure: v8.8.99 source `c08b31c5327522c5896704d7a24b5ddc6f70b9c4` passed run `38006379575` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.99-heaven-windows-closure.log`.

The tested source remains `c08b31c5327522c5896704d7a24b5ddc6f70b9c4` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Next action

1. Refresh canonical main, `_AGENT_CONTEXT/CURRENT_REVISION.json`, live ownership and exact-source evidence.
2. If current-source Windows closure is absent, explicitly dispatch `.github/workflows/windows-release-gate.yml` on `main` with `force_publish=false`. These context-only changes are outside its push path filters; integration alone does not trigger closure.
3. Require the full verification/build/release policy to pass, then let `Sync-VerificationContinuity.ps1` persist the exact tested source/run. Do not promote historical verification manually.
4. Once closure is recorded, follow the revision ledger and live project plan; do not rerun an already-verified source solely because an evidence-only commit advanced main.
5. Keep #558, #735 and independent backlog open until their own acceptance is evidenced.

## Unresolved risk

Until successful exact-source evidence is recorded, fresh v8.8.99 Windows closure and publication remain unproved. Preserve filesystem, SQLite transaction, conflict/dependency/override, updater publication/parity/provenance, exact-SDK and durable-evidence privacy invariants. External signing/ruleset and installed runtime acceptance remain governed by the live project plan.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW main. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, the active `_AGENT_CONTEXT/LEARNED_RULES.md`, and the current project plan; retrieve only task-relevant context. Preserve exact-input verification and all product/release safety invariants. The next agent must propagate this continuity contract to the agent after it. Before finishing, leave an equally exact successor handoff with that same recursive obligation. Do not break the chain.
