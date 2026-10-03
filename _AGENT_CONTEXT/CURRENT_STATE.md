# v8.8.83 self-cleaning test scratch — canonical state

v8.8.83 closes the unbounded disposable-test scratch leak reported in issue #650. Raw self-test/updater workspaces are transient again; durable evidence remains outside scratch where required.

## Behavior

- MHW self-test writes an ownership marker, clears Microsoft.Data.Sqlite pools, and retries recursive deletion instead of silently ignoring Windows file-lock failures.
- Installed-client updater E2E writes the same bounded ownership marker, stops processes launched from its install roots, and fails visibly if its per-run temp tree cannot be removed after retries.
- The disposable updater harness persists only its compact summary/evidence outside the disposable profile and removes the marked profile in `finally` on success or failure.
- `scripts/testing/Clear-StaleTestScratch.ps1` only reaps old marked `MhwManagerSelfTest-*` and `mhw-updater-installed-e2e-*` roots whose exact owning process is no longer alive. It refuses unmarked roots, active owners, reparse points, unrelated temp paths, repositories, user Mods/State, and WSL virtual disks.
- Normal test orchestration runs the conservative stale reaper before tests so an interrupted older run cannot accumulate indefinitely.

## Verification boundary

The last closed exact-head Windows boundary is v8.8.82 source `f46793a18700524ac7b5fb34770200206d6595f2`, hosted-Windows run `37107057966`, with exact evidence at `_AGENT_CONTEXT/EVIDENCE/v8.8.82-heaven-windows-closure.log`. Evidence-only commits `00dcf4a5076335b69c636a2c0cb4c84ac333a43e` and `d57e888f392b1180c563e80c96df31fd9fd8cdc0` do not change that tested-source identity.

That evidence does **not** attest v8.8.83. v8.8.83 requires fresh exact-source verification covering PowerShell parsing/contract tests, self-test scratch deletion, integration tests, and the normal Windows release gate. Preserve the already-integrated v8.8.82 updater topology/storage-retention boundary while verifying this source.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Existing historical unmarked scratch must only be removed after proving it is stale and not process-owned; the permanent reaper deliberately does not guess.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
