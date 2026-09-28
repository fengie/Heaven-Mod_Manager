# Agent Control Plane Safety / Ownership Audit

Status: **documentation-only independent support audit**  
Audit branch: `agent/support-agent-control-plane-safety-audit-20260928`  
Canonical `main` inspected: `a83dc6e047ccf98e896f10c25772df99b95426d1`  
Target implementation: PR #47, `feature/agent-control-panel-20260928` at `cc3278f5467735bc2e170ba2910edb1b54508a42`  
Target PR base: `4fd61dd33609a7c55e5aedbaad026266a410f942`  
At audit selection time, PR #47 was **2 commits ahead / 34 commits behind** current canonical `main`.

## Selected support boundary

This audit owns only the **Heaven agent-control-plane process ownership, stop semantics, restart reconciliation, API exposure, and capacity/state safety** boundary.

It deliberately does **not** modify the feature branch, updater implementation, archive extraction, filesystem/CAS safety, WPF product UI, release pipeline, or other active support lanes.

The lane was selected after inspecting current `main`, recent history, open PRs, branches, repository continuity, company agent doctrine, and the existing support portfolio. No other open support PR owned the control plane's process-control safety boundary.

## Methodology

Inspected actual source bodies and call paths in:

- `tools/agent-control/server.mjs`
  - startup/configuration
  - `loadState` / `saveState`
  - `refreshState`
  - `workerSnapshot`
  - lease acquisition
  - `deployOne`
  - `killProcessTree`
  - `stopAgent`
  - integration queue / branch reconciliation
  - HTTP API and `allowedOrigin`
- `tools/agent-control/agentctl.mjs`
- `tools/agent-control/public/index.html`
- `tools/agent-control/README.md`
- `tools/agent-control/CONTROL_PLANE.md`
- `tools/agent-control/chatgpt-plugin/skills/agent-control/SKILL.md`
- `tools/agent-control/package.json`
- `tools/agent-control/Start Agent Control.bat`

Also re-read the repository continuity/read-order documents and the relevant company doctrine, especially `_AGENT_TRAINING/SAFETY_AND_DESTRUCTIVE_OPERATIONS.md`, `MULTI_AGENT_COORDINATION.md`, and `VERIFICATION_DOCTRINE.md`.

Authoritative platform references consulted:

- Microsoft, **Process Handles and Identifiers**: a PID is valid only for the lifetime of that process.
- Microsoft ETW process documentation: process identifier numbers are reused.
- Microsoft, **taskkill**: `/PID` targets the process with that numeric identifier and `/T` also terminates its child processes.
- Node.js `child_process` documentation: child exit/close status is available from the live child object, while kill failures are separately observable.

No runtime reproduction is claimed in this audit.

## Existing strengths

The control plane already has several good safety properties:

1. controller-generated agent branch names are constrained rather than shell-derived;
2. Git execution uses `execFile`, not a shell command string;
3. every deployment gets an isolated worktree and branch;
4. named mutable-boundary leases reject an already-active exact boundary;
5. unknown worker placement fails closed instead of pretending a remote deployment happened;
6. the controller does not automatically merge integration candidates;
7. browser UI text is generally escaped before HTML insertion;
8. the plugin explicitly distinguishes managed agents from merely observed Git branches;
9. the default bind address is loopback;
10. the controller re-fetches the requested base before creating a worktree.

These are worth preserving.

---

# Findings

## ACP-P1-01 — stop can release a mutable-boundary lease even when the process was not terminated

**Severity:** P1  
**Confidence:** confirmed static control-flow defect; not runtime-reproduced

### Evidence

`killProcessTree(pid)` on Windows starts:

`taskkill /PID <pid> /T /F`

but resolves its promise on both the killer's `exit` and `error` events without checking the exit code or proving that the target is gone.

`stopAgent(id)` then unconditionally:

- marks the agent `stopped`;
- sets `finishedAt`;
- releases the agent's mutable-boundary lease with reason `user-stop`;
- updates the task state;
- removes the child from the in-memory map.

There is no post-`taskkill` liveness or identity check.

### Why this matters

A denied/failed `taskkill`, transient OS failure, stale PID, or other failure can leave the original worker alive while the controller advertises it as stopped and makes its mutable boundary available to a new agent.

That violates the control plane's most important coordination invariant: **one managed implementation owner per mutable boundary**.

