# Agent Control Plane Architecture

## Goal

Turn the existing repo-native agent doctrine into a fleet with one authoritative runtime representation.

The repository already has strong training, continuity, verification, role, and integration doctrine. The missing layer is runtime orchestration.

## Runtime model

```text
User / Operator
      |
      v
Agent Manager UI / CLI
      |
      v
Federated provider layer
  |-- local-control (automated)
  |-- ChatGPT bridge registration
  |-- heaven2 control bridge
  |-- GitHub / CI bridge
      |
      v
Normalized agent registry
      |
      v
Heartbeat + identity reconciliation
      |
      +--> routing / duplicate-work prevention
      +--> task / lease / worker placement
      +--> review / verification / integration
```

## State

`data/control-plane.json` is local runtime state and is intentionally gitignored.

It contains:

- `tasks[]` — requested objective, role, priority, dependencies, machine, branch and lifecycle state.
- `agents[]` — PID/session, task, role, branch, worktree, output paths and status.
- `leases[]` — one named mutable boundary → one managed owner at a time.
- `events[]` — bounded recent audit/history events.
- `autopilot` — durable big-direction orchestration state: phase, iteration/repair budgets, candidate/verification/review/repair worker IDs, last canonical-main observation, transition time, and governed stop reason.
- `federation.providers[]` — provider capability/health records including discovery mode, registration mode, heartbeat, and error state.
- `federation.agents[]` — normalized logical agents with stable identity, provider observations, role, machine, task, branch/PR, heartbeat, lifecycle state, last action, and correlation metadata.

Git remains the durable engineering source of truth. Runtime state is operational metadata, not a replacement for repository continuity documents.

## Lease semantics

A lease protects a mutable implementation boundary.

Examples:

- `updater-release-pipeline`
- `main-window-shell`
- `archive-extraction`
- `agent-control-plane`

If the boundary is omitted, the controller creates an isolated per-agent lease tied to the worker branch.

Explicit multi-agent deploys with one named boundary receive numbered sub-boundaries so the controller does not immediately deadlock a requested support fan-out.

The current controller releases a lease when its managed process ends. Future versions should support renewable TTL leases and explicit transfer.

## Worker scheduling

v0.5.0 registers the controller host as one local worker with:

- hostname
- platform/architecture
- CPU count
- free/total RAM
- configured capacity
- active slots
- heartbeat timestamp

`heaven2` is the control/credential authority. `heaven` is the preferred heavy worker. On `heaven2`, `auto` targets `heaven`; if no authenticated remote worker transport is connected, dispatch fails clearly instead of silently executing heavy work on the control machine. `local` explicitly means the controller host.

### Remote worker transport

The normalized registry and provider heartbeat model exist now, but registration is not execution transport. A bridge/provider may report that a worker exists without granting the controller authority to start processes there.

Until an authenticated remote worker daemon/transport is connected, remote dispatch fails closed. Do not infer execution authority from a fresh heartbeat alone.

## Federated registry and heartbeat semantics

Each observation identifies a source with stable `provider + source_id`. The reconciler assigns a logical `agent_id`; repeated observations are idempotent, while explicit correlation keys can join observations from different providers into one logical worker.

Normalized lifecycle states are:

- `working`
- `tool_wait`
- `blocked`
- `idle`
- `done`
- `failed`
- `disconnected`

Fresh `working/tool_wait/blocked/idle` records count as live. Once heartbeat age crosses the stale threshold, the worker remains visible but is removed from live counts. Once it crosses the disconnected threshold, its effective state becomes `disconnected`. `done` and `failed` are historical regardless of heartbeat age.

Automatic discovery is implemented for `local-control`. ChatGPT arbitrary-session discovery is unavailable, so ChatGPT sessions use bridge registration. GitHub/CI and `heaven2` control observations also use the same bridge contract when stable identifiers are available. Unsupported discovery is shown as unavailable; no fake telemetry is generated.

The planner consults both the fresh normalized registry and the routing manifest when deciding whether a role/lane is already occupied.

## Autonomy authorization

`settings.autonomyLevel` is an enforced server-side authorization boundary.

The declared profiles map to concrete permissions:

