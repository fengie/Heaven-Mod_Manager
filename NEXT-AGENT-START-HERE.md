# v8.8.81 canonical update-migration mod identity — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #630

## v8.8.81 behavior

- Update migration resolves requested old/replacement IDs with `PathRules.Comparer` and canonicalizes them to stored IDs.
- Conflict rules, metadata, family membership, resource-provider ownership, staged state, supersession, and update diffing use the same logical identity.
- Old/new IDs differing only by case remain rejected as one logical package.

## Verification boundary

v8.8.80 source `e7af34331b608fca3d115d2dfe20f5c8e69a5973` passed hosted Windows run `37092891133`. v8.8.81 changes Automation source/tests and release metadata, so fresh exact-input verification is required.

## Unresolved risks and next work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Actions artifact upload remains quota-constrained.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and case-insensitive mod identity. The successor **must propagate** this obligation onward. **Do not break the chain.**
