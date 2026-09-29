# Heaven Agent Control Plane

A zero-dependency local control plane for the MHW programming-agent swarm.

This is the execution layer that sits above the repository's existing agent doctrine and continuity system:

**You → control plane → tasks/leasing → isolated Codex workers → review/integration queue → Git/CI**

## What v0.5.2 does

- Runs locally on `127.0.0.1:7331` by default.
- Deploys Manager, Main Programmer, Support, Reviewer, Test, Integration, Recovery, and Release Codex roles.
- Gives every deployed agent its own Git worktree and unique `agent/control-*` branch.
- Maintains a local process/task registry plus a normalized federated agent registry of:
  - managed local agents
  - ChatGPT bridge sessions
  - GitHub/CI observations
  - heaven/heaven2 machine/control observations
  - tasks
  - worker capacity
  - mutable-boundary leases
  - recent events
  - integration candidates
  - observed `agent/*`, `support/*`, `feature/*`, `ui/*`, and `integration/*` branches
- Tracks PID, machine, role, task, priority, branch, base branch, lease, timestamps, JSONL output, and final Codex output.
- Enforces local worker capacity from fresh managed liveness only; stale, disconnected, done, and failed records do not consume live slots.
- Lets you stop a managed worker.
- Lets you launch a reviewer against a completed agent branch with one click.
- Exposes the same control plane through `agentctl.mjs`, which ChatGPT can operate through Heaven Local Bridge.
- Includes a private ChatGPT plugin package under `chatgpt-plugin/`.
- Adds a durable engineering-autopilot state machine for a user-supplied big direction: sync/plan → implement → verify → review → bounded repair/reverify → integration-ready → continuity.
- Persists autopilot phase, iteration/repair budgets, exact candidate/worker IDs, canonical-main observation, transition timestamps, stop reason, and restart-resumable state.
- Requires fresh routing ownership plus structured verification/review evidence; it fails closed instead of inferring success from an agent's last prose message.
- Exposes autopilot start/pause/resume/stop/status through HTTP, `agentctl.mjs`, and the first-party dashboard.
- Gives operators truthful lifecycle counts (working, waiting, blocked, idle, stale, disconnected), provider failure details, lease/boundary provenance, integration readiness, and recent controller events.
- Exposes existing server-authorized control operations in both dashboard and CLI: autonomy changes, routing set/clear, pause/resume, read-only mode, drain, emergency stop, owned-agent stop, and swarm stop. The UI remains a client; server-side authorization and ownership checks remain authoritative.
- Classifies Codex usage/quota exhaustion as `capacity-blocked`, preserves the unfinished task/branch as blocked work, and opens a dispatch circuit until the provider reset window expires (or a later successful worker proves recovery). Direct Heaven Bridge `proc_run` build/test/filesystem/process/computer-control work remains available during the cooldown.

## Federated registry and heartbeat semantics

The controller remains authoritative only for processes it launches, while `federation` is the normalized view of the real swarm. Local managed workers are ingested automatically. ChatGPT sessions, GitHub/CI work, and heaven2 control state register through the bridge API because automatic ChatGPT project-session enumeration is not available.

Normalized states are `working`, `tool_wait`, `blocked`, `idle`, `done`, `failed`, and `disconnected`. Fresh non-terminal heartbeats are live. Stale or disconnected observations remain visible but do not inflate Active Agents. Completed and failed historical records never count as live.

Identity reconciliation uses stable provider/source identities plus explicit, namespaced strong correlation keys. Similar chat titles, tasks, branches, PR numbers, roles, and machine labels are metadata only and never become logical identity keys. The same logical worker can be correlated across ChatGPT and GitHub only when explicit strong evidence such as a logical-agent, session, workflow-run, bridge-job, or work-item key is supplied.

Federation schema v2 adds an AgentSource-style adapter boundary for normalize / ingest / reconcile / heartbeat / supported discovery, preserves runtime/session/conversation identity plus opaque source metadata, rejects unknown or unsupported providers at ingestion, rejects ambiguous multi-agent correlation, and ignores late observations that would regress a newer provider/source heartbeat. Persisted v1 registry data migrates non-destructively; legacy providers without an installed adapter remain inspectable as `unsupported` but cannot emit new observations.

External ownership also participates in planning: fresh federated Manager/Main/lane observations suppress duplicate workflow lanes. Exact task IDs are rejected when already owned by a live local or federated agent, while local execution capacity is calculated only from fresh controller-owned workers.

