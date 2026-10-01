# v8.8.60 RECOVERY-004 runtime/updater hardening — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Candidate branch: `fix/recovery-004-runtime-hardening-v8.8.60`

## Implemented

This candidate semantically reconciles the two preserved RECOVERY-004 archive tips onto current 8.8.59 Settings lineage instead of replaying stale history.

- Dashboard content stretches naturally without a direct ScrollViewer viewport-width binding.
- Initial Mods-page metadata refresh records completion only after success and clears its in-flight flag after failure/cancellation so a later visit can retry.
- Staged updater handoff snapshots the exact `StagedUpdate`, invalidates stale prepared handoffs, and holds `programUpdateGate` across final identity/policy checks and synchronous helper launch.
- The 8.8.59 automatic-update preference and explicit manual-update intent remain enforced before and inside the serialized launch boundary.
- Startup diagnostics now redact updater health token, file, and attempt argument values.

Focused regressions cover all four recovered semantics plus the existing deferred-load contract.

## Verification state

Focused Windows verification on implementation checkpoint `5f11ce59712808ce259dbf72922cc011fb4319c1` passed:
- Release build: 0 warnings / 0 errors.
- IntegrationTests: 268/268.
- FunctionVerifier: 1469 functions, 0 trace gaps, 0 uncovered call sites, 0 parse errors.

Version/README/continuity metadata was updated after that checkpoint, so the final branch head still requires the repository's normal exact-head gates before integration.

The attempted local Codex audit on `heaven` hit its usage quota and made no edits. Deterministic Heaven Bridge execution successfully produced the Windows evidence above; no security/auth boundary was bypassed.

## Coordination / risks

- RECOVERY-003 Settings is DONE on canonical 8.8.59 via PR #550. Preserve its preferences/manual-update behavior.
- RECOVERY-002 catalog recovery has existing owner branches; do not duplicate or absorb them.
- RECOVERY-007 still needs representative installed-game/runtime discovery proof.
- RECOVERY-005 still needs installed Windows/WPF ComboBox visual/interaction acceptance.
- Do not merge the stale `fix/runtime-updater-hardening-v8.8.57-20261001` branch wholesale. Its useful semantics have been extracted into this fresh lane.
- Global Agent Control/Heaven/plugin work belongs in `fengie/heaven-toolbox`, not MHW.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full. Preserve the permanent continuity constitution, all unresolved evidence gaps, and this explicit successor-propagation obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
