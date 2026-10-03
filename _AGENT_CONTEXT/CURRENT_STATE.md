# v8.8.85 import descendant-reparse hardening — candidate state

Issue #676 closes an import-publication filesystem trust gap: manager-owned staging/published roots were checked for reparse status, but descendants were not revalidated immediately before publication or recursive deletion.

## Candidate behavior

- `SafeRecursiveTraversal.EnsureNoReparsePoints` traverses a tree without following reparse points and rejects any descendant file/directory reparse entry.
- Import publication invokes that guard immediately before the final same-volume directory move.
- Cleanup and rollback invoke the same guard before recursive deletion; unsafe trees are preserved for controlled recovery rather than traversed.
- Existing containment/root-reparse/same-volume/destination checks are preserved.
- Windows regressions create real junctions and prove external sentinel bytes remain unchanged for both pre-publication and rollback substitution cases.

## Verification boundary

Last closed hosted-Windows source: v8.8.84 `2ea6d6dd3851f24a40e562074a816d9bd1e61883`, run `37124460532`.

Current v8.8.85 source/test checkpoint: `e2a63c6a653426b014c189e8b2e44c41981229bf`, followed by release/continuity metadata. Exact-final-head verification is still required. No v8.8.84 green result applies to the changed source/test/release inputs.

Residual TOCTOU remains between final path-based validation and move/delete. This change closes the previously unvalidated descendant topology without claiming handle-level atomicity.

## Independent work

#673/#674 unmanaged adoption hardening remains a separate boundary and must reconcile from fresh `main` after this patch; its stale v8.8.85 metadata is not canonical. #668 Advanced Tools accessibility reconciliation, #642 atomic recipe export, #667 orphan snapshot reconciliation, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and RECOVERY-005/RECOVERY-007 remain independent.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
