## v0.6.15 — current-presence registry retirement

Ordinary terminal managed failures are retired only after recovery/work/process/remote-stop checks are safe; unsafe or durable-work cases remain visible. Federated terminal/disconnect-expired rows move to durable per-source tombstones, including all correlated provider/source identities, so stale replay cannot repopulate the live registry. Product integration: v8.8.37.

# Heaven Agent Control Plane

A zero-dependency local control plane for the MHW programming-agent swarm.

This is the execution layer that sits above the repository's existing agent doctrine and continuity system:

**You → current normal Chat when available → Agent Control → direct non-Work workers as needed → review/integration queue → Git/CI**

ChatGPT Work handoffs are deny-by-default. Agent Control keeps work moving through non-Work execution paths unless the user explicitly requests Work for the current task.

## Machine topology

- **heaven2 = control center.** Agent Manager/dashboard, orchestration, credentials, integration, operator controls, and canonical control-side Git actions live here.
- **heaven1 = resource worker center.** Its existing runtime hostname remains `heaven` for compatibility. Heavy agents, builds, tests, indexing, worktrees, batch jobs, and background execution belong here by default.
- UI text may say **heaven1**, while routing/bridge payloads may still say **`heaven`**. They refer to the same resource-worker machine; do not rename the runtime host merely for display consistency.

## What v0.6.14 does

- Renders backend notifications and exposes buttons only for the supported `inspect-agent` / `inspect-federation` actions.
- Adds stable ID-based managed/federated inspection with a persistent Inspector surface that survives refresh while its target stays live and reports retirement/missing races explicitly.
- Shows authoritative recovery context including provider/machine, lifecycle/recovery, task/boundary/lease, branch/PR, heartbeat/action, error/message, lineage, and valid managed actions.
- Moves keyboard semantics to explicit Inspect buttons while keeping card-body mouse convenience and nested-control isolation.
- Uses bound `data-*` actions instead of embedding arbitrary stable IDs in inline JavaScript handlers.

## What v0.6.13 does

- Canonicalizes startup source before restore, watchdog restart, and manual launch: only clean local `main` may fast-forward to `origin/main`; dirty, detached, non-main, ahead, or diverged checkouts fail closed without reset/clean.
- Publishes the exact runtime repository SHA and Agent Control version through `/api/status` and `controller-process.json`, and refuses an explicitly expected SHA/version mismatch.
- Replaces a stale listener only when persisted PID, exact server path, and Node process ownership all match; unknown listeners remain untouched.
- Keeps the startup/watchdog process path hidden/background and reuses the shared hidden-process wrapper for runtime Git probing.

## What v0.6.12 does

- Makes operator Stop and provider-capacity termination share one persisted fail-closed proof path.
- Resolves Heaven ownership through the canonical executionProvider/runtimeProvider/provider fallback and requires a durable remote job id plus an explicit processed-terminal Bridge state.
- Records local-wrapper exit evidence without finalizing status/task/lease while remote proof is pending; lease release occurs only after remote and local exit proof are both complete.
- Preserves non-Heaven local stop behavior while ambiguous/missing Heaven proof remains blocked with ownership retained.

- Makes `GET /api/status` a lightweight local-state liveness endpoint so watchdog/startup health is independent of repository scans and relay synchronization.
- Reuses one Heaven Bridge assessment per full dashboard snapshot while preserving authoritative dispatch-time bridge checks.

- Fails closed when a Heaven-backed retry-exhausted row lacks a durable remote job id, including runtimeProvider/provider fallback identities, and preserves retirement tombstones across state migration.
- Requires a retry-exhausted Heaven Bridge worker's durable remote job to reach an explicit processed-terminal state before live-registry retirement; ambiguous, queued, `not_running`, `unknown`, cancellation/status authority failure, and unrecognized states fail closed.
- Reasserts cancellation when a durable remote job races into `running`, and accepts retirement proof only for explicit terminal states: `completed`, `done`, `failed`, `error`, `timeout`, or `cancelled`.
- Reactivates a retired federated identity only when the raw live heartbeat is strictly newer than its tombstone `retiredAt`; stale, equal, missing, invalid, and terminal replay stays suppressed before normalization can synthesize freshness.

- Preserves click/Enter/Space inspection while restoring normal article semantics for managed/federated cards; nested action controls remain independent and executable interaction tests cover the accessibility boundary.
- Uses one relay checkout resolver for Heaven Bridge health and real submit/result-wait execution. Without an explicit relay path, the documented `~/HeavenBridgeRepo` checkout is used instead of crashing dispatched workers because `AGENT_CONTROL_HEAVEN_RELAY_DIR` is absent.
- Makes managed and federated agent cards directly inspectable by mouse and keyboard instead of leaving the card body display-only. Managed cards open their log; federated cards focus a linked managed worker or show federated session details.
- Keeps nested card actions independent so clicking **View log**, **Stop**, **Deploy reviewer**, or **Copy branch** does not also trigger the card-level inspector.