The dangerous postcondition is not merely an inaccurate status label. It can create two concurrently mutating agents on the same intended boundary.

### Required regression contract

Introduce a narrow termination seam and prove:

1. a failed killer exit leaves the agent non-terminal;
2. the lease remains active while the process is still verified alive;
3. the stop API reports failure rather than success;
4. a successful kill is followed by a bounded wait/probe proving the exact owned process is gone before releasing the lease;
5. only then may task/agent state become `stopped`.

Do not fix this by merely trusting `taskkill` launch success.

---

## ACP-P1-02 — persisted PID is treated as process ownership across controller restart

**Severity:** P1  
**Confidence:** confirmed static design defect + documented Windows process semantics; not runtime-reproduced

### Evidence

Persisted agent state contains a numeric `pid`.

After controller restart, the live `ChildProcess` object no longer exists in `children`. `refreshState()` decides whether an agent is still running using only:

`process.kill(pid, 0)`

and `stopAgent()` later calls `taskkill` against that stored PID.

No durable process identity such as creation/start time, executable identity, or another restart-stable discriminator is persisted and checked before destructive process control.

Microsoft documents that process identifiers are valid for a process lifetime and can be reused after termination.

### Failure mode

1. controller launches Codex PID X and persists X;
2. controller is terminated or crashes;
3. Codex exits;
4. Windows later reuses PID X for an unrelated process;
5. controller restarts and `isPidAlive(X)` reports true;
6. UI can show the stale agent as running;
7. user presses Stop;
8. `taskkill /PID X /T /F` can target the unrelated process tree.

This contradicts the README/architecture claim that stop controls are scoped only to processes launched by the panel.

### Required regression contract

On restart, a persisted PID must be treated as **untrusted identity**, not ownership proof.

Persist and validate a stronger process identity. A practical Windows contract can include at least:

- PID;
- process creation/start timestamp;
- expected executable path;
- expected worktree / command identity where retrievable.

If identity cannot be proven after restart, transition the agent to an explicit `orphaned` / `unknown` state and **do not offer destructive Stop by PID**.

The live in-memory `ChildProcess` handle remains stronger evidence during the same controller lifetime; preserve that distinction.

---

## ACP-P1-03 — dead process after restart can be promoted to `done` merely because a last-message file exists

**Severity:** P1  
**Confidence:** confirmed static control-flow defect; not runtime-reproduced

### Evidence

In `refreshState()`, when a persisted agent is marked running/starting/stopping but its PID is no longer alive:

`agent.status = last ? "done" : (agent.exitCode === 0 ? "done" : "finished");`

After controller restart, the live child exit event that would have recorded the real exit code is unavailable. A non-empty last-message file therefore becomes sufficient to classify the worker as `done`.

`integrationQueue()` includes terminal managed agents and can then present the branch as a candidate based on ahead/behind state.

### Why this matters

A worker may emit useful output and still crash, be killed, hit an account/tool limit, or stop before finishing verification/commit/handoff.

A text artifact is evidence, not a successful process exit.

This can turn interrupted work into a misleading `done` / integration-candidate state.

### Required regression contract

After restart:

- only a durably recorded successful exit result from the owning controller session may restore `done`;
- otherwise a dead previously-running process becomes `interrupted` / `unknown`, not `done`;
- integration candidate state must not imply successful task completion when exact exit status is unknown;
- a last-message file may populate diagnostics, never success state.

---

## ACP-P1-04 — the advertised localhost-only trust boundary can be configured away without authentication

**Severity:** P1 when exposed beyond loopback; default configuration remains loopback-only  
**Confidence:** confirmed static configuration/API behavior; no network exploit reproduced

### Evidence

The server uses:

`const HOST = process.env.AGENT_CONTROL_HOST || "127.0.0.1";`

There is no validation that `HOST` is loopback.

The HTTP API has no bearer token or other authentication.

`allowedOrigin(req)` returns `true` when the request has no `Origin` header. Non-browser HTTP clients commonly omit `Origin`.

The same API can:

- deploy Codex with `--approve-for-me`;
- create Git worktrees/branches;
- fetch repository state;
- read task/log output;
- stop process trees.

### Why this matters

The design documentation says the bridge through Remote Desktop Commander is intentional specifically because ChatGPT cloud cannot call Heaven's `127.0.0.1`. That means v0.2 does not need a network-exposed listener.

