# v8.8.58 dark ComboBox integrated — MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

The v8.8.57 Toolbox cutover is integrated on canonical main at `5d3c7cda5dcfaba8d3aafdd6710a47399fa23bb1`. RECOVERY-005 source/test work is integrated on v8.8.58 main via PR #549 / merge `b270494c047c2de24b2c7aa94bd710c21527d72a`; only installed Windows/WPF visual/interaction acceptance remains before DONE.

## Current MHW product work

- **RECOVERY-007 / P0:** universal installed-game discovery source is integrated; representative Windows/runtime discovery proof is still required before DONE.
- **RECOVERY-002 / P0:** in-app catalog browser recovery remains active on its existing product lane.
- **RECOVERY-004 / P0:** updater/runtime hardening remains on its separate divergent branch and must refresh after newer main before integration.
- **RECOVERY-005 / P1:** v8.8.58 source/test work replaces bright native ComboBox chrome with app-owned dark chrome and is integrated on `main`; exact-head automated gates were green on `9c9894d39f5875ef5271260638fb07d26c66da8d`, while installed Windows/WPF visual/interaction acceptance remains.
- **RECOVERY-003 / P1:** manual update-check UI remains product work and must avoid overlapping RECOVERY-004 updater edits.

Legacy Agent Control, Agent Manager, Heaven Bridge/auth/provider, Agent Work Reports, plugin-toolbox, and global coordination context takeover completed in `fengie/heaven-toolbox` issue #5 and is not MHW product work.

## Verification boundary

PR #549 head `9c9894d39f5875ef5271260638fb07d26c66da8d` passed Workflow Feature PR Gate run 636, MHW Product Security Gate run 626, and Heaven Toolbox Ownership Gate run 20 before merge. The source/test tranche is integrated; that automated evidence does not replace installed-client visual acceptance.

## Operational Toolbox handoff

The live Heaven Bridge transport cutover completed under Toolbox issue #18. Both hosts now use the Toolbox relay and the legacy MHW `heaven-bridge` branch has been retired.
