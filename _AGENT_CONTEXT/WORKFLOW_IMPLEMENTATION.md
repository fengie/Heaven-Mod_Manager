# Workflow expansion — implemented, 2026-09-27

Base: GitHub main 91f2b9e9815caab2e4670084022a5c2ab3e45f55 (v8.8.0 branding). The local repository was materialized from the GitHub tree and every baseline blob was verified. Local baseline commit is synthetic; use the GitHub SHA for publishing parents.

Requested scope: collection import, decision explanation, central rules editor, update migration, FOMOD choices, profile inheritance/diff, virtual filesystem, crash interaction minimization, stability evidence, relationship graph, adapter SDK.

Implementation and Linux validation are complete. Native WPF interaction is still pending. Read the evidence section below; old verification booleans were not manually promoted.

- Recipe format 2 has payload hash references and game identity. Formats 1/2 import into a saved profile; unresolved entries stay disabled. Matched families can be restored in a separate explicit transaction that refuses existing local groups; recipe metadata never silently overwrites local rules.
- Profile parents are additive schema, recursively resolved with cycle/depth protection. Child profiles store state differences. Existing flat profiles remain valid.
- Planner-derived explorer/decision evidence/profile comparison do not implement another conflict resolver.
- WorkflowWindow adds the desktop screens; actual Windows UI interaction remains to be verified.
- FOMOD supports choices, flags, conditional payloads, and priority. Unknown external dependency types fail closed. Inbox holds FOMOD archives for explicit import instead of activating all variants.
- ddmin checks passing baseline and reproducing full set, then minimizes subsets/complements. Multi-mod results are combinations, not individual blame.
- Migration uses optimistic row images written with manifest/state/commit marker. operation_metadata records inverse edits for rollback and Undo. Fault-injection tests cover crashes after file writes, before commit, after commit, and stale metadata; Undo restores metadata and payloads.
- IGameAdapter has built-in generic/MHW implementations. Planner/engine semantics resolve through the adapter registry. No arbitrary downloaded adapter DLLs are loaded.

Preserve native Windows verification requirements. Do not manually promote fingerprints. Update this document with exact validation and limitations before handoff.

**Do not break the chain.**

## Validation evidence

- .NET SDK 10.0.401; strict whole-solution Release build with Windows targeting: 0 warnings, 0 errors.
- Core 79/79, Automation 39/39, Integration 61/61 (179 total).
- Self-test 11/11. Recipe self-test updated for the format-2 export and import preview.
- Function scan: 683 functions, 562 unchanged known-good, 121 pending normal verification; 0 trace gaps, 0 uncovered call sites, 0 parse errors.
- Handoff preflight and git whitespace checks pass.
- Windows CI is configured on this feature branch/PR. Its outcome must be recorded separately; this Linux session cannot validate WPF rendering or real game launches.
- Evidence logs: `_AGENT_CONTEXT/EVIDENCE/workflow-expansion-validation.log` and `workflow-expansion-function-scan.json`.

## Remaining product boundaries

See `docs/WORKFLOWS.md`. FOMOD external dependency evaluation and cross-version selection mapping deliberately fail closed; recipes do not perform Nexus downloads; profile rules remain global; relationship graph is a persisted-rule neighborhood; SDK is an initial compiled extension contract. These are explicit limitations, not claims of universal installer/game support.