- Retires terminal **RETRY EXHAUSTED** no-work agents instead of leaving failed/dead cards in the live registry: Agent Control proves and terminates owned live processes when necessary, preserves uncertain ownership or dirty/diverged work, releases clean worktrees and leases, removes the logical agent from managed/federated live state, and keeps durable task/event tombstones.
- Suppresses repeated terminal observations from retired federated sources so dead sessions cannot immediately resurrect themselves; a genuine live heartbeat clears the tombstone and re-admits the recovered source.

- Fixes **Copy branch** so branch names are encoded before being embedded in the generated operator action, avoiding malformed inline-handler markup.
- Keeps the runtime, root ChatGPT plugin manifest, and nested Codex plugin manifest on one enforced release identity.

- Surfaces backend-ranked manager recommendations directly in the dashboard and turns supported recommendations into explicit recovery/review/swarm/inspection actions.
- Restricts **Deploy reviewer** to completed managed agents and makes emergency-stop recovery an explicit confirmed Resume action.

- Enforces the repository-driven **Agent Manager P0 functionality lock**: while `_AGENT_CONTEXT/CURRENT_REVISION.json` marks it active, implementation prompts and next-cycle planning stay on Agent Control reliability/orchestration/observability/recovery/routing/verification, and the expansion lane runs at priority 100 instead of selecting unrelated product work.
- Keeps the runtime package and private ChatGPT plugin on the same v0.6.10 release identity; stable ChatGPT sessions register/heartbeat by default when the runtime exposes a real stable identity, while undiscoverable sessions remain explicitly partial coverage.

- Makes dashboard lifecycle controls match the server's authoritative active-state model: reserved, starting, running, waiting, blocked, stale, and stopping managed workers can all be stopped from the UI. Polling is serialized and sequence-checked so slow older snapshots cannot overwrite newer operator state, and periodic refreshes preserve the selected worker target instead of silently resetting it to Auto.

- Makes **START SWARM** lifecycle-aware and truthful: a paused perpetual run is resumed and advanced immediately, an already-running perpetual run remains idempotent, an active non-perpetual autopilot returns an explicit conflict, and an invalid empty objective is rejected before pause/read-only/drain/emergency-stop state is normalized.

