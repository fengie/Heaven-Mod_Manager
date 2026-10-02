# v8.8.75 Mod Library accessibility — canonical-ready handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Integrated predecessor: v8.8.74 / PR #588
Change set: issue #589 Mod Library toggle accessibility

## v8.8.75 behavior

- The Mod Library whole-mod enable checkbox exposes a UI Automation name bound to the current mod `DisplayName`.
- Advanced component toggles expose UI Automation names bound to the current component `Label`.
- Both controls remain native WPF `CheckBox` instances, preserving TogglePattern state and keyboard Space behavior.
- Accessible names remain data-bound to the current item, so row/item recycling updates identity instead of retaining a previous item's name.
- `XamlBindingSafetyTests.ModLibraryTogglesExposeTargetSpecificAutomationNames` guards both bindings.

## Verification boundary

v8.8.74 predecessor exact head `1db52ceace7cc373753b3bac34d7bdfb4e002f01` passed Workflow Feature `36995650793`, Updater Publication `36995650796`, Heaven Toolbox Ownership `36995650860`, and MHW Product Security `36995650986`.

Those results do **not** authorize v8.8.75. Require fresh exact-head gates for the final accessibility candidate before integration, then read back canonical `main`.

## Unresolved risks and coordination

PRs #582, #585, and #586 predate this patch boundary and must reconcile against fresh canonical `main` before later integration. Issues #558/#559/#578, RECOVERY-005/RECOVERY-007, external production trust-anchor provisioning for #350, and account-tier ruleset/certificate constraints for #354 remain independent work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release supply-chain rules. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
