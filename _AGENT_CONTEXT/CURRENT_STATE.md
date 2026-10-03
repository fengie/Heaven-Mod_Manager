# v8.8.86 import descendant-reparse hardening — canonical state candidate

v8.8.86 closes issue #676 by applying the repository's fail-closed reparse topology invariant to import publication and recursive import cleanup/rollback.

## Behavior

- `SafeRecursiveTraversal.EnsureNoReparsePoints` walks an existing tree without following reparse points and rejects any descendant file or directory reparse entry.
- Import publication invokes the guard immediately before the final same-volume directory move into `ModsRoot`.
- Import cleanup and Smart Inbox rollback invoke the same guard before recursive deletion. Unsafe trees remain in place for controlled recovery rather than being traversed.
- Existing direct-child containment, root-reparse, destination-exists, and same-volume protections are preserved.
- Windows regressions inject real directory junctions before publication and after publication/before rollback and prove external target bytes remain untouched.
- The v8.8.85 feature-candidate/canonical-release verification separation remains unchanged.

## Verification boundary

The last closed canonical Windows release source remains v8.8.84 `2ea6d6dd3851f24a40e562074a816d9bd1e61883`, run `37124460532`.

PR #679 must pass Product Security and Workflow Feature gates on its exact reconciled v8.8.86 head before merge. After integration, canonical `main` must still pass the full Windows release gate without `-FeatureCandidate`.

Residual TOCTOU remains between final path-based validation and the subsequent move/delete operation. The change closes the previously unvalidated descendant topology but does not claim handle-level atomicity.

## Remaining independent work

#673/#674 unmanaged adoption hardening remains separate and must reconcile against v8.8.86 rather than replay stale release metadata. Advanced Tools accessibility #668 is already integrated on canonical main at `b4a6d20fdc7a522ba4cbf8b7cec5bab8940846f1`. #642 atomic recipe export, #667 orphan snapshot reconciliation, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and RECOVERY-005/RECOVERY-007 remain independent.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, durable-evidence privacy, and candidate-vs-canonical verification separation, and recursively propagate the same obligation.
