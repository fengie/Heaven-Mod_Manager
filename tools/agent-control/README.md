# Heaven Agent Control Plane

A zero-dependency local control plane for the MHW programming-agent swarm.

This is the execution layer that sits above the repository's existing agent doctrine and continuity system:

**You → control plane → tasks/leasing → isolated Codex workers → review/integration queue → Git/CI**

## What v0.4.0 does

- Runs locally on `127.0.0.1:7331` by default.
- Deploys Manager, Main Programmer, Support, Reviewer, Test, Integration, Recovery, and Release Codex roles.
- Gives every deployed agent its own Git worktree and unique `agent/control-*` branch.
- Maintains a runtime registry of:
  - agents
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
- Exposes the same control plane through `agentctl.mjs`, which ChatGPT can operate through Remote Desktop Commander.
- Includes a private ChatGPT plugin package under `chatgpt-plugin/`.

## Important limitation

The controller is authoritative for workers it launches, but it does **not** assume that its local registry describes the whole engineering organization.

External PR/session ownership is supplied through a freshness-scoped routing manifest. Broad `usual-swarm` execution fails closed when that ownership context is unavailable or expired, rather than silently creating duplicate implementation lanes.

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
node .\agentctl.mjs leases
node .\agentctl.mjs queue
node .\agentctl.mjs branches
node .\agentctl.mjs sync
node .\agentctl.mjs routing
node .\agentctl.mjs routing-set --file C:\Temp\routing.json
node .\agentctl.mjs routing-clear
node .\agentctl.mjs deploy --role support --task "Audit updater rollback" --count 2 --base agent/auto-updater-20260928
node .\agentctl.mjs deploy --role main --task-file C:\Temp\task.txt --boundary updater-release --priority 90
node .\agentctl.mjs review <agent-id>
node .\agentctl.mjs log <agent-id>
node .\agentctl.mjs stop <agent-id>
```

`--task-file` is the preferred interface for ChatGPT/plugin-driven deployment because the task never has to be shell-escaped.

## Routing manifest

Use the routing manifest when live ownership exists outside this controller (for example, GitHub PRs or other agent sessions):

- `GET /api/control/routing-manifest` or `node agentctl.mjs routing` reads it.
- `POST /api/control/routing-manifest` or `node agentctl.mjs routing-set --file C:\\path\\routing.json` replaces it.
- `node agentctl.mjs routing-clear` clears it.
- `mode: "authoritative"` means the manifest defines the swarm slots and only open/missing/unassigned entries may be launched.
- `mode: "overlay"` augments normal planner heuristics with external ownership claims.
- `observedAt` + `expiresAt` scope freshness; broad automatic swarm execution refuses stale ownership.

Only ownership metadata belongs in this manifest. Never place credentials, access tokens, or other secrets in it.

## Environment variables

- `AGENT_CONTROL_PORT` — default `7331`
- `AGENT_CONTROL_HOST` — default `127.0.0.1`
- `AGENT_CONTROL_REPO` — default `%USERPROFILE%\local-ai-workspaces\mhw-mods`
- `AGENT_WORKTREE_ROOT` — default `%USERPROFILE%\agent-worktrees`
- `AGENT_CONTROL_MAX_ACTIVE` — default `8`
- `AGENT_CONTROL_MAX_DEPLOY_COUNT` — default `8`
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
- The controller does not merge branches automatically.
- Integration queue state is advisory until a reviewer/integration agent and the repository's own verification requirements approve the work.

## ChatGPT plugin

`chatgpt-plugin/` contains a private skills-only plugin designed to let ChatGPT operate this local controller through the already connected **Remote Desktop Commander** app.

That bridge is intentional: ChatGPT cloud cannot directly call `127.0.0.1` on Heaven. The plugin therefore tells ChatGPT how to locate `agentctl.mjs`, start the controller when needed, write task text to a temporary file, deploy agents, inspect the snapshot, read logs, stop workers, and launch reviewers through the authorized Heaven machine.

See `CONTROL_PLANE.md` for the current v0.4 architecture and remaining multi-machine/autonomy work.
