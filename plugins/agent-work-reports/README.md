# Agent Work Reports

A small local app that makes the whole agent swarm readable at a glance. It is intentionally separate from Agent Manager today, but its `agent-work-reports/view/v1` view-model contract is designed to be consumed by Agent Manager later without duplicating state logic.

## What you see

- summary counts for active, blocked, completed and known agents;
- one overall work list, sorted so blocked/active work is visible first;
- one card per managed, federated or report-only agent;
- click-to-expand details instead of long always-open panels;
- current task, branch, machine/provider, status, progress and latest update;
- on-demand managed-agent logs;
- a collapsed recent-activity feed;
- automatic refresh every 4 seconds while visible and every 15 seconds while hidden.

Agent Control is the primary live source. The app reads `GET /api/snapshot` from loopback only and never mutates Agent Control. Explicit agent reports are persisted separately as bounded JSONL under the gitignored `data/` directory, so the reporting UI still has useful context if Agent Control is temporarily unavailable.

## Start it

On Windows, double-click:

`Start Agent Work Reports.bat`

That starts the local server minimized and opens `http://127.0.0.1:7341`.

Or run:

```powershell
node .\plugins\agent-work-reports\server.mjs
```

Environment overrides:

- `AGENT_WORK_REPORTS_PORT` — default `7341`;
- `AGENT_WORK_REPORTS_DATA_DIR` — defaults to `plugins/agent-work-reports/data`;
- `AGENT_CONTROL_BASE_URL` — defaults to Agent Control on `127.0.0.1:7331`.

The server binds loopback by default. A non-loopback bind is refused unless explicitly opted into because this first version has no remote authentication boundary.

## Agent reporting

Managed Agent Control work appears automatically from the existing task/agent/event state. Agents can add a clearer human-readable checkpoint at any time:

```powershell
node .\plugins\agent-work-reports\reportctl.mjs plan --summary "Inspect current UI and define the smallest safe fix"
node .\plugins\agent-work-reports\reportctl.mjs progress --summary "Regression test added; implementation in progress" --progress 60
node .\plugins\agent-work-reports\reportctl.mjs blocked --summary "Exact-head CI is unavailable"
node .\plugins\agent-work-reports\reportctl.mjs done --summary "Merged and verified on remote main" --progress 100
```

When launched by Agent Control, the CLI automatically uses `AGENT_CONTROL_AGENT_ID` and `AGENT_CONTROL_TASK_ID`. If the reports server is not running, the CLI falls back to the same local JSONL store so the update is not lost.

Each checkpoint is intentionally simple: phase, short summary, optional details/progress/branch. Never put credentials, tokens or secrets in a report.

## API / merge boundary

The stable integration surface for the future Agent Manager merge is:

- `GET /api/view` → `agent-work-reports/view/v1` with `stats`, `work`, `agents`, and `activity`;
- `POST /api/report` → append a bounded human-readable checkpoint;
- `GET /api/reports` → filter persisted reports;
- `GET /api/agents/:id/log` → read-only proxy to Agent Control logs.

Pure normalization/aggregation lives in `lib/view-model.mjs`; persistence lives in `lib/report-store.mjs`; Agent Control transport lives in `lib/agent-control-adapter.mjs`; the UI is a replaceable client. When Agent Manager and this app are merged, reuse these boundaries rather than copying dashboard logic.

## Security boundary

- loopback-only by default;
- read-only access to Agent Control;
- no credentials or Agent Control task tokens are stored;
- report payloads are size-bounded and plain text is length-bounded;
- report data is gitignored local runtime state;
- logs are loaded only when the operator clicks **View log**.

## Validation

```powershell
python .\plugins\agent-work-reports\verify.py
```

This performs Node syntax checks and deterministic tests for report validation/persistence and Agent Control/report view-model merging.

## Ownership / migration

This package owns agent-progress presentation and report normalization only. Agent Control remains authoritative for orchestration, process ownership, task state, federation and control actions. The intended long-term direction is one Agent Manager surface with this package retained as a modular reporting/view-model layer.
