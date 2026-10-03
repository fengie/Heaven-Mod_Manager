# v8.8.83 self-cleaning test scratch — canonical state

v8.8.83 closes the unbounded disposable-test scratch leak reported in issue #650. Raw self-test/updater workspaces are transient again; durable evidence remains outside scratch where required.

## Behavior

- MHW self-test writes an ownership marker, clears Microsoft.Data.Sqlite pools, and retries recursive deletion instead of silently ignoring Windows file-lock failures.
- Installed-client updater E2E writes the same bounded ownership marker, stops processes launched from its install roots, and fails visibly if its per-run temp tree cannot be removed after retries.
- The disposable updater harness persists only its compact summary/evidence outside the disposable profile and removes the marked profile in `finally` on success or failure.
- `scripts/testing/Clear-StaleTestScratch.ps1` only reaps old marked `MhwManagerSelfTest-*` and `mhw-updater-installed-e2e-*` roots whose exact owning process is no longer alive. It refuses unmarked roots, active owners, reparse points, unrelated temp paths, repositories, user Mods/State, and WSL virtual disks.
- Normal test orchestration runs the conservative stale reaper before tests so an interrupted older run cannot accumulate indefinitely.

## Verification boundary

Current hosted-Windows closure: v8.8.83 source `ed7006e18d1fdbbdedd2209d4b766b5db938add9` passed run `37108647747` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.83-heaven-windows-closure.log`.

The tested source remains `ed7006e18d1fdbbdedd2209d4b766b5db938add9` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Existing historical unmarked scratch must only be removed after proving it is stale and not process-owned; the permanent reaper deliberately does not guess.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, filesystem containment, and durable-evidence privacy boundary, and recursively propagate the same obligation.
