# v8.8.87 HPN composition precedence — canonical state

v8.8.87 repairs package-level composition for real HPN/Nexus layouts without weakening fail-closed structural-conflict rules.

## Behavior

- A recognized literal/manual `Main` or base package is dependency-like inside its logical family: when any sibling layer is active, the Mod Library stages that required base on as well.
- Same-Nexus base + sibling packages may compose as a base/overlay relationship on actual overlapping paths.
- Explicit same-Nexus package generations such as `Ver3.1`, `Ver3.10`, and `Ver4.2` may coexist. The newer generation wins only shared paths; files unique to the older generation remain provided by the older source.
- Version labels alone are insufficient. Unrelated same-page packages and equal-version sibling variants still require stronger evidence or a user choice.
- Advanced component rows identify inferred Main, Optional, and Revision roles instead of reducing these packages to generic Component labels.
- Regression coverage models the observed Nexus 4678 `Main + No Bats` layout, Nexus 1965 `3.1 → 4.2` and `3.10 → 4.2` precedence, the full `Main → 3.10 → 4.2` stack, and equal-version v4.2 sibling safety.

## Research/evidence boundary

Publicly indexed HPN ecosystem material confirms that mixed-generation installs are intentional in this mod family: dependent HPN content instructs users to retain both newer 4.2 and older 3.1-era packages for compatibility, and other HPN add-ons recommend retaining multiple v4 body/resource variants or their shared texture files. Direct adult Nexus post/file pages were not reliably readable through the available anonymous web surface, so no unobserved comment text is treated as resolver authority.

## Verification boundary

Current closed hosted-Windows closure is v8.8.86 source `7a8b602cf87be65c0dfefbaac84288b6b010df23`, run `37135652874`, with exact evidence at `_AGENT_CONTEXT/EVIDENCE/v8.8.86-heaven-windows-closure.log`. This is the last closed canonical Windows source; current main contains later integrated source changes, so the historical closure does not authorize v8.8.87.

PR #692 exact head `a4b04c99b82ed1efe326e0d8f575469716898fba` passed Workflow Feature PR Gate run `37136336432` and merged to canonical `main` as `92bb0162e3a64f2bf6b02f7feaeda8ff6b37eb70`. The v3.1 follow-up requires its own exact-head feature verification before integration. v8.8.87 still requires fresh canonical Windows closure after the final integration; historical v8.8.86 runs do not authorize this source.

## Remaining independent work

Keep the existing project-plan queue independent, including #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and remaining recovery/installed-Windows acceptance items.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, conflict/dependency proof separation, and durable-evidence privacy boundary, and recursively propagate the same obligation.
