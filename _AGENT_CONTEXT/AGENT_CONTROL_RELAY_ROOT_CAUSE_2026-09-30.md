# Agent Control Heaven relay dispatch root-cause handoff

## Confirmed failure

The worker click/dispatch path was creating Agent Control workers, but the launched Heaven Bridge runner exited with code 1 before useful work. Authoritative worker logs for `main-20260930042701-ldewu` and `manager-20260930042717-kmbi6` both reported:

`Error: AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.`

The failure came from `submitHeavenBridgeJob()` / `runHeavenBridgeAction()`, not from the dashboard click handler.

## Root cause and repair

`resolveHeavenRelayDir()` already discovers the documented `~/HeavenBridgeRepo` checkout when the env var is absent, and health inspection used that resolver. Actual job submission and result waiting bypassed it and defaulted directly to `process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR`.

v8.8.26 adds `resolveExecutionRelayDir()` and routes both submit and wait through it. Explicit caller paths remain authoritative; otherwise the documented per-user checkout is used.

PR #459 also owns the terminal-registry cleanup. During review, its retention test exposed a contradiction: `retry-blocked` was listed as active recovery even though the regression expected terminal blocked records to retire. The implementation was corrected to retain retry-pending/retry-waiting/stream-checking/incomplete-work states while allowing terminal retry-blocked records to retire when process/durable-work guards do not require preservation.

## Required next proof

1. Run exact-head `npm run check && npm test` in `tools/agent-control`.
2. Restart/reload the exact candidate on heaven2.
3. With `%USERPROFILE%\HeavenBridgeRepo` present and no explicit `AGENT_CONTROL_HEAVEN_RELAY_DIR`, dispatch one heaven-targeted worker and prove it gets past bridge submit/wait.
4. Verify dead failed/retry-exhausted/retry-blocked tombstones retire from managed + federated live registries after ownership/recovery guards clear.
5. Verify retry-pending/retry-waiting and durable incomplete-work records remain.
6. Verify task/event/failure/branch/worktree evidence survives registry retirement.
7. Merge only after exact-head checks; then verify current main and delete the task branch.

## Improvement opportunities

A future integration fixture should exercise submit/wait against a disposable dedicated relay checkout, not only the resolver helper. Keep relay HMAC/repository-identity/clean-checkout validation intact; this fix changes path resolution only and must not weaken bridge trust checks.
