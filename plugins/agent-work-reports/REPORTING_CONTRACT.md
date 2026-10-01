# Agent Work Reporting Contract

Agent Work Reports is the human-readable progress surface for engineering agents. It is intentionally separate from Agent Manager control logic so the two UIs can share the same data model and merge later without coupling reporting to orchestration.

## What must be visible

Every known agent should have one current card with its role, task, state, latest plain-language update, branch/machine/provider when known, and recent checkpoints. Overall work should have one row per task. Full managed-agent logs stay available on demand instead of being copied into the report store.

Agent Control already supplies assignment, plan/objective, lifecycle status, last message, events, tasks, federation state and managed-agent logs. Agent Work Reports consumes those records automatically, so a temporarily silent agent does not disappear. External sessions are shown only when they are honestly registered/federated or explicitly report; the app never invents undiscoverable ChatGPT telemetry.

## Explicit checkpoints

Agents that can reach the local reporting CLI should emit a short checkpoint at these boundaries:

1. `plan` — after the task is understood and before implementation.
2. `progress` — whenever a meaningful code, test, review, integration, documentation, or decision state changes.
3. `blocked` — as soon as work cannot proceed, including the concrete blocker and next action.
4. `progress` — when verification starts or its result changes.
5. `done` — only when the repository's real completion contract is met; use 100% only for actual completion.

Keep wording simple: what changed, what is happening now, and what is next. Do not paste secrets, credentials, raw environment values, huge logs, or private prompt text into reports. Managed logs remain behind the read-only Agent Control log endpoint.

From an Agent Control worker, IDs are supplied automatically:

```powershell
node .\plugins\agent-work-reports\reportctl.mjs plan --summary "Inspecting updater state and regression tests" --progress 5
node .\plugins\agent-work-reports\reportctl.mjs progress --summary "Regression added; implementing the fix" --progress 55
node .\plugins\agent-work-reports\reportctl.mjs blocked --summary "Waiting for exact-head CI" --details "Local focused tests passed; CI is still pending."
node .\plugins\agent-work-reports\reportctl.mjs done --summary "Merged to main and remote main verified" --progress 100
```

If the dashboard server is down, `reportctl.mjs` writes the same bounded checkpoint to the local JSONL store so it appears when the app starts again.

## Merge boundary with Agent Manager

The reusable boundary is `lib/view-model.mjs`: it turns the existing Agent Control `/api/snapshot` plus report checkpoints into the small `agent-work-reports/view/v1` model used by the UI. A future Agent Manager merge should reuse or extract that adapter/view model rather than copy this dashboard's control logic. Agent Work Reports must remain read-mostly; Agent Manager stays authoritative for orchestration, ownership, stopping, dispatch and authorization.
