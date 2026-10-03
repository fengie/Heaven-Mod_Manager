# v8.8.87 HPN composition precedence — candidate handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: PR #692

## v8.8.87 behavior

- Required literal/manual Main/base packages stay enabled beneath active sibling components.
- Same-source optional/component packages override Main only on overlapping paths.
- Same-Nexus `Ver3.10` / `Ver4.2`-style generations can remain enabled together; the newer generation wins shared paths while older-only files remain active.
- Equal-version sibling variants remain a choice unless stronger metadata or an explicit user rule orders them.
- Mod Library advanced rows identify Main / Optional / Revision roles.
- Tests cover Nexus-4678-style `Main → No Bats`, Nexus-1965-style `3.1 → 4.2` and `3.10 → 4.2`, full `Main → 3.10 → 4.2`, and equal-version variant safety.

## Verification boundary

The current closed hosted-Windows source is v8.8.86 `7a8b602cf87be65c0dfefbaac84288b6b010df23`, run `37135652874`; it is the last closed canonical Windows source. Current main has later integrated source changes, so do not reuse that historical evidence for v8.8.87.

PR #692 must pass fresh exact-final-head feature verification before merge. After merge, verify remote `main` and obtain fresh canonical Windows release closure because this change modifies product source/tests/release inputs.

## Execution/offload note

This ChatGPT runtime did not expose callable Heaven Local Bridge or Agent Control operations. Implementation therefore used the authenticated GitHub path after live branch/PR overlap checks. No local execution or Windows verification is claimed unless corresponding workflow evidence exists.

## Next action

Observe the exact-final-head PR #692 gate. Merge only that green head. Then verify the resulting remote-main SHA and its strict canonical Windows release gate/evidence before declaring release closure.

## Unresolved risk

The HPN composition behavior is source- and regression-tested on this candidate, but it still requires exact-final-head feature verification before merge and fresh canonical Windows closure after integration. Independent open risks such as external trust prerequisites and remaining recovery/catalog work stay separate and must not be conflated with this change.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, conflict/dependency proof separation, updater/filesystem safety, durable-evidence privacy, and candidate-vs-canonical verification separation. The successor must propagate this continuity obligation to the next agent after them.

**Do not break the chain.** The next agent must preserve and recursively propagate this same continuity obligation to its successor.
