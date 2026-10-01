# v8.8.55 branch-cleanup recovery â€” current handoff

Canonical baseline is v8.8.54 `main`: behavior merged as `be001c2582564e55a7561a97bff791bbf821621a` after exact-head Agent Control, Security Supply Chain, and Workflow Feature gates; canonical post-merge evidence currently extends through `62cd959e00c66f034b7091f0f323ba0ccb1db8b2`.

This v8.8.55 candidate restores `_AGENT_CONTEXT/PROJECT_PLAN.md` as the canonical active/recovery ledger and closes the branch-cleanup policy gap exposed by the 2026-09-30 cleanup. Exact archived tips were preserved, but a semantic audit proved several still contained useful unique work that had not reached `main`. **ARCHIVED is therefore preservation only, never a terminal work disposition.**

## Verification and unresolved risk

Verification state: this v8.8.55 candidate requires fresh exact-head `Test-AgentHandoff.ps1` plus negative-fixture proof after every reconciliation; historical v8.8.54 green evidence does not prove these governance/verifier changes.

Unresolved risk: archived product lanes still contain unique semantics not yet canonical. Their exact source tags and finish/supersede paths are tracked in `_AGENT_CONTEXT/PROJECT_PLAN.md`; do not mistake preservation for completion.

## Ordered next actions

1. Run `scripts/testing/Test-AgentHandoff.ps1` and its negative fixtures on the exact current candidate.
2. Refresh `main` and current ownership immediately before integration; reconcile any concurrent governance changes without dropping v8.8.54 state.
3. Integrate only the verified v8.8.55 tree, then prove remote `main` contains the project ledger, extraction-first cleanup rule, LR-062, and synchronized version metadata.
4. Continue the remaining `RECOVERY-*` items from `_AGENT_CONTEXT/PROJECT_PLAN.md`; do not blindly recreate archived branches.
5. Retire stale/redundant branches only after each useful unique semantic is `INTEGRATED`, `EXTRACTED`, `SUPERSEDED`, or `REJECTED` with evidence.

## Current recovery truth

- RECOVERY-008 Agent Control coordination is implemented on v8.8.54 main; only redundant-ref cleanup remains after uniqueness proof.
- Catalog recovery is active on fresh-main branch `fix/issue281-catalog-v8.8.55`; the older `fix/issue281-catalog-browser-recovery` remains preserved until equivalence is proven.
- Manual updater check, overlapping updater/runtime hardening, dark ComboBox chrome, Heaven auth-state reporting, and universal installed-game discovery remain live recovery work with exact archive provenance in the project ledger.
- v8.8.53 Agent Work Reports still has an authenticated heaven2 runtime-smoke gap. Do not bypass HMAC to close it.

You inherit the permanent continuity constitution in `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`. Read and preserve it and active Learned Rules at `_AGENT_CONTEXT/LEARNED_RULES.md`. Before finishing, leave a current durable handoff. Your successor must inherit and preserve the constitution and must recursively propagate it to the agent after them. Do not break the chain.