Automatic discovery is deliberately honest: `local-control` is automated; ChatGPT registration is bridge-based with discovery unavailable; GitHub/CI and heaven2 use bridge registration. Scheduler `auto` placement on `heaven2` prefers `heaven` for heavy work and fails closed when the authenticated Heaven Local Bridge is unavailable rather than silently falling back to `heaven2`.

## Start

From this directory:

```bat
Start Agent Control.bat
```

Or:

```powershell
$env:AGENT_CONTROL_REPO="$env:USERPROFILE\local-ai-workspaces\mhw-mods"
node .\server.mjs
```

Open:

```text
http://127.0.0.1:7331
```

## CLI

```powershell
node .\agentctl.mjs status
node .\agentctl.mjs snapshot
node .\agentctl.mjs workers
node .\agentctl.mjs federation
node .\agentctl.mjs providers
node .\agentctl.mjs events
node .\agentctl.mjs control
node .\agentctl.mjs federation-heartbeat --file C:\\Temp\\agent-heartbeat.json
node .\agentctl.mjs leases
node .\agentctl.mjs queue
node .\agentctl.mjs branches
node .\agentctl.mjs sync
node .\agentctl.mjs routing
node .\agentctl.mjs routing-set --file C:\Temp\routing.json
node .\agentctl.mjs routing-clear
node .\agentctl.mjs autonomy
node .\agentctl.mjs autonomy-set coordinate
node .\agentctl.mjs pause
node .\agentctl.mjs resume
node .\agentctl.mjs read-only on
node .\agentctl.mjs drain
node .\agentctl.mjs emergency-stop
node .\agentctl.mjs stop-swarm
node .\agentctl.mjs autopilot
node .\agentctl.mjs autopilot-start --task "Build the current big direction" --max-repairs 3
node .\agentctl.mjs autopilot-pause
node .\agentctl.mjs autopilot-resume
node .\agentctl.mjs autopilot-stop
node .\agentctl.mjs deploy --role support --task "Audit updater rollback" --count 2 --base agent/auto-updater-20260928
node .\agentctl.mjs deploy --role main --task-file C:\Temp\task.txt --boundary updater-release --priority 90
node .\agentctl.mjs review <agent-id>
node .\agentctl.mjs log <agent-id>
node .\agentctl.mjs stop <agent-id>
```

`--task-file` is the preferred interface for ChatGPT/plugin-driven deployment because the task never has to be shell-escaped.

### Bridge observation example

```json
{
  "provider": "chatgpt",
  "source_id": "conversation-stable-id",
  "role": "support",
  "machine": "cloud",
  "task": "Review Agent Manager federation",
  "branch": "feature/agent-control-plane-v2-20260928",
  "state": "tool_wait",
  "heartbeat_at": "2026-09-29T09:00:00Z",
  "correlation_keys": ["work-item:agent-control-59"]
}
```

POST observations to `/api/federation/observations` (or `/api/federation/heartbeat`). Provider-only health can heartbeat at `/api/federation/providers/<provider-id>/heartbeat`.

## Routing manifest

Use the routing manifest when live ownership exists outside this controller (for example, GitHub PRs or other agent sessions):

- `GET /api/control/routing-manifest` or `node agentctl.mjs routing` reads it.
- `POST /api/control/routing-manifest` or `node agentctl.mjs routing-set --file C:\\path\\routing.json` replaces it.
- `node agentctl.mjs routing-clear` clears it.
- `mode: "authoritative"` means the manifest defines the swarm slots and only open/missing/unassigned entries may be launched.
- `mode: "overlay"` augments normal planner heuristics with external ownership claims.
- `observedAt` + `expiresAt` scope freshness; broad automatic swarm execution refuses stale ownership.

Only ownership metadata belongs in this manifest. Never place credentials, access tokens, or other secrets in it.

## Autonomy enforcement

Autonomy is enforced by the server API, not just shown as UI metadata:

- `observe` — monitoring only. Workflow preview and governed control-plane mutations are denied.
- `assist` (default) — recommendations and workflow previews are allowed, but direct deploys, workflow execution, review dispatch, continuity mutation, integration preparation, and self-improvement starts are denied.
- `coordinate` — adds bounded support dispatch, review requests, and test/verification workflows. Integration preparation and continuity maintenance remain denied.
- `engineering-autopilot` — adds integration preparation and continuity maintenance while read-only, pause/drain, emergency-stop, machine-policy, ownership, and release governance still apply.

Use `node .\agentctl.mjs autonomy` to inspect the active profile and `node .\agentctl.mjs autonomy-set <level>` to change it explicitly.

