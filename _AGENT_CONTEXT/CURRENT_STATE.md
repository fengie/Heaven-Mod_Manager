# v8.8.60 runtime/updater hardening — MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

Canonical 8.8.59 already contains the persistent Settings/manual-update preference work from PR #550. The active 8.8.60 RECOVERY-004 lane is a fresh-main semantic reconciliation of two archived updater/runtime hardening branches; it does not replay their stale version or continuity metadata.

## Current MHW product work

- **RECOVERY-004 / P0:** active 8.8.60 candidate. Dashboard stretch, retry-safe initial metadata refresh, staged-update identity/mutation serialization, and updater-health diagnostic redaction are implemented with focused regressions.
- **RECOVERY-002 / P0:** in-app catalog browser recovery remains active on its existing owner lane.
- **RECOVERY-007 / P0:** discovery source is integrated; representative Windows/runtime installed-game proof remains before DONE.
- **RECOVERY-003 / P1:** DONE on canonical 8.8.59 via PR #550 with required exact-head gates green.
- **RECOVERY-005 / P1:** dark ComboBox source/tests are integrated; installed Windows/WPF visual/interaction acceptance remains before DONE.

## Verification boundary

Focused Windows proof for the recovered implementation checkpoint `5f11ce59712808ce259dbf72922cc011fb4319c1` is green: Release build 0 warnings / 0 errors, IntegrationTests 268/268, FunctionVerifier 1469 functions with 0 trace gaps, 0 uncovered call sites, and 0 parse errors. This is not a substitute for final exact-head CI after version/continuity edits.

The local Codex audit route on `heaven` was quota-blocked and made no edits. Deterministic Heaven Bridge execution remained available and produced the focused Windows evidence. No authentication or security boundary was weakened.

## Coordination

Do not merge the stale `fix/runtime-updater-hardening-v8.8.57-20261001` history wholesale. Its useful semantics have been re-evaluated against current main and selectively ported into the fresh 8.8.60 lane. Catalog branches remain separately owned and must not be absorbed into this updater lane.

Global Agent Control/Heaven/plugin work remains owned by `fengie/heaven-toolbox` and must not be recreated in MHW.
