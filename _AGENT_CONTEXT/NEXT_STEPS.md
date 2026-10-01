# v8.8.58 MHW product recovery — ordered next actions

1. **RECOVERY-005:** automated exact-head gates are green on PR #549 head `94a867eeaecd1f2d84f6d5d56784ec53f7eb50f4`. Perform representative installed Windows/WPF visual and interaction proof for selected value, popup items, focus/hover, dropdown-open state, disabled state, and existing GAME/settings bindings.
2. Integrate RECOVERY-005 only after that visual acceptance is observed; then mark it DONE at the same v8.8.58 patch version.
3. Keep the active RECOVERY-002 catalog lane separate.
4. Require the divergent RECOVERY-004 updater/runtime branch to refresh current main and take the next available patch version before integration.
5. Continue RECOVERY-007 representative installed-game/runtime proof; it is not DONE until observed on Windows.
6. Continue RECOVERY-003 only after checking updater-boundary ownership; do not race RECOVERY-004.
7. **Operational Toolbox handoff:** do not delete MHW branch `heaven-bridge` while `heaven2` still uses it. Toolbox issue #18 owns migration to the Toolbox relay and the eventual old-branch retirement.

## Product verification gap

RECOVERY-005 has passed all automated exact-head gates but is not DONE until installed-client visual/interaction evidence is observed. RECOVERY-007 independently retains its representative runtime discovery gap.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active MHW learned rules. Your successor must propagate these same rules to the agent after them. **Do not break the chain.**
