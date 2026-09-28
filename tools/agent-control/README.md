# Heaven Agent Control Plane

A zero-dependency local control plane for the MHW programming-agent swarm.

This is the execution layer that sits above the repository's existing agent doctrine and continuity system:

**You → control plane → tasks/leasing → isolated Codex workers → review/integration queue → Git/CI**

## What v0.4 does

- Starts useful with **no objective entered**: current mission, prioritized next actions, swarm, task graph, integration queue, machines, release gate, notifications, and recent activity are derived from real state.
- Provides one-click governed workflows including Usual Swarm, Support Current Programmer, Review, Integration, Release, Bug Hunt, UI/UX, Updater Hardening, Verification, Research, Continuity, Cleanup, and Self-Improve.
- Supports the default organization of **1 Manager + 1 Primary Programmer + 4 distinct Support lanes**, filling only missing useful roles.
- Provides a state-aware command bar that resolves common operator language into an explicit preview before dispatch. Unknown commands are staged as bounded custom work rather than silently executed.
- Stores agents, tasks, dependencies, exact base/current SHAs, leases, heartbeat/activity state, prompt provenance, evidence, notifications, improvement proposals, and an explainable event trail.
- Uses versioned centralized role/task prompt templates and persists the exact prompt hash and rendered prompt received by each worker.
- Treats restart uncertainty explicitly: an old PID is never trusted as process ownership, interrupted work is not promoted to success, and takeover state can be generated/persisted automatically.
- Maintains an explicit review/integration queue and evidence-only release gate; it does not silently merge or publish.
- Provides autonomy profiles, pause, drain, read-only, preserve-and-stop, and Emergency Stop controls.
- Exposes global search across control-plane tasks/agents/events/improvements plus repository commits and continuity text.
- Captures Agent Manager feature requests as governed improvement proposals that can enter the same isolated implementation/review pipeline.
- Runs locally and independently of ChatGPT. `agentctl.mjs` exposes the same day-to-day operations for scripting or alternate clients.
- Applies machine policy: Heaven is background/read-only for repository work unless the exact task records explicit authorization; Heaven2 is the primary development machine but should be used only when justified.
- Backs up runtime state and fails closed into degraded read-only mode when ownership state cannot be recovered.
- Refuses unauthenticated non-loopback binding.

## Important limitation

The dashboard is authoritative for workers launched through this control plane.

It can also discover relevant Git branches created by other agents, but ChatGPT does **not** expose a general API that lets this app enumerate every unrelated active ChatGPT web conversation. Those appear as external/observed branches unless they are launched or registered through this control plane.

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
node .\agentctl.mjs snapshot
node .\agentctl.mjs recommend
node .\agentctl.mjs workflows
node .\agentctl.mjs workflow usual-swarm
node .\agentctl.mjs workflow usual-swarm --execute
node .\agentctl.mjs command check everyone
node .\agentctl.mjs command "support whoever is working on updater" --execute
node .\agentctl.mjs release
node .\agentctl.mjs search "rollback problem"
node .\agentctl.mjs improve --task "Add a button that deploys 3 security reviewers"
node .\agentctl.mjs improve-start <proposal-id>
node .\agentctl.mjs prompt <agent-id>
node .\agentctl.mjs takeover <agent-id>
node .\agentctl.mjs control --autonomy coordinate
node .\agentctl.mjs drain
node .\agentctl.mjs stop-swarm
node .\agentctl.mjs emergency-stop
node .\agentctl.mjs deploy --role support --task "Audit updater rollback" --boundary updater-review
node .\agentctl.mjs review <agent-id>
node .\agentctl.mjs log <agent-id>
node .\agentctl.mjs stop <agent-id>
```

Workflow and natural-language commands are **preview-only by default**. Use `--execute` only after inspecting the resolved plan. Unknown natural-language commands do not auto-dispatch; use explicit bounded `deploy` when no governed preset matches.

`--task-file` is the preferred interface for ChatGPT/plugin-driven deployment because the task never has to be shell-escaped.

## Environment variables

- `AGENT_CONTROL_PORT` — default `7331`
- `AGENT_CONTROL_HOST` — default `127.0.0.1`
- `AGENT_CONTROL_REPO` — default `%USERPROFILE%\local-ai-workspaces\mhw-mods`
- `AGENT_WORKTREE_ROOT` — default `%USERPROFILE%\agent-worktrees`
- `AGENT_CONTROL_MAX_ACTIVE` — default `8`
- `AGENT_CONTROL_MAX_DEPLOY_COUNT` — default `8`
- `CODEX_EXE` — optional explicit path to `codex.exe`; otherwise the newest ChatGPT Codex install is discovered automatically.

## Safety / isolation

- The server is **loopback-only** and refuses startup on a non-loopback bind until a separate authenticated remote boundary exists.
- GitHub/canonical state is treated as engineering truth; runtime state never silently rewrites `main`.
- Each repository-writing managed deployment gets an isolated worktree/branch and a mutable-boundary lease.
- Capacity reservation and deploy creation are serialized to prevent concurrent oversubscription.
- Leases have heartbeat/expiry metadata. Stale, blocked, waiting, interrupted, orphaned, failed, and completed states remain distinct.
- A PID persisted across a controller restart is not considered ownership proof. Uncertain workers become interrupted/orphaned and are preserved for reconciliation.
- Stop/Emergency Stop only terminate processes owned by the current controller session; lease release occurs only after termination is proven.
- Runtime state is written with backup recovery. Unrecoverable corruption enters degraded read-only mode and blocks dispatch.
- Heaven repository mutation fails closed unless the exact task carries explicit authorization. The dashboard disables remote-sync UI when the current host policy forbids it.
- Completed work enters review/integration state. The controller never equates agent completion with permission to merge or publish.
- Release readiness is evidence-based and reports unknown checks rather than fabricating green status.

## Deliberately not claimed yet

v0.4 does **not** claim an authenticated phone/LAN control surface, a true remote worker daemon for cross-machine dispatch, token/cost accounting, model-provider routing, automatic merge/release publication, learned-workflow auto-installation, or transactional Agent Manager self-update/rollback. The current self-improvement path produces governed engineering work; self-installation remains a separate safety boundary.

## ChatGPT plugin

`chatgpt-plugin/` contains a private skills-only plugin designed to let ChatGPT operate this local controller through the already connected **Remote Desktop Commander** app.

That bridge is intentional: ChatGPT cloud cannot directly call `127.0.0.1` on Heaven. The plugin therefore tells ChatGPT how to locate `agentctl.mjs`, start the controller when needed, write task text to a temporary file, deploy agents, inspect the snapshot, read logs, stop workers, and launch reviewers through the authorized Heaven machine.

See `CONTROL_PLANE.md` for the architecture and the path from this v1 to the full Level-4 scheduler.
