# v8.8.86 import descendant-reparse hardening — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #676 / PR #691

## v8.8.86 behavior

- `SafeRecursiveTraversal.EnsureNoReparsePoints` rejects descendant file or directory reparse points without following them.
- Import publication applies that guard immediately before the final same-volume move into `ModsRoot`.
- Import cleanup and rollback apply the same guard before recursive deletion, preserving suspect trees instead of traversing them.
- Existing direct-child containment, root-reparse, destination-exists, and same-volume guards remain in force.
- Windows junction regressions prove external target bytes remain untouched.
- v8.8.85 feature-candidate verification separation remains unchanged; canonical main/release verification stays strict.

## Verification boundary

The last closed canonical Windows source remains v8.8.85 `271b9d8d7303a6136b574af52d43d95efff26f34` / run `37130638298`, with evidence in `_AGENT_CONTEXT/EVIDENCE/v8.8.85-heaven-windows-closure.log`.

PR #691 source head `6e34b48331fe6943e69210b91094b719e3004ea4` passed its exact-head Workflow Feature admission/build-test gate before release metadata synchronization. The final metadata-synchronized PR #691 head must pass fresh exact-head gates before merge. After integration, canonical `main` must still pass the full Windows release gate without `-FeatureCandidate`; prior v8.8.85 evidence does not transfer.

Residual limitation: validation is path-based. A topology swap after final validation and before move/delete remains a TOCTOU risk; v8.8.86 does not claim handle-atomic containment.

## Ordered continuation

1. Merge only the exact final green PR #691 head and verify remote `main`.
2. Observe the canonical Windows release gate, persist exact v8.8.86 closure evidence, and then close #676.
3. Reconcile #674, #677, #666, #685, #687, and #688 one coherent patch at a time against fresh `main`; preserve their unique work and discard stale release metadata.
4. Keep draft #659 until every still-unique tranche is either integrated or explicitly extracted/superseded.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, filesystem containment, fail-closed destructive boundaries, durable-evidence privacy, and candidate-vs-canonical verification separation. Propagate the same obligation to the next successor. Do not break the chain.
