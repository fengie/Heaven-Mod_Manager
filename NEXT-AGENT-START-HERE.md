# v8.8.58 dark ComboBox chrome — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Candidate branch: `fix/recovery-005-dark-combobox-v8.8.58`

## Implemented

RECOVERY-005 has been reconciled from the preserved archive onto current v8.8.57 main without importing stale metadata. `App.xaml` now owns ComboBox and ComboBoxItem chrome with the existing dark Text/Panel/Border/Accent palette. The selected value, arrow, popup, hover, focus, selected-item, open, and disabled states no longer depend on the bright Windows system control surface. Existing GAME/settings bindings and view-model contracts are unchanged.

The focused XAML regression now requires the custom ComboBox template and `PART_Popup`, pins dark palette resources, and rejects both Windows system control brush keys.

## Verification state

The production source/test checkpoint is `5076ee0bf81ddfe69e0418bc599e2c7a810a0959`. PR #549 head `94a867eeaecd1f2d84f6d5d56784ec53f7eb50f4` passed Workflow Feature PR Gate run 634 (`36810757532`), MHW Product Security Gate run 624 (`36810757536`), and Heaven Toolbox Ownership Gate run 18 (`36810757535`). Installed-client visual proof is **not** claimed.

RECOVERY-005 remains ACTIVE until representative installed Windows/WPF visual and interaction acceptance confirms selected value, popup items, focus/hover/open/disabled states and normal GAME/settings use. This evidence-only handoff update must itself remain green on the final PR head before integration.

## Coordination / risks

- Do not race the active catalog recovery branches.
- The divergent `fix/runtime-updater-hardening-v8.8.57-20261001` branch touches other UI/updater boundaries and must refresh newer main and allocate the next patch version before it integrates.
- RECOVERY-007 still requires representative Windows/runtime installed-game discovery evidence before DONE.
- Global Agent Control/Heaven/plugin work remains in `fengie/heaven-toolbox` and must not return to MHW.
- The separate MHW `heaven-bridge` transport branch is still live for `heaven2`; Toolbox issue #18 owns its relay cutover and eventual retirement. Do not delete it as stale.

## Successor obligation

Start from current `fengie/heaven-toolbox@main`, then current MHW canonical state and ownership. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and preserve active `LEARNED_RULES.md`. You inherit the permanent continuity constitution; preserve it and explicitly require your successor to inherit it. Your successor must recursively propagate these same rules to the agent after them. **Do not break the chain.**
