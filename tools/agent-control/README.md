# Heaven Agent Control Plane

A zero-dependency local control plane for the MHW programming-agent swarm.

This is the execution layer that sits above the repository's existing agent doctrine and continuity system:

**You → control plane → tasks/leasing → isolated Codex workers → review/integration queue → Git/CI**

## What v0.5.0 does

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
- Enforces a local active-worker capacity.
- Lets you stop a managed worker.
- Lets you launch a reviewer against a completed agent branch with one click.
- Exposes the same control plane through `agentctl.mjs`, which ChatGPT can operate through Heaven Local Bridge.
- Includes a private ChatGPT plugin package under `chatgpt-plugin/`.
- Adds a durable engineering-autopilot state machine for a user-supplied big direction: sync/plan → implement → verify → review → bounded repair/reverify → integration-ready → continuity.
- Persists autopilot phase, iteration/repair budgets, exact candidate/worker IDs, canonical-main observation, transition timestamps, stop reason, and restart-resumable state.
- Requires fresh routing ownership plus structured verification/review evidence; it fails closed instead of inferring success from an agent's last prose message.
- Exposes autopilot start/pause/resume/stop/status through HTTP, `agentctl.mjs`, and the first-party dashboard.

## Federated registry and heartbeat semantics

The controller remains authoritative only for processes it launches, while `federation` is the normalized view of the real swarm. Local managed workers are ingested automatically. ChatGPT sessions, GitHub/CI work, and heaven2 control state register through the bridge API because automatic ChatGPT project-session enumeration is not available.

Normalized states are `working`, `tool_wait`, `blocked`, `idle`, `done`, `failed`, and `disconnected`. Fresh non-terminal heartbeats are live. Stale or disconnected observations remain visible but do not inflate Active Agents. Completed and failed historical records never count as live.

Identity reconciliation uses stable provider/source identities plus explicit correlation keys. Similar chat titles are display metadata only and never cause sessions to be merged. The same logical worker can be correlated across ChatGPT and GitHub when a shared strong correlation key is supplied.

External ownership also participates in planning: fresh federated Manager/Main/lane observations suppress duplicate workflow lanes, while local execution capacity is still calculated from controller-owned processes only.

Automatic discovery is deliberately honest: `local-control` is automated; ChatGPT registration is bridge-based with discovery unavailable; GitHub/CI and heaven2 use bridge registration.

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
- Autopilot stops at stale ownership, missing structured verification/review evidence, worker-capacity or lease preflight failure, exhausted repair budget, degraded/read-only/emergency state, and the final integration approval boundary.
- The controller does not merge, release, or publish branches automatically.
- Integration queue state is advisory until a reviewer/integration agent and the repository's own verification requirements approve the work.

## ChatGPT plugin

`chatgpt-plugin/` contains a private skills-only plugin designed to let ChatGPT operate this local controller through the already connected **Heaven Local Bridge** app.

That bridge is intentional: ChatGPT cloud cannot directly call `127.0.0.1` on Heaven. The plugin uses the user-authorized Heaven Local Bridge to invoke `agentctl.mjs`, register session heartbeats, start the controller when needed, deploy agents, inspect snapshots, read logs, stop proven-owned workers, and launch reviewers.

See `CONTROL_PLANE.md` for the current v0.5.0 architecture, engineering-autopilot state machine, and remaining remote-worker transport work.
