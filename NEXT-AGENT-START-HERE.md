# v8.8.83 self-cleaning test scratch — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #650

## v8.8.83 behavior

- Self-test scratch is marker-owned, clears SQLite pools before teardown, and uses bounded delete retries.
- Installed-client updater E2E stops owned install-root processes and removes its marked per-run temp tree; cleanup failures are no longer swallowed.
- The disposable updater harness keeps compact evidence outside the disposable profile and deletes raw profile/workspace data in `finally`.
- `Clear-StaleTestScratch.ps1` conservatively reaps only old, marked, no-longer-owned MHW scratch roots and refuses unmarked/reparse/active/unrelated paths.
- `Run-Tests.ps1` performs stale marked-scratch maintenance before running test suites.

## Verification boundary

The last closed hosted-Windows boundary is v8.8.82 source `f46793a18700524ac7b5fb34770200206d6595f2`, run `37107057966`, with exact evidence at `_AGENT_CONTEXT/EVIDENCE/v8.8.82-heaven-windows-closure.log`. The later commits `00dcf4a5076335b69c636a2c0cb4c84ac333a43e` and `d57e888f392b1180c563e80c96df31fd9fd8cdc0` persist verification/updater-E2E evidence only and do not change the tested-source identity.

v8.8.83 changes cleanup behavior and therefore requires fresh exact-source verification; do not inherit the v8.8.82 closure. Required verification includes focused disposable-harness contract checks, self-test cleanup proof, integration tests, PowerShell syntax/policy checks, and the normal Windows release gate on the exact source.

## Unresolved risks and next work

The permanent stale reaper intentionally refuses historical unmarked roots. Any one-time deletion of pre-v8.8.83 scratch must first establish age and lack of active process ownership. #559/#558, #350/#354, and RECOVERY-005/RECOVERY-007 remain independent.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, the v8.8.82 updater topology/storage-retention boundary, filesystem containment, and the durable-evidence privacy boundary. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
