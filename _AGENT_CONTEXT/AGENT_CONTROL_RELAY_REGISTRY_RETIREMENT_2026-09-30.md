# Agent Control relay dispatch + retry-exhausted registry retirement — 2026-09-30

## User-visible failure

Agent Manager showed workers ending as **failed · RETRY EXHAUSTED** and left those dead executions in the managed/federated registry instead of clearing them. The repeated replacements also appeared to “not work” because they died before doing any task work.

## Confirmed root cause

Live heaven2 logs for `main-20260930042701-ldewu` and `manager-20260930042717-kmbi6` contain the same eight-line failure:

`Error: AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.`

The exception originates in `submitHeavenBridgeJob` via `heaven-bridge-runner.mjs`. The provider already had `resolveHeavenRelayDir()`, and bridge inspection used it, but job submission/result waiting still defaulted directly to the environment variable. As a result, health could look configured while actual execution failed before publishing a job.

A second lifecycle bug made the symptom persistent: bounded recovery changed `recoveryStatus` to `retry-exhausted` and saved state, while `syncManagedAgents()` continued mirroring that failed managed row into federation forever.

## Fix

- `submitHeavenBridgeJob` and `waitForHeavenBridgeResult` now default through `resolveHeavenRelayDir()`.
- A managed `failed + retry-exhausted` execution is retired after the process is already dead or controller ownership + termination are proven.
- Retirement preserves task status, blockers, events, logs, and the durable failure ledger, but removes the execution row from `state.agents` and its federation mirror.
- Federation synchronization prunes stale managed observations and refuses to re-ingest retry-exhausted managed failures.
- Federated-only retry-exhausted no-work observations are removed instead of persisting as historical live-registry clutter.
- Existing retry-exhausted rows are swept by the normal recovery reconciler so the current dirty registry self-cleans after the fixed controller starts.
- A live PID whose ownership cannot be proven is **not** killed or hidden; retirement fails closed and leaves explicit evidence instead.

## Verification contract

Before closure, run exact candidate `npm run check` and `npm test` under `tools/agent-control`, then restart/update Agent Control on heaven2 and prove:

1. `HeavenBridgeRepo` is discovered without requiring `AGENT_CONTROL_HEAVEN_RELAY_DIR`.
2. A real heaven worker job reaches the bridge instead of exiting before dispatch.
3. The existing retry-exhausted rows disappear from managed/federated registry surfaces.
4. Task/event/failure evidence for those old failures remains available.
5. A fresh failure fixture cannot leave an immortal retry-exhausted registry row.
