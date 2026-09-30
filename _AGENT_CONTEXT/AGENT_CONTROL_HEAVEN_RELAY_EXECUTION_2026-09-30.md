# Agent Control Heaven relay execution repair — successor handoff

## What failed

The dashboard/operator action did create managed workers. The failure happened after dispatch, in the Heaven Bridge execution provider.

Authoritative runtime evidence for:
- `main-20260930042701-ldewu`
- `manager-20260930042717-kmbi6`

showed the same first failure:

`Error: AGENT_CONTROL_HEAVEN_RELAY_DIR is required for bridge execution.`

Both then reached authoritative exit code 1.

## Root cause

`resolveHeavenRelayDir()` already recognized the documented per-user `~/HeavenBridgeRepo` checkout, and health inspection used it. Actual `submitHeavenBridgeJob()` and `waitForHeavenBridgeResult()` bypassed that resolver and depended directly on the environment variable.

This produced a false-green configuration state: provider health could find the relay while every real dispatched Heaven job still failed.

## Implemented repair

- Add `resolveExecutionRelayDir()`.
- Keep an explicit caller relay path authoritative.
- Otherwise use `resolveHeavenRelayDir()`, including the documented `~/HeavenBridgeRepo` fallback.
- Route both job submit and result-wait through that execution resolver.
- Add a regression for the no-explicit-env documented fallback.
- Advance repo identity to v8.8.27 and Agent Control/plugin identity to v0.6.6.
- Record LR-051 and the matching generic training lesson/bug precedent.

## Registry cleanup status

The user's separate **failed · RETRY EXHAUSTED** cleanup request is already canonical in v8.8.26 / merged PR #463.

That lifecycle:
- proves controller ownership before killing a still-live PID;
- authoritatively cancels Heaven Bridge remote work when applicable;
- kills the owned process tree and verifies exit;
- refuses destructive cleanup if PID ownership is uncertain;
- refuses to delete dirty worktrees or committed divergent work;
- releases safe clean worktrees/leases;
- removes the dead managed + federated live-registry entries;
- records a retirement tombstone and preserves task/event history;
- suppresses replayed terminal observations until a genuine live heartbeat returns.

Do not replace this with a UI-only filter or broad terminal-state deletion.

## Verification still required

1. `cd tools/agent-control && npm run check && npm test` on the exact candidate.
2. Restart/reload exact candidate on heaven2.
3. With `%USERPROFILE%\HeavenBridgeRepo` installed and no explicit `AGENT_CONTROL_HEAVEN_RELAY_DIR`, dispatch a heaven-targeted worker.
4. Verify bridge submit/wait passes the former crash point.
5. Re-smoke retry-exhausted cleanup and fail-closed evidence preservation.
6. Sync with current main before integration, rerun exact-head checks after any reconciliation, merge, confirm remote main, and delete the temporary branch.

## Improvement opportunities

A future integration fixture should exercise submit + wait against a disposable dedicated relay checkout, not only the resolver helper. Keep HMAC signing/key rotation, repository identity, clean-checkout validation, and existing bridge trust checks unchanged; this repair narrows only path-resolution semantics.
