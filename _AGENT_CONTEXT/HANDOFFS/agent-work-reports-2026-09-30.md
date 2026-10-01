# Agent Work Reports handoff — 2026-09-30

## Integration checkpoint

v8.8.53 source/release integration is complete. PR #540 exact head `b295586beef4e56c7bdc2968b005cc44354c86f5` passed all four applicable exact-head gates and merged as `2885609c8db18494dc5d89d7e9545732ab2bf741` with the tested tree preserved. Hosted Windows closure `36794493034` passed 26/26 and produced updater build 362; installed-client updater E2E `36794962203` passed update and rollback. The remaining acceptance gap is the live authenticated heaven2 loopback dashboard smoke; it is not claimed here.

## Delivered boundary

A standalone `plugins/agent-work-reports/` application provides a glanceable progress surface over the existing Agent Control snapshot plus explicit human-readable checkpoints. It is intentionally modular: Agent Control remains authoritative for orchestration and process/task state, while `lib/view-model.mjs` owns normalized reporting presentation data for later reuse inside Agent Manager.

The UI shows summary counts, one overall-work row per task, clickable/expandable agent cards, recent checkpoints, optional managed-agent logs, filtering/search, and recent activity. It refreshes every 4 seconds while visible and every 15 seconds while hidden.

## Reporting contract

Agents that can access the local plugin CLI should report `plan`, meaningful `progress`, `blocked`, verification-state changes, and `done` via `reportctl.mjs`. Managed Agent Control workers automatically receive agent/task IDs through environment variables. Reports are short, plain-language JSONL checkpoints and must not contain secrets or raw giant logs.

Agent Control state is always ingested automatically, so a managed/federated agent stays visible even if it misses an explicit checkpoint. Unknown external ChatGPT sessions are never fabricated; they become visible only through federation or an explicit report.

## Verification

Verification accumulated in layers:

- Historical local exact-source verification passed Node syntax checks, 7/7 deterministic tests, and HTTP fallback smoke.
- Exact PR head `b295586beef4e56c7bdc2968b005cc44354c86f5` passed Security Supply Chain `36793284454`, Agent Control `36793284492`, Plugin Toolbox `36793284463`, and Workflow Feature `36793284449`.
- Merge source `2885609c8db18494dc5d89d7e9545732ab2bf741` passed hosted Windows closure `36794493034`: 26/26, updater build 362.
- Installed-client updater E2E `36794962203` passed success and rollback.
- Live heaven2 Agent Work Reports dashboard acceptance is still pending. The heaven2 bridge is HMAC-required, while the exact-main heaven provider probe has no signing key; do not bypass that trust boundary.

## Successor actions

1. From an authorized signed or machine-local heaven2 path, smoke the loopback dashboard against canonical main: rendering, Agent Control ingestion, explicit reports, fallback, refresh cadence, and logs. Do not weaken HMAC or expose the loopback service to manufacture evidence.
2. Keep the stable `agent-work-reports/view/v1` data contract when merging the reporting surface into Agent Manager; reuse/extract `lib/view-model.mjs` instead of duplicating orchestration logic.
3. Keep Agent Manager authoritative for dispatch, stop, leases, federation, task state, and authorization.
4. After the live smoke closes this lane, update current agent training/prompt generation so every managed worker emits the reporting checkpoints automatically.
5. Simplify the root README into milestone/larger-version changes only; keep patch-by-patch detail in CHANGELOG and continuity evidence.
6. Install the optional desktop shortcut only on the operator machine where the canonical checkout is known; the repository includes a hidden-window launcher and shortcut installer but this handoff does not claim desktop installation.

## Risks / limits

The reporting app is loopback-only by default and has no remote authentication boundary; do not expose it off-host without adding authentication. The local JSONL log is bounded to 16 MiB and intentionally stores summaries rather than full process logs.
