# v8.8.86 import descendant-reparse hardening — canonical handoff candidate

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #676 / PR #679
Immediate parent: v8.8.85 feature-verification decoupling on canonical `main` at `de896dab613070944bf107d339f89722f0200272`

## v8.8.86 behavior

- `SafeRecursiveTraversal.EnsureNoReparsePoints` provides an allocation-light fail-closed tree-topology check that never follows descendant reparse points.
- `ImportPublicationWorkspace.Publish` rejects unsafe descendant topology immediately before the final same-volume move into `ModsRoot`.
- `ImportPublicationWorkspace.Cleanup` and `RollbackPublished` reject unsafe descendant topology before recursive deletion, preserving the suspect tree instead of traversing it.
- Existing direct-child containment, root-reparse, destination-exists, and same-volume guards remain in force.
- Windows junction regressions cover pre-publication descendant substitution and post-publication rollback substitution while proving external sentinel bytes remain untouched.
- v8.8.85's `-FeatureCandidate` verification separation is preserved unchanged; canonical main/release verification remains strict.

## Verification boundary

The last closed canonical Windows source remains v8.8.84 `2ea6d6dd3851f24a40e562074a816d9bd1e61883` / run `37124460532`.

The v8.8.86 PR #679 final head requires fresh exact-head Product Security and Workflow Feature verification. Prior green evidence, including the pre-reconciliation #679 run and the v8.8.85 source-only verification, does not authorize this changed head.

Residual limitation: tree validation is path-based and closes the unvalidated-descendant case, but a topology swap after final validation and before move/delete remains a TOCTOU risk. Do not claim handle-atomic containment.

## Unresolved risk

The descendant reparse validation is path-based. A topology swap after the final validation and before the subsequent move/delete remains a residual TOCTOU risk; v8.8.86 does not claim handle-atomic containment.

## Coordination

Issue #673 / PR #674 is a separate unmanaged-adoption boundary. Its stale branch claimed v8.8.85 from an older base; it must reconcile onto fresh `main` after v8.8.86 and take the next available patch identity rather than replay stale metadata.

## Exact next action

Require exact-final-head PR #679 gates after reconciliation with v8.8.85. Merge only that exact green head, verify remote `main` contains both the v8.8.85 verification changes and v8.8.86 import hardening, then observe the canonical main Windows release gate and persist exact closure evidence normally. Close #676 and retire the temporary branch/ownership claim only after verified integration.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, filesystem containment, fail-closed destructive boundaries, durable-evidence privacy, and candidate-vs-canonical verification separation. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