- Runs locally on `127.0.0.1:7331` on `heaven2` by default. Normal startup refuses other hosts; `AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST=1` exists only for isolated tests or explicit recovery.
- Makes startup a single normal action: open the dashboard and press **START SWARM**. That explicit operator action switches the controller to `coordinate`, clears pause/read-only/drain/emergency-stop friction, and launches/fills the usual 1 Manager + 1 Primary Programmer + 4 Support topology while preserving capacity, live-ownership, lease, machine, and degraded-state checks. Raw routing/autonomy controls remain available only under Advanced / diagnostics.
- Coordinates Manager, Main Programmer, Support, Reviewer, Test, Integration, Recovery, and Release roles. Normal Chat is preferred when already available, but local dispatch defaults to a direct non-Work worker path so execution does not stop merely because arbitrary ChatGPT conversations cannot be auto-created.
- Agent Control never initiates ChatGPT Work mode. Work is an external, explicit current-task opt-in; ordinary dispatch, review, retry, recovery, and autopilot flows remain non-Work.
- Enforces a mandatory pre-response repository-training gate: every spawned worker must have all required training/continuity sources present and non-empty, receives their exact hashes in a training manifest, and is instructed to read them plus task-relevant source/tests before it may process the task prompt.
- Gives every deployed agent its own Git worktree; it reuses a safe compatible unowned branch when available and creates a new `agent/control-*` branch only when no strong safe match exists.
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
- Keeps a continuously armed multi-worker recovery pool (4 slots by default) during swarm execution. Failed, interrupted, or orphaned lanes are claimed immediately while healthy workers continue; each failed lineage has a single active recovery owner, replacement workers inherit the preserved branch/worktree/task evidence, and the pool remains bounded by configured recovery capacity and retry limits.
- Lets you stop a managed worker.
- Lets you launch a reviewer against a completed agent branch with one click.
- Exposes the same control plane through `agentctl.mjs`, which ChatGPT can operate through Heaven Local Bridge.
- Includes a private ChatGPT plugin package under `chatgpt-plugin/`.
- Adds a durable engineering-autopilot state machine for a user-supplied big direction. Bounded mode remains sync/plan → implement → verify → review → bounded repair/reverify → integration-ready → continuity.
- The normal one-click startup now launches **Perpetual Cycle**: reconcile canonical truth → stabilize/implement → verify → review → bounded repair/reverify → integrate the approved exact candidate → PR/branch/issue hygiene → next-cycle expansion/continuity → reset per-cycle ownership → repeat.
- Perpetual Cycle now has progress-based self-healing: a worker that remains alive but produces no recorded progress past the stale threshold is takeover-preserved, controller-owned termination is proven, and a one-for-one direct replacement inherits the preserved branch/worktree/task context. Replacement intent is durable, so a failed dispatch retries the exact lane instead of forgetting it.
- Recoverable perpetual failures no longer disable the standing run. Provider-capacity, temporary worker-capacity/routing failures, missing verification/review evidence, exhausted bounded phase retries, and runtime errors enter a durable retry/backoff path; repeated recoveries escalate cooldown instead of creating a restart storm. Read-only, drain/pause, emergency-stop, degraded state, and changed autonomy remain operator/safety holds rather than being silently overridden.
- Agent Control is supervised from outside the Node process by **Heaven Agent Control Watchdog**. The watchdog uses a single-instance lock, localhost health checks, controller-written PID identity, ownership-verified hung-process termination, durable restart history, exponential cooldown, Task Scheduler restart-on-failure, and a Startup-folder fallback.
- Persists autopilot phase, transition/repair/retry budgets, perpetual cycle number, exact candidate/worker IDs, canonical-main observation, transition timestamps, stop reason, and restart-resumable state.
- Engineering autopilot requires structured verification/review evidence and current ownership truth; perpetual mode renews an expired routing-freshness lease from current registered controller/federated state without reviving stale assignments.
- Exposes autopilot start/pause/resume/stop/status through HTTP, `agentctl.mjs`, and the first-party dashboard.
- Gives operators truthful lifecycle counts (working, waiting, blocked, idle, stale, disconnected), provider failure details, lease/boundary provenance, integration readiness, and recent controller events.
- Exposes existing server-authorized control operations in both dashboard and CLI: autonomy changes, routing set/clear, pause/resume, read-only mode, drain, emergency stop, owned-agent stop, and swarm stop. The UI remains a client; server-side authorization and ownership checks remain authoritative.
- Classifies Codex usage/quota exhaustion as `capacity-blocked`, preserves the unfinished task/branch as blocked work, and opens a dispatch circuit until the provider reset window expires (or a later successful worker proves recovery). Capacity-blocked work is not eligible for automatic reviewer/takeover replacement on the same provider. Direct Heaven Bridge `proc_run` build/test/filesystem/process/computer-control work remains available during the cooldown.
- Reconciles response/transport stream loss against durable execution evidence before retrying: `STREAM LOST · CHECKING WORK` resolves to `WORK DETECTED · INCOMPLETE`, `WORK VERIFIED · COMPLETE`, or `NO DURABLE WORK DETECTED · RETRY`. Commits, changed files, artifacts, PRs, and verification evidence prevent destructive full-task retries; evidence-free interrupted work enters the bounded replacement loop only after the durable-evidence scan is authoritative. Unknown worktree/branch evidence fails closed as unverified instead of spawning a duplicate.

## Federated registry and heartbeat semantics

The controller remains authoritative only for processes it launches, while `federation` is the normalized view of the real swarm. Local managed workers are ingested automatically. ChatGPT sessions, GitHub/CI work, and heaven2 control state register through the bridge API because automatic ChatGPT project-session enumeration is not available.

Normalized states are `working`, `tool_wait`, `blocked`, `idle`, `done`, `failed`, and `disconnected`. Fresh non-terminal heartbeats are live. Stale or disconnected observations remain visible but do not inflate Active Agents. Completed and failed historical records never count as live.

Identity reconciliation uses stable provider/source identities plus explicit, namespaced strong correlation keys. Similar chat titles, tasks, branches, PR numbers, roles, and machine labels are metadata only and never become logical identity keys. The same logical worker can be correlated across ChatGPT and GitHub only when explicit strong evidence such as a logical-agent, session, workflow-run, bridge-job, or work-item key is supplied.

Federation schema v2 adds an AgentSource-style adapter boundary for normalize / ingest / reconcile / heartbeat / supported discovery, preserves runtime/session/conversation identity plus opaque source metadata, rejects unknown or unsupported providers at ingestion, rejects ambiguous multi-agent correlation, and ignores late observations that would regress a newer provider/source heartbeat. Persisted v1 registry data migrates non-destructively; legacy providers without an installed adapter remain inspectable as `unsupported` but cannot emit new observations.

