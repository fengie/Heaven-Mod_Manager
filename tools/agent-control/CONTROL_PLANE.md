# Agent Control Plane Architecture

## Goal

Turn the existing repo-native agent doctrine into a fleet with one authoritative runtime representation.

The repository already has strong training, continuity, verification, role, and integration doctrine. The missing layer is runtime orchestration.

## Runtime model

```text
User / ChatGPT
      |
      v
Agent Control Plane
  |-- Task registry
  |-- Worker registry
  |-- Mutable-boundary leases
  |-- Agent process registry
  |-- Federated provider/agent registry
  |-- Heartbeat + identity reconciliation
  |-- Branch/worktree registry
  |-- Event/history log
  |-- Integration queue
      |
      +--> Codex worker in isolated worktree
      +--> Reviewer/Test worker
      +--> Integration worker
```

## State

`data/control-plane.json` is local runtime state and is intentionally gitignored.

It contains:

- `tasks[]` — requested objective, role, priority, dependencies, machine, branch and lifecycle state.
- `agents[]` — controller-owned PID/session, task, role, branch, worktree, output paths and status.
- `federation.providers[]` — provider capability/health for local-control, ChatGPT bridge, GitHub/CI bridge, heaven2 control state, and future adapters.
- `federation.agents[]` — normalized logical agents with provider/source identities, correlation keys, task/branch/PR association, heartbeat, last action, and historical lifecycle.
- `leases[]` — one named mutable boundary → one managed owner at a time.
- `events[]` — bounded recent audit/history events.
- `autopilot` — durable big-direction orchestration state: phase, iteration/repair budgets, candidate/verification/review/repair worker IDs, last canonical-main observation, transition time, and governed stop reason.

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

v0.5.0 models `heaven2` as the control/credential authority and `heaven` as the preferred heavy execution worker.

The controller host is always exposed as a local worker with hostname, platform/architecture, CPU/memory, configured capacity, active slots, and heartbeat timestamp. When the controller runs on `heaven2`, `auto` placement prefers `heaven`.

Remote `heaven` execution is implemented through the authenticated **Heaven Local Bridge** relay provider:

1. the controller verifies the dedicated relay checkout, expected private repository, protocol, host identity, and fresh worker heartbeat;
2. it creates the ordinary local task/lease/worktree and renders the governed prompt;
3. a controller-owned local runner submits a uniquely identified bridge job;
4. `heaven` prepares an isolated workspace, runs Codex under the requested sandbox, and returns an authoritative terminal result;
5. the runner transfers a binary Git patch back and commits it only in the controller-owned isolated worktree;
6. the normal review/integration pipeline consumes that local branch.

A bridge heartbeat proves provider health, **not** process authority by itself. Dispatch proceeds only when the configured transport passes its health checks. If the bridge is missing, stale, on the wrong host/protocol/repository, dirty, or otherwise unhealthy, remote placement fails closed rather than silently running heavy work on `heaven2`.

Stop semantics remain ownership-safe: the controller cancels the exact recorded bridge job and requires authoritative cancellation success before terminating its proven-owned local runner. Unknown remote processes are never killed.

The remote worker intentionally does not push, publish, move credentials, or commit directly to canonical repository history. Git remains reconciled on the control side.

## Autonomy authorization

`settings.autonomyLevel` is an enforced server-side authorization boundary.

The declared profiles map to concrete permissions:

- `observe` — no action permissions;
- `assist` — `recommend`, `preview`;
- `coordinate` — adds `dispatch-support`, `replace-stale`, `request-review`, and `run-tests`;
- `engineering-autopilot` — adds `prepare-integration` and `maintain-continuity`.

Workflow execution has an explicit permission requirement. Direct deployment requires `dispatch-support`; review dispatch requires `request-review`; structured verification evidence may be recorded under `run-tests` (or `maintain-continuity`); other evidence/persisted-takeover mutation requires `maintain-continuity`; review verdict recording is allowed to `request-review` or `prepare-integration`, while self-improvement/integration preparation remains gated by `prepare-integration`. Unknown autonomy levels and unmapped workflows fail closed.

