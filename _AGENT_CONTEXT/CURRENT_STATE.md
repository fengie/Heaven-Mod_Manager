# v8.8.61 dark window chrome + build-label repair — MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

Canonical v8.8.60 is integrated on `main` via PR #552. Its exact-head Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership gates passed before merge, so the Dashboard stretch/clipping repair and RECOVERY-004 runtime/updater hardening are now canonical.

The active v8.8.61 lane addresses the remaining defects visible in the installed v8.8.59 screenshot: Windows-light scrollbar chrome, a light native title bar, and mojibake in the build identity label.

## Current MHW product work

- **v8.8.61 direct UI fix:** application-owned dark ScrollBar/Thumb templates, native DWM dark title-bar request, clean ASCII-stable build identity display, and focused regression coverage are implemented on the current candidate branch.
- **RECOVERY-004 / P0:** DONE on canonical v8.8.60 via PR #552.
- **RECOVERY-002 / P0:** in-app catalog browser recovery remains active on its existing owner lane.
- **RECOVERY-007 / P0:** discovery source is integrated; representative Windows/runtime installed-game proof remains before DONE.
- **RECOVERY-003 / P1:** DONE on canonical v8.8.59 via PR #550.
- **RECOVERY-005 / P1:** dark ComboBox source/tests are integrated; installed Windows/WPF interaction acceptance remains before DONE. The supplied installed-client screenshot visually confirms the ComboBox no longer falls back to the bright native control surface, but does not by itself prove interaction behavior.

## Verification boundary

v8.8.60 exact-head verification is closed on RECOVERY-004 head `38d0fcaa853c8951e1bb0043cb962fdb2261a92f` and merged as `2ace37e2731dc9282e04cc42d77c701ff12e1751`.

The v8.8.61 UI implementation was reconciled non-destructively on top of that canonical tree at merge checkpoint `eb440ef91cc453c28a18c243d4e3fa325ad33b81`. Final version/README/continuity edits followed, so the final candidate head still requires exact-head required gates before integration. No installed-client acceptance is claimed for the new scrollbar/title-bar behavior until the resulting build is observed on Windows.

A first draft-gate attempt exposed literal `\\n` sequences in the DWM interop declaration; that malformed source was repaired and a regression now rejects the escaped-newline form.

## Coordination

The direct local Heaven/Agent Control route is not exposed in this ChatGPT session, but GitHub's self-hosted Heaven Windows runner is available and is the required execution path for exact-head compile/test evidence here. Catalog branches remain separately owned and must not be absorbed into this UI lane.

Global Agent Control/Heaven/plugin work remains owned by `fengie/heaven-toolbox` and must not be recreated in MHW.