If `AGENT_CONTROL_HOST` is accidentally changed to `0.0.0.0`, a LAN/VPN address, or another non-loopback bind, Origin filtering is not an authentication boundary and non-browser clients can invoke privileged control operations.

### Required regression contract

For the current architecture, the safest first boundary is simple:

- reject startup unless the resolved bind host is loopback;
- document that remote access must go through the authorized local bridge.

If a future release intentionally supports remote binding, that must be a separate security boundary with authenticated requests, explicit exposure configuration, host/origin policy, secret handling, and threat-model tests. Do not silently turn the current unauthenticated loopback API into a network API.

---

## ACP-P2-01 — active-agent capacity is checked before an asynchronous gap and is not reserved atomically

**Severity:** P2  
**Confidence:** likely concurrency defect from static ordering; not stress-reproduced

### Evidence

`deployOne()`:

1. loads/refreshed state;
2. counts running agents;
3. rejects if the current count reached `MAX_ACTIVE_AGENTS`;
4. then awaits `resolveBaseRef(base)`, which performs Git fetch/rev-parse work;
5. only later persists the new task/lease and eventually the running agent.

Two concurrent HTTP deploy requests can both observe the same free slot before either reserves capacity.

### Expected consequence

Concurrent requests can exceed configured active capacity, increasing CPU/memory/repository pressure exactly when the controller is intended to constrain it.

### Recommended checkpoint

Add an explicit synchronous reservation state before the first await, or serialize deployments through a controller-level queue/mutex. Capacity accounting should include `reserved/starting` slots.

Stress-test simultaneous POST requests at `capacity - 1` and assert no more than one succeeds.

---

## ACP-P2-02 — state-load corruption fails open to an empty authoritative registry

**Severity:** P2  
**Confidence:** confirmed static recovery behavior; corruption not reproduced

### Evidence

`loadState()` catches any read/JSON failure and returns `defaultState()`.

That default has no agents, tasks, leases, or events.

If active workers continue running while the state file becomes unreadable/corrupt, the controller can forget their leases and accept overlapping deployments.

### Recommended checkpoint

Operational registry corruption should not silently mean "no agents exist."

Prefer:

- durable previous-state backup / replace strategy;
- parse/schema validation;
- explicit controller-degraded mode on unreadable state;
- no new mutation/deployment while ownership state is unknown;
- operator-visible recovery diagnostics.

A recovered empty registry should require evidence that no managed processes remain.

---

## ACP-P2-03 — worktrees, branches, logs, and last-message files accumulate without a lifecycle/reaping policy

**Severity:** P2 operational/resource risk  
**Confidence:** confirmed missing lifecycle behavior

The controller creates a worktree, branch, JSONL log, and last-message file for every deployment. No cleanup/reap operation is implemented.

This is acceptable for a first smoke-test version, but long-lived use will accumulate Git worktrees, disk data, branch clutter, and stale runtime artifacts.

Do not auto-delete evidence blindly. Define retention and integration-state preconditions first.

A safe later lifecycle can retain candidate/failed evidence until reviewed and only reap artifacts after branch disposition is durable.

---

# Test gaps

No dedicated automated tests are present for the control-plane JavaScript in PR #47.

Highest-value deterministic additions:

1. **stop failure / lease retention test** — injected killer failure, process remains alive, lease stays active;
2. **restart PID reuse test** — persisted PID maps to a different process identity; controller refuses destructive stop;
3. **restart unknown-exit test** — non-empty last message + missing authoritative exit status does not become `done`;
4. **loopback binding test** — non-loopback `AGENT_CONTROL_HOST` fails startup in the current unauthenticated design;
5. **concurrent capacity test** — parallel deployments cannot exceed the limit;
6. **corrupt-state test** — unreadable registry enters degraded/fail-closed mode rather than empty authoritative state;
7. **state transition invariant test** — a lease is released only after a terminal process postcondition is proven;
8. **cleanup-retention tests** once a reap lifecycle is designed.

Use temp Git repositories, fake Codex executables/processes, injected process-control seams, and a temporary data/worktree root. Do not require a user's real repository or real Codex account for these tests.

---

# Recommended implementation order

Keep these as independent checkpoints rather than a broad rewrite.

## C1 — process-stop and lease invariant

Fix ACP-P1-01 first.

