# v8.8.82 self-cleaning test scratch — canonical state

v8.8.82 closes the unbounded disposable-test scratch leak reported in issue #650. Raw self-test/updater workspaces are transient again; durable evidence remains outside scratch where required.

## Behavior

- MHW self-test writes an ownership marker, clears Microsoft.Data.Sqlite pools, and retries recursive deletion instead of silently ignoring Windows file-lock failures.
- Installed-client updater E2E writes the same bounded ownership marker, stops processes launched from its install roots, and fails visibly if its per-run temp tree cannot be removed after retries.
- The disposable updater harness persists only its compact summary/evidence outside the disposable profile and removes the marked profile in `finally` on success or failure.
- `scripts/testing/Clear-StaleTestScratch.ps1` only reaps old marked `MhwManagerSelfTest-*` and `mhw-updater-installed-e2e-*` roots whose exact owning process is no longer alive. It refuses unmarked roots, active owners, reparse points, unrelated temp paths, repositories, user Mods/State, and WSL virtual disks.
- Normal test orchestration runs the conservative stale reaper before tests so an interrupted older run cannot accumulate indefinitely.

## Verification boundary

The last closed hosted-Windows boundary remains v8.8.81 source `aa5e56afa5bf8db73bcb7ac0af0c9825874c736a`, run `37105670926`. That evidence does **not** attest v8.8.82.

v8.8.82 requires fresh exact-source verification covering PowerShell parsing/contract tests, self-test scratch deletion, the integration suite, and the normal Windows release gate. Do not promote the previous closure to this source.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Existing historical unmarked scratch must only be removed after proving it is stale and not process-owned; the permanent reaper deliberately does not guess.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
