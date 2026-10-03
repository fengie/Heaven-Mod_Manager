# v8.8.86 import descendant-reparse hardening — canonical state

v8.8.86 integrates issue #676's fail-closed reparse-topology protection into import publication and recursive import cleanup/rollback.

## Behavior

- `SafeRecursiveTraversal.EnsureNoReparsePoints` walks an existing tree without following reparse points and rejects any descendant file or directory reparse entry.
- Import publication invokes the guard immediately before the final same-volume directory move into `ModsRoot`.
- Import cleanup and rollback invoke the same guard before recursive deletion. Unsafe trees remain for controlled recovery rather than being traversed.
- Existing direct-child containment, root-reparse, destination-exists, and same-volume protections are preserved.
- Windows regressions inject real directory junctions and prove external target bytes remain untouched.
- v8.8.85 feature-candidate/canonical-release verification separation remains unchanged.

## Verification boundary

The last closed canonical Windows release source is v8.8.85 `271b9d8d7303a6136b574af52d43d95efff26f34`, run `37130638298`, with exact evidence in `_AGENT_CONTEXT/EVIDENCE/v8.8.85-heaven-windows-closure.log`.

The final metadata-synchronized v8.8.86 PR #691 head requires fresh exact-head gates before merge. After integration, canonical `main` requires the full Windows release gate without `-FeatureCandidate`.

Residual TOCTOU remains between final path-based validation and the subsequent move/delete operation. This closes the previously unvalidated descendant topology but does not claim handle-level atomicity.

## Remaining independent work

#673/#674 unmanaged adoption hardening, #677 orphan snapshot pruning, #642/#666 atomic recipe export, #669/#685 storage preflight, #686/#687 public provenance, #688 Browse Mods exact-file state, #558/#559 catalog work, and RECOVERY-005/RECOVERY-007 remain independent. Draft #659 must stay until every unique tranche is classified.

Every successor must preserve the continuity constitution, active Learned Rules, exact-input verification, filesystem containment, durable-evidence privacy, and candidate-vs-canonical verification separation, then propagate the same obligation onward.
