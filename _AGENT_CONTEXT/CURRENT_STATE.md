# v8.8.87 HPN composition precedence — canonical state

v8.8.87 repairs package-level composition for real HPN/Nexus layouts without weakening the resolver's fail-closed structural-conflict rules.

## Behavior

- A recognized literal/manual `Main` or base package is dependency-like inside its logical family: when any sibling layer is active, the Mod Library stages that required base on as well.
- Same-Nexus base + sibling packages may compose as a base/overlay relationship on actual overlapping paths.
- Explicit same-Nexus package generations such as `Ver3.10` and `Ver4.2` may coexist. The newer generation wins only shared paths; files unique to the older generation remain provided by the older source.
- Version labels alone are insufficient. Unrelated same-page packages and equal-version sibling variants still require stronger evidence or a user choice.
- Advanced component rows identify inferred Main, Optional, and Revision roles instead of reducing these packages to generic Component labels.
- Regression coverage models the observed Nexus 4678 `Main + No Bats` layout and Nexus 1965 mixed HPN generation behavior.

## Research/evidence boundary

Publicly indexed HPN ecosystem material confirms that mixed-generation installs are intentional in this mod family: dependent HPN content instructs users to retain both newer 4.2 and older 3.1-era packages for compatibility, and other HPN add-ons recommend retaining multiple v4 body/resource variants or their shared texture files. Direct adult Nexus post/file pages were not reliably readable through the available anonymous web surface, so no unobserved comment text is treated as resolver authority.

## Verification boundary

The last closed hosted-Windows source remains v8.8.85 commit `271b9d8d7303a6136b574af52d43d95efff26f34` / run `37130638298`. v8.8.87 changes product source, tests, and release metadata and therefore requires a fresh exact-final-head PR #692 verification before integration plus fresh canonical Windows closure after integration. Historical green runs do not authorize this source.

## Remaining independent work

Keep the existing project-plan queue independent, including #669 storage lifecycle work, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and remaining recovery/installed-Windows acceptance items.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, conflict/dependency proof separation, and durable-evidence privacy boundary, and recursively propagate the same obligation.
