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
- `agents[]` — PID/session, task, role, branch, worktree, output paths and status.
- `leases[]` — one named mutable boundary → one managed owner at a time.
- `events[]` — bounded recent audit/history events.

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

v0.3.1 registers the controller host as one local worker with:

- hostname
- platform/architecture
- CPU count
- free/total RAM
- configured capacity
- active slots
- heartbeat timestamp

Placement currently accepts only `auto`, `local`, or the controller hostname. A request for another machine fails instead of pretending the task was deployed.

### Next worker layer

Add a small worker daemon on Heaven/Heaven2 that registers with the controller and periodically heartbeats:

```text
worker_id
machine
capabilities
models
active_slots
cpu
memory
last_heartbeat
```

Then the scheduler can choose machines by capacity and policy.

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

- running agents
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

The included private plugin is skills-only and uses Remote Desktop Commander to reach the authorized Heaven machine.

This avoids falsely claiming that ChatGPT cloud can reach Heaven's localhost interface.

A future native MCP mode can replace the bridge once the controller has an authenticated, intentionally exposed Streamable HTTP endpoint.

## Safety decisions

1. No automatic merge in v0.2.
2. No force-updating main.
3. One worktree per deployed worker.
4. Named leases prevent silent managed collisions.
5. Unknown remote workers fail closed.
6. Controller state is local operational data; Git remains canonical engineering state.
7. External Git branches are observable but not falsely treated as live managed processes.
8. Stopping a managed agent targets only its recorded process tree.
9. Operator stop intent is terminal: a zero exit after a stop request is recorded as `stopped`, not successful completion.
10. Child-exit Git evidence is collected before authoritative registry mutation so a stale whole-state snapshot is never saved after an asynchronous yield.