External ownership also participates in planning: fresh federated Manager/Main/lane observations suppress duplicate workflow lanes. Exact task IDs are rejected when already owned by a live local or federated agent, while local execution capacity is calculated only from fresh controller-owned workers.

Operator topology is strict: Agent Control, its browser dashboard, CLI interaction, autonomy controls, and other human-facing control surfaces live on `heaven2` as the control center. `heaven1` is the resource worker center; its runtime host identifier remains `heaven` for bridge/routing compatibility. The multi-host bridge uses explicit `target_host` routing; controller/plugin calls target `heaven2`, while heavy execution placement can target `heaven`.

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
node .\agentctl.mjs autopilot-start --task "Continuously improve the project" --perpetual --max-cycles 0 --max-phase-retries 2
node .\agentctl.mjs autopilot-pause
node .\agentctl.mjs autopilot-resume
node .\agentctl.mjs autopilot-stop
# Default deploy/review stays on a direct non-Work execution path. ChatGPT Work is never an automatic handoff.
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
- `observedAt` + `expiresAt` scope freshness; broad automatic/background swarm execution refuses stale ownership. A deliberate dashboard **START SWARM** click is an explicit operator start and can plan from current managed/federated live ownership without forcing the operator to hand-author a routing manifest first.

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
- `AGENT_CONTROL_CONTROLLER_HOST` — default `heaven2`; normal controller startup requires this hostname
- `AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST` — default unset; set to `1` only for isolated tests or an explicit recovery override
- `AGENT_CONTROL_REPO` — default `%USERPROFILE%\local-ai-workspaces\mhw-mods`
- `AGENT_WORKTREE_ROOT` — default `%USERPROFILE%\agent-worktrees`
- `AGENT_CONTROL_MAX_ACTIVE` — default `8`
- `AGENT_CONTROL_MAX_DEPLOY_COUNT` — default `8`
- `AGENT_CONTROL_AUTOPILOT_TICK_MS` — autopilot control-loop cadence; default `4000` ms, minimum `1000`
- `AGENT_CONTROL_PERPETUAL_RECOVERY_WINDOW_MS` — restart-intensity window for perpetual recovery; default 10 minutes
- `AGENT_CONTROL_PERPETUAL_MAX_RECOVERIES_PER_WINDOW` — recoveries allowed in that window before escalating cooldown; default `6`
- `AGENT_CONTROL_PERPETUAL_RETRY_BASE_MS` — base automatic retry delay; default 15 seconds
- `AGENT_CONTROL_PERPETUAL_RETRY_MAX_MS` — maximum exponential retry delay; default 15 minutes
- `AGENT_CONTROL_HEAVEN_RELAY_DIR` ? optional override for the dedicated local relay checkout used to exchange authenticated Heaven Bridge heartbeat/jobs/results on the `heaven-bridge` branch; when unset, Agent Control auto-discovers the documented `%USERPROFILE%\HeavenBridgeRepo` checkout if it exists
- `AGENT_CONTROL_HEAVEN_RELAY_REPOSITORY` — expected private relay repository; defaults to `fengie/mhw-mods`
- `AGENT_CONTROL_HEAVEN_HEARTBEAT_MAX_MS` — maximum accepted Heaven Bridge heartbeat age
- `AGENT_CONTROL_HEAVEN_REPO_URL` — optional repository URL used by remote Heaven workspace preparation
- `CODEX_EXE` — optional explicit path to `codex.exe` for the direct local non-Work worker path; otherwise the newest ChatGPT Codex install is discovered automatically when a local worker is needed.

## Safety / isolation