Safety controls such as changing the autonomy level, pausing/draining, emergency stop, and stopping owned workers remain operator-accessible so a restrictive profile cannot trap the controller in an unsafe state.

## Environment variables

- `AGENT_CONTROL_PORT` — default `7331`
- `AGENT_CONTROL_HOST` — default `127.0.0.1`
- `AGENT_CONTROL_REPO` — default `%USERPROFILE%\local-ai-workspaces\mhw-mods`
- `AGENT_WORKTREE_ROOT` — default `%USERPROFILE%\agent-worktrees`
- `AGENT_CONTROL_MAX_ACTIVE` — default `8`
- `AGENT_CONTROL_MAX_DEPLOY_COUNT` — default `8`
- `AGENT_CONTROL_AUTOPILOT_TICK_MS` — autopilot control-loop cadence; default `4000` ms, minimum `1000`
- `AGENT_CONTROL_HEAVEN_RELAY_DIR` — dedicated local checkout used to exchange authenticated Heaven Bridge heartbeat/jobs/results on the `heaven-bridge` branch
- `AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY` — expected private relay repository; defaults to `fengie/mhw-mods`
- `AGENT_CONTROL_HEAVEN_HEARTBEAT_MAX_MS` — maximum accepted Heaven Bridge heartbeat age
- `AGENT_CONTROL_HEAVEN_REPO_URL` — optional repository URL used by remote Heaven workspace preparation
- `CODEX_EXE` — optional explicit path to `codex.exe`; otherwise the newest ChatGPT Codex install is discovered automatically.

## Safety / isolation

- The server binds to localhost by default.
- Browser requests from unrelated origins are rejected.
- It never checks out or force-updates `main`.
- Each deployment creates a separate worktree and branch.
- A named mutable-boundary lease prevents two managed agents from silently owning the same implementation surface.
- Stopping a worker targets only the PID launched by this controller.
- Operator stop intent dominates a zero exit code: a stopped worker remains `stopped` and cannot become an integration candidate.
- Authoritative child-exit handling collects asynchronous Git evidence before loading and mutating the registry, preventing a stale whole-state snapshot from overwriting newer control-plane changes.
- Counted deploy requests preflight the whole batch against available capacity, so a near-capacity request is rejected before any partial worker launch.
- Pre-launch setup failures converge reserved tasks to failed, release their lease only because no process was launched, and explicitly retain any created worktree/branch for evidence-safe cleanup.
- Broad `usual-swarm` execution requires current reconciled ownership context.
- An authoritative routing manifest fills only manager-declared open slots; claimed external ownership counts as occupied, while stale/superseded claims do not block a lane forever.
- Autonomy permissions are checked server-side before workflow execution, direct deployment, review dispatch, persisted takeover/evidence mutation, integration verdict mutation, and self-improvement execution.
- Engineering autopilot is control-authority-bound to `heaven2`; it rechecks remote `main` with `git ls-remote` and requires a current routing manifest before advancing.
- On `heaven2`, `auto` placement prefers `heaven`. Dispatch fails closed if the dedicated relay checkout or authenticated worker heartbeat cannot be proven healthy; it does not silently fall back to heavy execution on `heaven2`.
- Governed engineering-autopilot dispatch explicitly authorizes only the scoped remote repository work it creates. The Heaven runner does not push or publish: it executes Codex in an isolated remote checkout, transfers a binary patch back, and commits only in the controller-owned local worktree.
- Stopping a bridge-backed worker first requires an authoritative cancellation result for the owned remote job before terminating the local runner process or releasing its lease.
- Autopilot stops at stale ownership, missing structured verification/review evidence, worker-capacity or lease preflight failure, exhausted repair budget, degraded/read-only/emergency state, and the final integration approval boundary.
- The controller does not merge, release, or publish branches automatically.
- Integration queue state is advisory until a reviewer/integration agent and the repository's own verification requirements approve the work.

## ChatGPT plugin

`chatgpt-plugin/` contains a private skills-only plugin designed to let ChatGPT operate this local controller through the already connected **Heaven Local Bridge** app.

That bridge is intentional: ChatGPT cloud cannot directly call `127.0.0.1` on Heaven. The plugin uses the user-authorized Heaven Local Bridge to invoke `agentctl.mjs`, register session heartbeats, start the controller when needed, deploy agents, inspect snapshots, read logs, stop proven-owned workers, and launch reviewers.

See `CONTROL_PLANE.md` for the current v0.5.1 federation/liveness architecture, authenticated Heaven Bridge execution transport, heartbeat semantics, and engineering-autopilot state machine.
