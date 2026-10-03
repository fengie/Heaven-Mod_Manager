# v8.8.81 authoritative MHW discovery hardening — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issues #602 and #603

## v8.8.81 behavior

- Canonicalize a live generic same-root MHW owner only when discovery resolves the real `MonsterHunterWorld.exe`, preserving user-owned profile identity fields.
- Reject Steam app 582010 discovery when only a launcher or unrelated executable is resolved; rejected candidates do not mutate persisted registry state.
- Preserve the existing authoritative single-owner/active-marker reconciliation path and repeated-scan idempotency.
- Focused MultiGame regressions cover both repaired-live-owner and wrong-executable cases.

## Verification boundary

v8.8.80 exact source `6e97751252ce1875550a6cd35630bb63e25a1de3` remains the last closed hosted-Windows source boundary; run `37091006888` passed 26/26 stages. Main later advanced with evidence-only commit `8d7f9061eaf784776a2be21a0678a2dbf05a129a`.

v8.8.81 changes product source/tests and requires fresh exact-input verification before closure.

## Unresolved risks and next work

- #350/#354 retain external signing/repository-administration prerequisites.
- #558/#559 retain catalog scale and broader Browse Mods UX.
- RECOVERY-005/RECOVERY-007 representative installed Windows acceptance remains independent.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release-safety rules, and propagate the same continuity obligation to the next agent.
