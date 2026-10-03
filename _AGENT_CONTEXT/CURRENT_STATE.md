# v8.8.84 updater test-state isolation — canonical state

v8.8.84 closes issue #671 by removing a verification-only race in updater integration tests. The production updater is unchanged.

## Behavior

- `UpdateInstallerTests`, `UpdateRuntimeTests`, `UpdaterCoreTests`, `UpdaterInstalledClientE2ETests`, and `UpdaterStorageMaintenanceTests` share one xUnit collection with `DisableParallelization = true`.
- Those classes are serialized because they share the process-global updater root and canonical pending-state file.
- Other integration-test classes remain parallel.
- Existing per-test unique directories remain intact.
- Production request topology, pending-state validation, rollback, storage maintenance, and updater publication behavior are unchanged.

## Verification boundary

v8.8.83 source `ed7006e18d1fdbbdedd2209d4b766b5db938add9` passed hosted-Windows verification run `37108647747` and remains the last closed source. The earlier v8.8.84 source-only PR head `b12d9eeb31b9e069d63a9983b26fe1e0dc80401c` passed Workflow Feature PR Gate run `37122995254`, but later version/continuity changes invalidate inheritance of that exact-head result. The final PR #672 head requires fresh verification before integration.

## Remaining independent work

#673 adoption hardening, #668 Advanced Tools accessibility reconciliation, #642 atomic recipe export, #667 orphan snapshot reconciliation, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and RECOVERY-005/RECOVERY-007 remain independent. Reconcile them against v8.8.84 rather than replaying stale release metadata.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
