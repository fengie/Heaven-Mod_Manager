# v8.8.58 MHW product recovery — ordered next actions

1. **RECOVERY-005:** source/test work is integrated on v8.8.58 main via PR #549. Perform representative installed Windows/WPF visual and interaction proof for selected value, popup items, focus/hover, dropdown-open state, disabled state, and existing GAME/settings bindings.
2. Mark RECOVERY-005 DONE at the same v8.8.58 patch version only after that installed-client visual acceptance is observed.
3. Keep the active RECOVERY-002 catalog lane separate.
4. Require the divergent RECOVERY-004 updater/runtime branch to refresh current main and take the next available patch version before integration.
5. Continue RECOVERY-007 representative installed-game/runtime proof; it is not DONE until observed on Windows.
6. Continue RECOVERY-003 only after checking updater-boundary ownership; do not race RECOVERY-004.
7. **Operational Toolbox handoff:** complete. Toolbox issue #18 proved authenticated `heaven2` traffic on the Toolbox relay and retired the legacy MHW `heaven-bridge` branch.

## Product verification gap

RECOVERY-005 has passed all automated exact-head gates but is not DONE until installed-client visual/interaction evidence is observed. RECOVERY-007 independently retains its representative runtime discovery gap.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active MHW learned rules. Your successor must propagate these same rules to the agent after them. **Do not break the chain.**
