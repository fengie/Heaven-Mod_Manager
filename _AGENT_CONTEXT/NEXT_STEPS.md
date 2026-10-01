# v8.8.58 MHW product recovery — ordered next actions

1. Run the required exact-head MHW gates for `fix/recovery-005-dark-combobox-v8.8.58` and repair any regression without weakening verification.
2. Perform representative installed Windows/WPF visual and interaction proof for the ComboBox selected value, popup items, focus/hover, dropdown open state, disabled state, and existing GAME/settings bindings.
3. Integrate RECOVERY-005 only after the candidate is green and visual acceptance is observed; then mark RECOVERY-005 DONE at the same patch version.
4. Keep the active RECOVERY-002 catalog lane separate.
5. Require the divergent RECOVERY-004 updater/runtime branch to refresh current main and take the next available patch version before integration.
6. Continue RECOVERY-007 representative installed-game/runtime proof; it is not DONE until observed on Windows.
7. Continue RECOVERY-003 only after checking updater-boundary ownership; do not race RECOVERY-004.

## Product verification gap

RECOVERY-005 is implemented but not DONE until exact-head gates and installed-client visual/interaction evidence are observed. RECOVERY-007 independently retains its representative runtime discovery gap.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active MHW learned rules. Your successor must propagate this continuity obligation onward. **Do not break the chain.**
