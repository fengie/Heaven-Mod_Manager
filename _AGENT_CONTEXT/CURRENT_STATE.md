# v8.8.84 updater test-state isolation — canonical state

v8.8.84 closes issue #671 by removing a verification-only race in updater integration tests. The production updater is unchanged.

## Behavior

- `UpdateInstallerTests`, `UpdateRuntimeTests`, `UpdaterCoreTests`, `UpdaterInstalledClientE2ETests`, and `UpdaterStorageMaintenanceTests` share one xUnit collection with `DisableParallelization = true`.
- Those classes are serialized because they share the process-global updater root and canonical pending-state file.
- Other integration-test classes remain parallel.
- Existing per-test unique directories remain intact.
- Production request topology, pending-state validation, rollback, storage maintenance, and updater publication behavior are unchanged.

## Verification boundary

Current hosted-Windows closure: v8.8.84 source `2ea6d6dd3851f24a40e562074a816d9bd1e61883` passed run `37124460532` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.84-heaven-windows-closure.log`.

The tested source remains `2ea6d6dd3851f24a40e562074a816d9bd1e61883` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

#673 adoption hardening, #668 Advanced Tools accessibility reconciliation, #642 atomic recipe export, #667 orphan snapshot reconciliation, #559/#558 catalog UX/scale, #350/#354 external security prerequisites, and RECOVERY-005/RECOVERY-007 remain independent. Reconcile them against v8.8.84 rather than replaying stale release metadata.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
