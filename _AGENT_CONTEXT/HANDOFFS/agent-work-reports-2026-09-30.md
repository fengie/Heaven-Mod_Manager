# Agent Work Reports handoff — 2026-09-30

## Delivered boundary

A standalone `plugins/agent-work-reports/` application provides a glanceable progress surface over the existing Agent Control snapshot plus explicit human-readable checkpoints. It is intentionally modular: Agent Control remains authoritative for orchestration and process/task state, while `lib/view-model.mjs` owns normalized reporting presentation data for later reuse inside Agent Manager.

The UI shows summary counts, one overall-work row per task, clickable/expandable agent cards, recent checkpoints, optional managed-agent logs, filtering/search, and recent activity. It refreshes every 4 seconds while visible and every 15 seconds while hidden.

## Reporting contract

Agents that can access the local plugin CLI should report `plan`, meaningful `progress`, `blocked`, verification-state changes, and `done` via `reportctl.mjs`. Managed Agent Control workers automatically receive agent/task IDs through environment variables. Reports are short, plain-language JSONL checkpoints and must not contain secrets or raw giant logs.

Agent Control state is always ingested automatically, so a managed/federated agent stays visible even if it misses an explicit checkpoint. Unknown external ChatGPT sessions are never fabricated; they become visible only through federation or an explicit report.

## Verification

Local exact-source plugin verification passed on 2026-09-30:

- Node syntax checks for server, CLI, adapters, view model, and UI script.
- 7/7 deterministic tests covering snapshot/log ingestion, loopback enforcement, report validation/persistence, corrupt-line tolerance, Agent Control/report merging, and report-only external agents.
- HTTP smoke confirmed report submission and dashboard rendering while Agent Control is unavailable, preserving local progress instead of dropping it.

## Successor actions

1. Keep the stable `agent-work-reports/view/v1` data contract when merging the reporting surface into Agent Manager; reuse/extract `lib/view-model.mjs` instead of duplicating orchestration logic.
2. Keep Agent Manager authoritative for dispatch, stop, leases, federation, task state, and authorization.
3. After this plugin lands, update current agent training/prompt generation so every managed worker emits the reporting checkpoints automatically.
4. Simplify the root README into milestone/larger-version changes only; keep patch-by-patch detail in CHANGELOG and continuity evidence.
5. Install the optional desktop shortcut only on the operator machine where the canonical checkout is known; the repository includes a hidden-window launcher and shortcut installer but this handoff does not claim desktop installation.

## Risks / limits

The reporting app is loopback-only by default and has no remote authentication boundary; do not expose it off-host without adding authentication. The local JSONL log is bounded to 16 MiB and intentionally stores summaries rather than full process logs.
