# v8.8.82 migration identity, recovery, and installer correctness — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #630, issue #639, and FOMOD exactly-one correction PR #636

## Behavior

- Update migration resolves old/replacement IDs with `PathRules.Comparer`, canonicalizes them to stored IDs, and uses that identity consistently across migration state.
- LastKnownGood restore reconstructs state over the complete current installed mod set: baseline mods recover saved enabled/priority values and post-baseline mods are disabled while retaining their current priority.
- FOMOD `SelectExactlyOne` groups use native WPF `RadioButton` semantics so one choice remains selected; `SelectAtMostOne` remains checkbox-based zero-or-one.

## Verification boundary

v8.8.81 source `1d21a7ee99774c6820e16421837e9451ef2e3ef6` passed hosted Windows run `37098443475`. v8.8.82 changes Automation/App source and tests plus release metadata, so fresh exact-input verification is required.

## Execution route

This ChatGPT session exposed the canonical Heaven plugin skills but not a callable Heaven Local Bridge / Agent Control runtime namespace. Repository mutation therefore continued through authenticated GitHub; no local-offload result is claimed.

## Unresolved risks

External updater signing/ruleset prerequisites and installed-Windows acceptance remain unresolved risks; preserve the current canonical risk ledger in `_AGENT_CONTEXT/CURRENT_REVISION.json`.

## Successor obligation

The successor **must propagate** this continuity obligation to the **next agent after them**, and require that agent to continue the same recursive handoff. **Do not break the chain.**