- `observe` — no action permissions;
- `assist` — `recommend`, `preview`;
- `coordinate` — adds `dispatch-support`, `replace-stale`, `request-review`, and `run-tests`;
- `engineering-autopilot` — adds `prepare-integration` and `maintain-continuity`.

Workflow execution has an explicit permission requirement. Direct deployment requires `dispatch-support`; review dispatch requires `request-review`; evidence/persisted-takeover mutation requires `maintain-continuity`; integration verdict mutation and self-improvement execution require `prepare-integration`. Unknown autonomy levels and unmapped workflows fail closed.

The default is `assist`, so a fresh controller can inspect, recommend, and preview without launching agents. The operator must explicitly raise autonomy before dispatch. Safety controls remain outside this restriction so pause/drain/emergency-stop/owned-worker stop and autonomy changes cannot be blocked by the current profile.

## Engineering autopilot

v0.5.0 preserves the durable control loop for routine engineering work. A user supplies one high-level objective, and the controller advances only from authoritative state/evidence through:

```text
sync-plan -> implement -> verify -> review
                         ^          |
                         |          v
                    reverify <- repair
                                   |
                                   v
integration-ready -> continuity -> operator integration gate
```

The loop is restart-resumable because its phase and worker references live in `control-plane.json`, not in chat history. It never treats last-message prose as proof. Verification requires structured task evidence; review requires an explicit structured verdict. Failed verification/review routes through a bounded repair budget.

Every active cycle observes canonical `origin/main` without mutating shared refs and requires a freshness-scoped routing manifest before dispatch. The controller must run on `heaven2`, which remains the control/credential authority. Heavy/background work may move to `heaven` only when remote worker transport is actually connected and policy-safe; the controller does not pretend unsupported remote execution exists.

The first implementation intentionally stops before merge/release/publish and preserves a takeover artifact at the integration boundary.

## Integration queue

Terminal managed agents are compared against their base branch.

The queue records:

- branch
- base
- ahead/behind counts
- agent/task
- completion state
- last output

This queue is intentionally not an automatic merger. It is the input to reviewer/integration agents and the repository's exact-SHA verification gates.

## Observability

The UI currently reports:

- live normalized agents across providers
- working/tool-wait/blocked/idle/stale/disconnected counts
- provider/source/machine and heartbeat freshness
- worker capacity
- active leases
- candidate branches
- success rate
- average runtime in the API snapshot
- managed task/agent lifecycle
- discovered swarm branches
- recent controller events

Future telemetry should add:

- token/cost accounting
- duplicate-work rate
- reverted-change rate
- verification failure rate
- manager interventions
- stale-branch age
- time-to-review
- time-to-merge
- prompt/role effectiveness

## ChatGPT integration

The included private plugin is skills-only and prefers the user's authorized Heaven Local Bridge to reach `heaven`. This avoids falsely claiming that ChatGPT cloud can reach Heaven's localhost interface and avoids silently substituting another remote-control system.

The same bridge can register ChatGPT/GitHub/control-machine observations through the federation API. Registration proves observation only; it does not grant remote process-execution authority.

## Safety decisions

1. No automatic merge, release, or publish in v0.5.0.
2. No force-updating main.
3. One worktree per deployed worker.
4. Named leases prevent silent managed collisions.
5. Unknown remote workers fail closed.
6. Controller state is local operational data; Git remains canonical engineering state.
7. External providers are visible only from real registered observations; unsupported discovery is labeled unavailable and never synthesized.
8. Stopping a managed agent targets only its recorded process tree.
9. Operator stop intent is terminal: a zero exit after a stop request is recorded as `stopped`, not successful completion.
10. Child-exit Git evidence is collected before authoritative registry mutation so a stale whole-state snapshot is never saved after an asynchronous yield.
11. Counted deploys reserve capacity as a batch and fail before the first launch when the full request cannot fit.
12. Failures after task/lease reservation but before worker launch converge to a failed task and released lease, while retaining created worktree/branch evidence for explicit cleanup.
13. Autonomy permissions are enforced at server mutation/dispatch boundaries; UI labels or client behavior are not trusted as the authorization mechanism.
14. Historical completed/failed records and stale/disconnected observations do not inflate the live-agent count.
15. Similar ChatGPT titles never establish identity; stable source identifiers or explicit correlation keys are required.
16. A remote heartbeat is not treated as execution authority; unsupported remote placement fails closed.
