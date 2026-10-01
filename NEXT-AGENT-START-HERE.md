# v8.8.61 dark window chrome + build-label repair — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Candidate branch: `fix/ui-chrome-encoding-v8.8.60-20261001` (temporary branch name; release version is 8.8.61)
PR: #551

## Implemented

This candidate is reconciled on top of canonical v8.8.60 / PR #552 and keeps its Dashboard/runtime/updater fixes intact.

- WPF ScrollBar/Thumb chrome is now application-owned and dark for vertical and horizontal scrolling instead of falling back to Windows' light scrollbar surface.
- The native Windows title bar requests immersive dark mode through a constrained `dwmapi.dll` System32 import, without enabling unsafe code for the application.
- `UpdateBuildIdentity.DisplayId` no longer contains the corrupted literal `â€¢`; it uses an ASCII-stable ` | ` separator.
- Focused regressions protect all three fixes and reject literal escaped-newline corruption in the DWM declaration.

The first draft-gate iteration caught a malformed-source regression where literal `\\n` sequences caused the interop declaration to be commented out. The source was repaired to real line breaks and the regression was added before release integration.

## Verification state

RECOVERY-004 v8.8.60 exact-head gates passed before PR #552 merged as `2ace37e2731dc9282e04cc42d77c701ff12e1751`.

v8.8.61 implementation merge checkpoint: `eb440ef91cc453c28a18c243d4e3fa325ad33b81`. Version/README/continuity edits followed, so the final branch head requires the normal exact-head Workflow Feature, MHW Product Security, Heaven Toolbox Ownership, and continuity gates before PR #551 is marked ready and merged.

No installed-client visual/interaction acceptance is claimed yet for the new scrollbar or native title-bar dark mode.

## Coordination / risks

- RECOVERY-004 is DONE on canonical v8.8.60; do not reopen or replay its archived branches.
- RECOVERY-002 catalog recovery remains separately owned.
- RECOVERY-007 still needs representative installed-game/runtime discovery proof.
- RECOVERY-005 retains an interaction-acceptance gap even though the supplied installed screenshot visually confirms the ComboBox is dark.
- The direct local Heaven/Agent Control route is unavailable in this chat; exact-head Windows execution is available through the self-hosted Heaven Actions runner.
- Global Agent Control/Heaven/plugin work belongs in `fengie/heaven-toolbox`, not MHW.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full. Preserve the permanent continuity constitution, all unresolved evidence gaps, and this explicit successor-propagation obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