The default is `assist`, so a fresh controller can inspect, recommend, and preview without launching agents. The operator must explicitly raise autonomy before dispatch. Safety controls remain outside this restriction so pause/drain/emergency-stop/owned-worker stop and autonomy changes cannot be blocked by the current profile.

## Engineering autopilot

v0.5.0 adds a durable control loop for routine engineering work. A user supplies one high-level objective, and the controller advances only from authoritative state/evidence through:

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

## Federated identity and heartbeat

The live dashboard is derived from the normalized federation registry rather than raw process count. A provider observation supplies a stable `provider + source_id`, normalized lifecycle state, heartbeat, role, machine, task, branch/PR metadata, and optional strong correlation keys.

Federation schema v2 reconciles idempotently. Multiple observations from the same provider/source identity update one logical agent. Cross-provider observations merge only through explicit namespaced strong correlation evidence; task IDs, PRs, branches, titles, roles, and machine labels are never identity evidence. Ambiguous correlations fail closed, and an older observation cannot regress a newer provider/source heartbeat or aggregate lifecycle. Fresh `working`, `tool_wait`, `blocked`, and `idle` states are live. Stale/disconnected heartbeats remain visible but are excluded from live capacity/availability counts. `done` and `failed` are historical.

The registry exposes an AgentSource-style adapter contract for provider capability, normalization, ingestion/reconciliation, provider heartbeat, and discovery when the provider actually supports it. Persisted v1 registry state migrates to v2 without destructive reset. Unknown legacy providers are retained as `unsupported` for inspection but cannot ingest fresh observations until an adapter is installed.

Provider capability is explicit:
- local-control: automated discovery/registration;
- ChatGPT: bridge registration, automatic project-session discovery unavailable;
- GitHub/CI: bridge registration when stable PR/run identifiers are available;
- heaven2 control state: bridge registration.

Workflow planning consults fresh federated ownership so external Manager/Main/lane work suppresses obvious duplicate lanes. Local worker slot capacity remains based only on controller-owned processes.

## Observability

The UI currently reports:

- normalized live/working/tool-wait/blocked/idle/stale/disconnected agent counts
- worker capacity
- active leases
- candidate branches
- success rate
- average runtime in the API snapshot
- managed task/agent lifecycle
- federated provider health, runtime/machine, task, branch/PR, heartbeat freshness, and last action
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

The included private plugin is skills-only and uses the user-authorized Heaven Local Bridge to reach the authorized Heaven machine.

This avoids falsely claiming that ChatGPT cloud can reach Heaven's localhost interface.

A future native MCP mode can replace the bridge once the controller has an authenticated, intentionally exposed Streamable HTTP endpoint.

## Safety decisions

1. No automatic merge, release, or publish in v0.5.0.
2. No force-updating main.
3. One worktree per deployed worker.
4. Named leases prevent silent managed collisions.
5. Unknown remote workers fail closed.
6. Controller state is local operational data; Git remains canonical engineering state.
7. External Git branches are repository observations, while external agents count as live only through fresh normalized provider heartbeats.
8. Stopping a managed agent targets only its recorded process tree.
9. Operator stop intent is terminal: a zero exit after a stop request is recorded as `stopped`, not successful completion.
10. Child-exit Git evidence is collected before authoritative registry mutation so a stale whole-state snapshot is never saved after an asynchronous yield.
11. Counted deploys reserve capacity as a batch and fail before the first launch when the full request cannot fit.
12. Failures after task/lease reservation but before worker launch converge to a failed task and released lease, while retaining created worktree/branch evidence for explicit cleanup.
13. Autonomy permissions are enforced at server mutation/dispatch boundaries; UI labels or client behavior are not trusted as the authorization mechanism.