Acceptance criterion:

> A mutable-boundary lease cannot be released by Stop until the exact owned process has been proved terminated.

This is the narrowest high-value defect and can be tested without redesigning the API.

## C2 — restart-safe process identity and reconciliation

Fix ACP-P1-02 and ACP-P1-03 together because they share the same persisted process-identity / authoritative-exit problem.

Acceptance criterion:

> After controller restart, numeric PID liveness and output files alone can never prove ownership or successful completion.

## C3 — enforce loopback-only v0.2 server

Fix ACP-P1-04 without designing remote auth yet.

Acceptance criterion:

> Current v0.2 refuses to listen on non-loopback interfaces.

## C4 — deployment serialization/capacity reservation

Fix ACP-P2-01 with a reservation or queue.

## C5 — fail-closed state recovery

Fix ACP-P2-02.

## C6 — explicit artifact/worktree retention and reap policy

Address ACP-P2-03 after integration/disposition semantics are stable.

---

# Things deliberately not changed

This support audit does not:

- patch `server.mjs`;
- patch the feature branch;
- change the WPF product;
- change updater code or verification state;
- change repository CI;
- merge or close PR #47;
- kill/restart any controller or Codex process;
- alter Heaven/Heaven2 filesystems;
- change Git remotes or credentials;
- claim Windows runtime reproduction.

No verification cache or historical evidence was promoted or rewritten.

# Verification actually performed

- verified canonical GitHub `main` HEAD twice during task selection/work;
- inspected recent main history;
- inspected all open PRs returned by the connected GitHub repository;
- inspected the repository branch inventory;
- confirmed PR #47 metadata and changed-file list;
- compared PR #47 head against current `main` and recorded **ahead 2 / behind 34** at audit time;
- read the mandatory repository startup/continuity chain and relevant company doctrine;
- inspected actual control-plane source bodies, not filenames alone;
- consulted current Microsoft/Node primary documentation for PID lifetime/reuse, `taskkill`, and child-process exit/error semantics;
- statically traced process stop, restart reconciliation, lease release, bind/origin handling, deployment capacity, and state-recovery paths.

# Not verified

- no Node process was run;
- no `npm run check` was run by this audit;
- no unit/integration test suite was executed;
- no Windows `taskkill` failure was injected;
- no PID-reuse runtime fixture was executed;
- no controller restart fixture was executed;
- no concurrent HTTP capacity stress was executed;
- no corrupt-state recovery test was executed;
- no GitHub Actions gate was run for this audit;
- no claim is made that PR #47 is merge-ready or unsafe in every default deployment.

The implementation PR itself reports its own smoke/syntax evidence; this audit does not inherit or re-label that evidence as independently executed.

# Learned-rule / trainer review

No new project Learned Rule is appended by this audit.

The reusable root lesson is already represented by company doctrine:

- destructive/stateful work must prove ownership/authority before mutation;
- fail-closed postconditions matter;
- interrupted/stale agent state is evidence, not automatically valid work;
- leases/coordination state must remain discoverable and trustworthy.

This audit applies those existing rules to OS process identity and agent-control ownership rather than duplicating them under a new rule number.

# Parallel-work / integration notes

- PR #47 owns the feature implementation. This branch is reviewer/support documentation only.
- PR #47 is based on `4fd61dd...` while audited canonical `main` is `a83dc6e...`; rebase/reconciliation must preserve newer updater/continuity truth rather than replaying stale branch-local state.
- Do not merge stale continuity snapshots from PR #47 over current updater continuity.
- If the feature author fixes these findings on PR #47, this audit should remain as design/test rationale rather than becoming a competing implementation branch.
- The control-plane feature is separate from the product updater boundary and should not inherit product release verification merely because `main` is green.

# Successor handoff

Next independent checkpoint:

> Implement and test **C1 process-stop / lease retention** on the feature branch or a narrowly based repair branch: inject a controllable process terminator, make Stop fail closed when termination is not proven, and release the mutable-boundary lease only after exact owned-process termination is confirmed.

After C1, independently tackle restart-safe process identity/reconciliation.

The successor must re-check canonical `main`, current PR #47 head, and all newer control-plane/support work before editing. Preserve the repository's permanent continuity constitution and active Learned Rules, and explicitly require the successor after you to preserve and recursively propagate them again.

**Do not break the chain.**
