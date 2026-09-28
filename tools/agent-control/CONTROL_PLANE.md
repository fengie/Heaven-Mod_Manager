# Agent Control Plane Architecture

## Purpose

Heaven Agent Control is the local runtime layer above the repository's existing governance, training, continuity, review, and release doctrine.

Its job is not to replace GitHub or repository truth. Its job is to turn that truth into a recoverable engineering organization that remains useful when ChatGPT is closed.

```text
Operator / future clients
        |
        v
Command Center + agentctl CLI
        |
        v
Control-plane API
  |-- mission / recommendations / command resolution
  |-- task graph / ownership / leases
  |-- agent lifecycle / heartbeats / takeover
  |-- workflow presets / prompt provenance
  |-- review + integration queue
  |-- release evidence gate
  |-- machine policy / capacity
  |-- notifications / events / search
  |-- improvement proposals
        |
        +--> isolated local worker process + Git worktree
```

Canonical GitHub state and repository continuity remain authoritative engineering truth.
## Durable runtime state

`data/control-plane.json` is local operational state and is gitignored. Writes retain a backup copy; if both primary and backup state are unreadable, the controller enters degraded read-only mode and blocks dispatch.

The registry includes:

- `tasks[]`: objective, role, ownership, dependencies, branch/base SHA, acceptance criteria, verification obligations, evidence, blockers, and next action.
- `agents[]`: task/role, machine, branch/worktree, owner session, PID, exact base/current SHA, heartbeat/activity, prompt provenance, logs, completion evidence, review state, and lifecycle status.
- `leases[]`: mutable boundary, owner, task, acquisition/heartbeat/expiry state, and disposition.
- `events[]`: explainable operator/controller actions and their evidence.
- `notifications[]`: actionable failures, stale workers, review/integration state, and other operator-relevant events.
- `improvements[]`: governed Agent Manager improvement proposals and implementation linkage.
- `promptHistory[]`: exact template/version/hash provenance for dispatched work.

Runtime state is not a substitute for repository continuity. A meaningful implementation must still leave durable Git/continuity evidence.
## Lifecycle and recovery

Active states include reserved, starting, running, waiting, blocked, stale, and stopping. Terminal/uncertain states remain distinct: done, failed, stopped, interrupted, and orphaned are not interchangeable.

A controller restart never treats a persisted PID as durable process identity. Workers from another controller session are reconciled as interrupted/orphaned unless ownership and completion can be proven.

A last-message file is evidence only; it cannot promote interrupted work to successful completion.

Takeover generation preserves the smallest useful continuation packet: role, scope, branch, base/current SHA, task state, findings/evidence, blockers, constraints, and next action. Preserve-and-stop writes takeover state before attempting process termination.

Stop and Emergency Stop release mutable ownership only after process termination is proven for a worker owned by the current controller session.

## Workflows and command resolution

The workflow library is centralized in `lib/control-core.mjs`; prompt roles/sections are centralized and versioned in `lib/prompt-templates.mjs`.

One-click presets include Usual Swarm, Support Current Programmer, Integration, Review, Release, Bug Hunt, UI/UX, Updater Hardening, Verification, Research, Continuity, Cleanup, and Self-Improve.

Usual Swarm targets 1 Manager + 1 Primary Programmer + 4 distinct support lanes, but fills only missing useful work. Support lanes are deliberately different and task-sensitive.

Natural-language commands resolve to a governed workflow or a recommendation-only action. Unknown commands are staged for explicit bounded deployment instead of silently spawning work.
## Autonomy and emergency control

Autonomy levels are explicit:

- **Observe**: monitoring only.
- **Assist**: recommendations and previews; operator initiates meaningful work.
- **Coordinate**: bounded support/review/test coordination while integration stays approval-gated.
- **Engineering Autopilot**: safe routine coordination, stale replacement, verification, integration preparation, and continuity maintenance within repository policy.

Operational controls include pause/resume dispatch, drain, read-only mode, per-agent stop, preserve-and-stop, stop swarm, and Emergency Stop.

Emergency Stop first blocks new dispatch, then attempts to stop only workers whose ownership this controller can prove. Unknown/orphaned work is preserved rather than killed by stale PID.

## Machine policy

Machine placement fails closed when a requested runtime is not actually connected.

- **heaven**: background worker by default; no repository clone/fetch/pull/push/remotes/credentials/worktree/repository mutation without explicit per-task authorization; foreground UI is disallowed by policy.
- **heaven2**: primary development machine; repository development is permitted when genuinely required, but responsiveness should be preserved and routine scouting should not consume it unnecessarily.

The current release has local execution plus policy-aware visibility of configured machines. A true authenticated remote worker daemon/scheduler is a future boundary, not a fabricated capability.
## Review, integration, release, and search

Completed work does not become canonical automatically. Integration candidates retain branch/base/current SHA, ahead/behind state, completion evidence, review verdict, task evidence, and eligibility.

The release gate aggregates evidence without turning unknowns into passes. It checks canonical-source identity, stored verification applicability, unresolved integration candidates, uncertain workers, artifact provenance, and rollback/update proof.

Global search covers runtime tasks, agents, events, improvement proposals, repository commit subjects, and continuity text.

## Self-improvement

Feature requests can be captured as structured improvement proposals. Starting a proposal uses the governed Self-Improve workflow: isolated implementation, tests, independent review, compatibility consideration, and an upgrade candidate.

v0.4 deliberately stops before self-installation. Transactional Agent Manager update staging, migration backup, startup health verification, and automatic rollback require their own implementation/review boundary before they can be enabled.

## API/client boundary

The browser command center and `agentctl.mjs` both use the same local HTTP API. This keeps future desktop, phone, CLI, ChatGPT, and automation clients from coupling to internal process objects.

The HTTP server is unauthenticated by design only because it is loopback-only. Startup refuses non-loopback binding. Authenticated remote/mobile control must be introduced as a separate threat-modeled boundary.

## Verification

`npm run check` syntax-checks the server, CLI, workflow core, and prompt engine. `npm test` covers prompt provenance, state migration, swarm deduplication, command interpretation, machine policy, integration eligibility, recommendation ordering, takeover state, non-loopback refusal, corrupt-state fail-closed recovery, restart uncertainty, and command-center contracts.

Real Windows/MHW/package/release validation remains evidence-specific; ordinary JavaScript tests do not claim those platform outcomes.
