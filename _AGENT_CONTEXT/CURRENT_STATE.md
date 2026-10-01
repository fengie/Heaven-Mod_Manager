# v8.8.58 dark ComboBox candidate — MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

The v8.8.57 Toolbox cutover is integrated on canonical main at `5d3c7cda5dcfaba8d3aafdd6710a47399fa23bb1`. RECOVERY-005 is now active on `fix/recovery-005-dark-combobox-v8.8.58`: the archived application-owned dark ComboBox template and its focused regression have been reconciled onto current main without importing stale metadata.

## Current MHW product work

- **RECOVERY-007 / P0:** universal installed-game discovery source is integrated; representative Windows/runtime discovery proof is still required before DONE.
- **RECOVERY-002 / P0:** in-app catalog browser recovery remains active on its existing product lane.
- **RECOVERY-004 / P0:** updater/runtime hardening remains on its separate divergent branch and must refresh after newer main before integration.
- **RECOVERY-005 / P1:** v8.8.58 candidate replaces bright native ComboBox chrome with app-owned dark chrome; all exact-head automated gates are green on `94a867eeaecd1f2d84f6d5d56784ec53f7eb50f4`, while installed Windows/WPF visual/interaction acceptance remains.
- **RECOVERY-003 / P1:** manual update-check UI remains product work and must avoid overlapping RECOVERY-004 updater edits.

Legacy Agent Control, Agent Manager, Heaven Bridge/auth/provider, Agent Work Reports, plugin-toolbox, and global coordination follow-up remains owned by `fengie/heaven-toolbox` issue #5 and is not MHW product work.

## Verification boundary

PR #549 head `94a867eeaecd1f2d84f6d5d56784ec53f7eb50f4` passed the exact repository verification/build/test workflow, focused UX/XAML regressions, product-security gate, and Toolbox-ownership gate. That automated evidence does not replace installed-client visual acceptance.

## Operational Toolbox handoff residue

The MHW `heaven-bridge` transport branch is still live for `heaven2` and must **not** be treated as stale or deleted. Toolbox issue #18 owns the relay cutover; retire the MHW branch only after that issue proves `heaven2` on the Toolbox relay.
