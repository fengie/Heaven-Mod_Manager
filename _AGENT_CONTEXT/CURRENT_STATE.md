# v8.8.84 save-snapshot orphan pruning — canonical state

v8.8.84 closes the save-snapshot retention leak where interrupted captures or failed best-effort cleanup can leave unindexed immediate-child directories under the manager-owned snapshot root.

## Behavior

- Unindexed immediate-child snapshot directories are reclaimed during pruning.
- Every live DB-owned snapshot path is removed from orphan consideration before retention/delete handling, preventing retained or retryable indexed snapshots from being deleted by the orphan sweep.
- Snapshot deletion remains restricted to normalized immediate children of the canonical snapshot root.
- Orphan reparse points are unlinked without traversing or deleting their external targets.
- Locked or inaccessible orphan cleanup is best effort and remains retryable on a later prune.
- The v8.8.83 marker-owned test-scratch lifecycle and v8.8.82 updater topology/storage-retention boundaries remain unchanged.

## Verification boundary

v8.8.83 candidate source `d7d15429bbcfb421e5d5b7120f7f54a830082aca` passed exact-head Workflow Feature run `37108432265` plus the Ownership and Product Security gates, then integrated via canonical merge `ed7006e18d1fdbbdedd2209d4b766b5db938add9`. The merge SHA itself was not separately exact-source verified.

The pre-reconciliation #662 head `d469d879277ebc9bec6fef93ece3c6faf290b496` built successfully and passed all focused workflow/snapshot tests; its sole repository-gate failure was version parity. v8.8.84 fixes that release boundary and therefore requires a new exact-head gate before integration.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Existing historical unmarked test scratch remains outside automatic deletion unless ownership/staleness is independently proven.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment/no-follow deletion, test-scratch lifecycle, updater trust/storage boundaries, and durable-evidence privacy boundary, and recursively propagate the same obligation.