- The server binds to localhost by default.
- Browser requests from unrelated origins are rejected.
- It never checks out or force-updates `main`.
- Each deployment creates a separate worktree, but it no longer blindly creates a new branch. Agent Control fetches/prunes and inventories existing branches first, reuses a strong deterministic compatible unowned branch when safe, and creates a new `agent/control-*` branch only when no safe match exists.
- Branch decisions are recorded on tasks/agents as reused vs created with candidate/rejection evidence. Active leases/agents and branches checked out by another worktree are never auto-reused.
- A managed task is not considered fully complete until its branch tip is proven reachable from `origin/main`, its controller-owned worktree/local branch cleanup is verified, and the recurring Branch Lifecycle Enforcer has removed any remaining proven-safe remote branch. Cleanup failure becomes `cleanup-required`; unique/unmerged work is preserved.
- A named mutable-boundary lease prevents two managed agents from silently owning the same implementation surface.
- Stopping a worker targets only the PID launched by this controller.
- Operator stop intent dominates a zero exit code: a stopped worker remains `stopped` and cannot become an integration candidate.
- Authoritative child-exit handling collects asynchronous Git evidence before loading and mutating the registry, preventing a stale whole-state snapshot from overwriting newer control-plane changes.
- Counted deploy requests preflight the whole batch against available capacity, so a near-capacity request is rejected before any partial worker launch.
- Pre-launch setup failures converge reserved tasks to failed, release their lease only because no process was launched, and explicitly retain any created worktree/branch for evidence-safe cleanup.
- Broad `usual-swarm` execution requires current reconciled ownership context.
- An authoritative routing manifest fills only manager-declared open slots; claimed external ownership counts as occupied, while stale/superseded claims do not block a lane forever.
- Autonomy permissions are checked server-side before workflow execution, direct deployment, review dispatch, persisted takeover/evidence mutation, integration verdict mutation, and self-improvement execution.
- Engineering autopilot is control-authority-bound to `heaven2`; it rechecks remote `main` with `git ls-remote` before advancing.
- On `heaven2`, `auto` placement prefers `heaven`. Dispatch fails closed if the dedicated relay checkout or authenticated worker heartbeat cannot be proven healthy; it does not silently fall back to heavy execution on `heaven2`.
- Bounded autopilot still stops at its final integration approval boundary. **Perpetual Cycle** crosses that boundary only for an independently approved candidate and then requires refreshed remote-main ancestry proof for that exact candidate tip before the cycle can proceed.
- If the routing freshness lease expires, perpetual mode creates a short-lived `overlay` lease from freshly reconciled controller/federated state with **no inherited assignments**, so stale manager claims are not resurrected. Registered live ownership still suppresses duplicate lanes.
- Perpetual mode ignores the ordinary transition-count budget; `maxCycles=0` means unlimited. Recoverable execution failures are retried with persisted backoff instead of disabling the run. Genuine operator/safety controls remain authoritative: read-only, drain/pause, emergency stop, degraded state, changed autonomy, and ownership that cannot be proven suspend or gate mutation rather than being bypassed.
- Repository hygiene never deletes unique or ambiguous work merely to reduce counts. PRs/issues are closed and branches are removed only with evidence that the work is integrated, completed, obsolete, or otherwise safe to retire.
- Stopping a bridge-backed worker first requires an authoritative cancellation result for the owned remote job before terminating the local runner process or releasing its lease.
- Release publication remains separately governed by repository release policy; Perpetual Cycle does not weaken release gates.

## ChatGPT plugin

`chatgpt-plugin/` contains a private skills-only plugin designed to let ChatGPT operate this local controller through the already connected **Heaven Local Bridge** app.

That bridge is intentional: ChatGPT cloud cannot directly call `127.0.0.1` on Heaven. The plugin uses the user-authorized Heaven Local Bridge to invoke `agentctl.mjs`, register session heartbeats, start the controller when needed, deploy agents, inspect snapshots, read logs, stop proven-owned workers, and launch reviewers.

See `CONTROL_PLANE.md` for the federation/liveness architecture, authenticated Heaven Bridge execution transport, heartbeat semantics, and engineering-autopilot state machine.


### Adaptive Work handoff signatures

The controller does not assume ChatGPT's handoff card labels are permanent. It reads the accessible UI tree, uses the built-in signature registry for known labels, and can safely learn renamed labels only after a unique non-Work action is identified and the Work action is verified gone. Learned aliases and ambiguous drift observations are persisted under the gitignored Agent Control `data/` directory and are available through `GET /api/work-handoff-signatures`. This updates plugin behavior without self-editing tracked source or dirtying `main`.


### Durable failure ledger and swarm startup guard

Agent Control now writes a structured local failure ledger to `data/failures.jsonl`. Each record uses the `agent-control/failure/v1` schema and keeps only bounded diagnostic fields such as agent/task/workflow identity, provider/mode, terminal status, exit/signal data, sanitized error text, and the last useful output. Common bearer/token/secret/password patterns are redacted before persistence; prompt bodies, task capabilities, and environment secrets are not copied into the ledger. The newest records are exposed as `recentFailures` in `/api/snapshot` and through `GET /api/failures?limit=N`.

Multi-step workflow launch is no longer a blind burst. After each worker is started, the controller briefly polls authoritative state and output before launching the next lane. If the worker immediately exposes a provider-capacity error or another terminal startup failure, the startup guard stops the remaining fan-out, records a workflow-level failure entry, and leaves the unfinished lanes visible instead of launching a whole doomed swarm against the same shared failure.
