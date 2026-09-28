# Agent Control v2 Product / Workflow Audit — 2026-09-28

## Status

Documentation-only Support 2 audit for the live swarm board.

- Canonical repository: `fengie/mhw-mods`
- Initial canonical `main` inspected: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`
- Final canonical `main` recheck: `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`
- Review target: PR #59, `feature/agent-control-plane-v2-20260928`
- Exact reviewed PR head: `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5`
- Support lane: product/workflow acceptance, not the independent process/lease safety lane
- Production code changed by this audit: none
- Runtime verification claimed by this audit: none

PR #59 is still a draft implementation lane. Support 1 separately owns process ownership, PID/restart, stop/lease, state-race, degraded-recovery, and loopback-binding safety. This audit deliberately does not duplicate those findings.

## Assignment

Evaluate Agent Control v2 against the product goal represented by the live routing board and PR #59:

1. objective can be optional when repository/runtime context already identifies useful work;
2. strong premade role and workflow prompts exist and preserve repository doctrine;
3. the system prevents duplicate ownership rather than merely showing branches;
4. self-improvement, recovery, review, integration, verification, and release flows form usable end-to-end workflows;
5. autonomy levels actually constrain what the controller may do;
6. the latest machine policy is executable: `heaven2` is control/credential authority, while `heaven` is the preferred heavy execution machine;
7. UI, CLI, and ChatGPT integration expose the v2 control plane rather than only the old direct-deploy surface;
8. all conclusions remain exact-source and evidence-scoped.

## Methodology

Static source inspection of the exact PR head covered:

- `tools/agent-control/lib/control-core.mjs`
- `tools/agent-control/lib/prompt-templates.mjs`
- `tools/agent-control/server.mjs`
- `tools/agent-control/agentctl.mjs`
- `tools/agent-control/public/index.html`
- `tools/agent-control/README.md`
- `tools/agent-control/CONTROL_PLANE.md`
- `tools/agent-control/chatgpt-plugin/skills/agent-control/SKILL.md`
- `tools/agent-control/test/control-core.test.mjs`
- `tools/agent-control/test/server-safety.test.mjs`

Repository coordination inspection covered current `main`, open PRs, branches, issue #60, issue #56, and the PR #59 discussion. Current repository continuity/training/read-order documents were also rechecked before writing this audit.

No conclusion below is based only on filenames or test names.

## Existing strengths

The branch contains substantial real control-plane structure rather than only a dashboard mock-up.

- `ROLE_TEMPLATES` provide distinct Manager, Primary Programmer, Support, Research, Reviewer, Verification, Test, Integration, Release, Recovery, Documentation, and Cleanup missions.
- `renderAgentPrompt()` binds a task to exact task ID, priority, mutable boundary, base branch/SHA, working branch, machine, dependencies, acceptance criteria, verification obligations, repository protocol, and a prompt SHA-256.
- `WORKFLOW_PRESETS` and `planWorkflow()` provide useful workflow vocabulary: usual swarm, support-current, integration, review, release, bug-hunt, UI/UX, updater hardening, verification, research, continuity, cleanup, and self-improvement.
- `deriveMission()` permits an omitted workflow objective to fall back to current managed work or repository `nextMilestone`.
- Recovery/takeover state, evidence records, review verdicts, release-readiness checks, recommendations, state migration/backups, read-only/degraded mode, pause/drain/emergency-stop APIs, and integration eligibility all exist as concrete server concepts.
- The server refuses to pretend a disconnected machine is a real worker.
- The release gate is explicitly evidence-only and does not equate a green source build with publication or rollback proof.

These are useful foundations. The gaps below are mostly failures to connect these foundations into the intended product behavior.

---

# Findings

## P1 — Workflow planning is blind to external/live repository ownership

### Confirmed behavior

`planWorkflow()` determines whether Manager/Main/Support roles are missing only from `state.agents` via `activeAgents()` and `activeLaneSet()`.

`previewWorkflow()` passes only managed state, mission, base branch, and requested machine into `planWorkflow()`.

The server separately computes `observedBranches()`, open branch divergence, integration candidates, repository state, and current Git metadata for the snapshot, but none of that ownership evidence participates in workflow planning.

The repository's actual live swarm demonstrates why this matters: PR #59 already owns the Main Programmer lane, PR #55 and PR #58 own other lanes, and issue #60 contains explicit role claims. A fresh/recovered controller with an empty local registry can nevertheless conclude that no Main Programmer exists and create a new one when asked for the usual swarm.

### Why this matters

The stated product purpose includes decomposing work, preventing duplicate ownership, and managing a live engineering organization. A controller that only prevents duplication among agents it personally launched cannot safely act as the coordinator for the repository's mixed environment of ChatGPT sessions, GitHub branches/PRs, manually launched agents, and controller-managed workers.

This is not just an observability limitation. The current planner can make the wrong dispatch decision from state it already knows is incomplete.

### Required acceptance contract

Before automatic workflow execution, ownership planning should reconcile at least:

- managed active agents;
- active mutable-boundary leases;
- live/open PR ownership;
- repository branches with clear active work;
- the current routing board / durable ownership record when one exists;
- explicit dependencies and supersession.

External work should default to **occupied / review-needed**, not **missing**, unless there is affirmative evidence that the lane is stale or abandoned.

An override may exist, but duplicate implementation ownership should require an explicit operator decision.

### Regression tests

Add deterministic planner tests such as:

1. empty managed registry + repository context declaring an active Main Programmer -> `usual-swarm` must not create another Main Programmer;
2. external support lane claim -> support fan-out must choose a different lane;
3. stale/superseded external branch -> planner may recommend review/recovery, not silently count it as active forever;
4. repository ownership context unavailable -> automatic broad swarm dispatch fails closed or requires explicit confirmation.

---

## P1 — The required heaven2-control / heaven-execution split is not implementable by the current runtime

### Confirmed behavior

The latest routing requirement on PR #59 says:

- `heaven2` is the main/control machine and credential authority;
- `heaven` is the default execution machine for agents, local swarms, model/inference workloads, builds, tests, scans, indexing, batch jobs, and other heavy work;
- the controller should prefer `heaven` for heavy execution;
- it must not silently fall back to heavy execution on `heaven2`;
- credentials should be narrowly brokered/inherited from `heaven2`, not duplicated as plaintext.

The reviewed implementation does not currently provide that architecture:

- `workerSnapshot()` marks configured non-local machines as `not-connected`.
- `assertWorkerPlacement()` maps `auto` to the controller's own hostname and rejects any target whose hostname differs from the controller.
- `CONTROL_PLANE.md` explicitly describes the Heaven/Heaven2 worker daemon as a future layer.
- `deployOne()` always performs local Git/worktree setup before spawning Codex.
- fresh default machine policy marks `heaven.repositoryWriteAllowed = false`.
- the README and ChatGPT plugin describe the controller as Heaven-hosted.
- the UI, CLI, and ChatGPT skill do not expose `repositoryWriteAuthorized`, although the raw API accepts it.

On a fresh controller actually running on `heaven`, normal UI/CLI/plugin deployment therefore reaches the local `heaven` machine policy and can be blocked because every deployed coding worker requires repository worktree creation. Running the controller on `heaven2` does not solve the stated requirement because the current server refuses remote placement on `heaven`.

### Why this matters

This is a product-blocking mismatch between the requested topology and the implementation topology. It also creates policy ambiguity: the runtime simultaneously describes Heaven as the controller host, disallows repository writes there by default, and requires repository mutation to create every worker worktree.

### Required acceptance contract

Implement one explicit topology and make all first-party surfaces agree with it. For the current user requirement:

1. controller/orchestration and credential authority reside on `heaven2`;
2. an authenticated worker provider/daemon on `heaven` heartbeats capability/capacity;
3. heavy workflow placement prefers `heaven`;
4. if `heaven` is unavailable, heavy execution fails closed unless explicit recovery/override allows `heaven2`;
5. repository mutation on `heaven` is limited to controller-owned isolated worktrees or another explicitly defined safe worker checkout contract, never the canonical development checkout;
6. credentials are brokered narrowly and never serialized into controller state, prompts, logs, or repository files;
7. UI/CLI/plugin expose the same placement and authorization semantics.

### Regression tests

- controller on `heaven2`, connected `heaven` worker -> heavy workflow is placed on `heaven`;
- `heaven` unavailable -> heavy workflow is blocked, with no silent `heaven2` fallback;
- explicitly `heaven2`-required Windows/UI task -> placement allowed there;
- worker loses heartbeat before dispatch -> fail closed;
- worker has no repository-write capability for requested task -> fail closed before lease/worktree mutation;
- plugin/CLI/UI all produce the same placement decision.

---

## P1 — Autonomy profiles are descriptive metadata, not authorization

### Confirmed behavior

`AUTONOMY_PROFILES` declare materially different permissions:

- Observe: no automatic actions;
- Assist: recommend/preview;
- Coordinate: support/review/test dispatch;
- Engineering Autopilot: adds integration preparation and continuity maintenance.

The runtime stores `settings.autonomyLevel` and exposes profiles through APIs.

However:

- `assertMutationsAllowed()` checks degraded state, read-only, emergency stop, dispatch pause, and drain state;
- it does **not** inspect `autonomyLevel` or the profile permission list;
- `executeWorkflow()` and direct `POST /api/deploy` do not authorize requested actions against the selected profile;
- no other reviewed code consumes the declared permission strings.

Therefore setting the controller to `observe` or `assist` does not, by itself, enforce the advertised dispatch restrictions.

### Why this matters

Autonomy is a safety and trust contract, not a display preference. An operator can reasonably interpret “Observe” as meaning no automatic dispatch is possible. The current implementation does not make that statement true at the server boundary.

### Required acceptance contract

Introduce one central server-side authorization function for every meaningful control-plane action. It should map actions to profile permissions and be called by all mutation/dispatch endpoints, not only UI code.

At minimum, tests should prove:

- Observe: status/search/snapshot only; no workflow execution, deploy, review dispatch, integration preparation, continuity mutation, or self-improvement start.
- Assist: preview/recommendations allowed; dispatch denied.
- Coordinate: only the documented bounded dispatch/review/test actions allowed.
- Engineering Autopilot: only its declared additional actions allowed; release publication and other governed actions remain separately gated.

Direct API calls must not bypass the profile.

---

## P1 — “Usual swarm” cannot reliably mean the repository's fixed operating model

### Confirmed behavior

The current live repository routing board fixes the operating model at exactly one Manager + one Main Programmer + four Support agents with specific lanes and an integration order.

The preset description says it fills missing useful roles toward that topology, but generic `supportLanesFor()` chooses lanes from objective text rather than consuming the current routing board's assigned roles/boundaries. The planner also has no manager-issued lane manifest input.

For the current Agent Control mission, generic support lanes become architecture/tests/adversarial/continuity, not the live board's exact Support 1–4 assignments (safety verification, product/workflow audit, frontend finalization, updater publication closure).

### Why this matters

Premade workflows are valuable only if they coordinate the current organization rather than create a second organization from generic heuristics.

### Required acceptance contract

Allow a durable/live routing manifest to override generic workflow heuristics. The usual-swarm preset should fill **the declared missing slots**, not recompute support roles from keywords when a manager routing plan exists.

The repository issue/manifest can remain external truth; the controller only needs a trusted adapter and freshness/provenance checks.

---

## P2 — The advertised self-improvement pipeline stops after implementation dispatch

### Confirmed behavior

The self-improvement preset promises:

`proposal -> isolated implementation -> test -> review -> upgrade-candidate`.

The server can:

- create an improvement proposal;
- start the proposal by executing the `self-improve` workflow;
- store `implementationTaskId`;
- set proposal status to `implementation-active` or `blocked`.

But on the reviewed head:

- `upgradeCandidate` is initialized to `null` and has no completion/update path;
- agent completion does not advance an improvement proposal into verification/review;
- there is no reviewed server workflow that converts a green reviewed implementation into an upgrade candidate;
- no UI/CLI/plugin surface exposes proposal lifecycle management.

### Why this matters

Self-improvement is one of the stated differentiating product goals. At present the implementation provides “launch an agent to improve the manager,” not the governed closed-loop pipeline its prompt claims.

### Required acceptance contract

Use an explicit state machine, for example:

`proposed -> implementation-active -> verification-required -> review-required -> upgrade-candidate -> approved/integrated`

with failure/recovery states.

Advancement should require exact evidence identities, not agent exit alone. The upgrade-candidate record should include branch/SHA, test evidence, reviewer verdict, unresolved risks, and whether exact-main integration verification is still required.

---

## P2 — Most v2 workflow/control features are server-only and absent from the first-party operator surfaces

### Confirmed behavior

The server exposes v2 concepts including:

- workflows and preview/execute;
- natural-language command resolution;
- recommendations;
- autonomy profiles/settings;
- pause/resume/read-only/drain/emergency-stop/stop-swarm;
- evidence recording;
- review verdicts;
- takeover generation/preserve-stop;
- improvement proposals/start;
- release gate;
- notifications/search.

The current `public/index.html` remains a direct-deploy dashboard. It contains no workflow, autonomy, recommendation, improvement, emergency-control, natural-language command, or release-gate surface.

The current `agentctl.mjs` exposes status/snapshot/workers/leases/queue/branches/sync/direct deploy/review/stop/log, but not those v2 workflow/control APIs.

The ChatGPT skill mirrors the same older operational surface.

### Why this matters

The implementation can contain good APIs and still fail the product goal if the user and ChatGPT cannot reach them through the supported interfaces. The branch currently looks like a v2 backend behind a v0.2/v1 operator experience.

### Required acceptance contract

Either narrow the v2 claim, or expose the important workflows consistently through UI, CLI, and ChatGPT skill.

Minimum operator-visible v2 set:

- current mission;
- workflow presets;
- natural-language command -> preview;
- preview before execution for broad fan-out;
- suggested next actions;
- autonomy level with enforced semantics;
- pause/drain/read-only/emergency stop;
- takeover/recovery action;
- integration/review state;
- release-readiness gate;
- self-improvement lifecycle.

The CLI/plugin do not need every visualization, but they should expose equivalent control semantics.

---

## P2 — Documentation and prompt machine policy are stale relative to the live routing requirement

### Confirmed behavior

- `README.md` says “What v0.2 does”.
- `CONTROL_PLANE.md` describes multi-machine Heaven/Heaven2 registration as future work.
- the browser describes itself as v1/local-only.
- the ChatGPT skill says to operate the Heaven-hosted controller and “Do not use heaven2 unless the user explicitly asks for it.”
- `prompt-templates.mjs` still tells workers that Heaven is read-only/background by default and Heaven2 is the primary development machine.

Those texts predate the current routing directive that Heaven2 is control/credential authority and Heaven is the preferred heavy execution host.

### Required acceptance contract

Do not patch wording alone before the runtime architecture is real. Once the topology is implemented, update the prompt library, README, architecture doc, UI help, CLI help, and ChatGPT skill together and add a consistency test that catches contradictory machine-policy text across first-party surfaces.

---

# Test / acceptance gaps

Current tests provide useful unit coverage for prompt provenance, state migration, usual-swarm role counting, support-current blocking, updater lane differentiation, command mapping, the old Heaven repository-write policy, integration eligibility, recommendations, takeover context, loopback binding, corrupt-state degraded mode, and backup recovery.

This product/workflow audit found no current deterministic coverage for:

- external GitHub/routing-board ownership influencing workflow planning;
- remote worker placement or Heaven2 -> Heaven routing;
- no-fallback behavior when Heaven is unavailable;
- credential-broker semantics;
- autonomy permission enforcement;
- workflow API execution under each autonomy profile;
- self-improvement lifecycle past implementation dispatch;
- UI/CLI/plugin parity with server v2 features;
- routing-manifest-defined support roles overriding generic keyword lanes;
- stale machine-policy documentation detection.

Support 1 separately identified missing lifecycle/concurrency safety tests. Keep that audit as the specialized authority for those defects.

# Recommended implementation order

1. **First:** repair the Support 1 safety blockers before relying on managed completion/integration state.
2. **Then:** make workflow planning ownership-aware so the controller cannot create a duplicate organization.
3. **Then:** implement the authenticated Heaven2-control / Heaven-worker topology and reconcile repository-write policy with isolated worker worktrees.
4. **Then:** enforce autonomy permissions server-side.
5. **Then:** complete self-improvement state transitions through verification/review/upgrade-candidate.
6. **Then:** expose the v2 semantics through UI, CLI, and ChatGPT skill and reconcile docs/prompts.

Do not combine all six into one source checkpoint. Each is independently testable and should remain a separate integration/verification boundary.

# Things deliberately not changed

- No Agent Control production source.
- No tests.
- No updater/frontend/archive code.
- No repository verification cache.
- No release workflow.
- No `CURRENT_REVISION.json` status.
- No existing Learned Rule numbering.

A new Learned Rule was deliberately not allocated from this parallel support branch because multiple live support branches may append the ledger concurrently. The durable lesson is recorded here for the integrating agent to promote without creating a numbering collision:

> **Candidate rule:** declared autonomy, placement, and ownership policy must be enforced at the server dispatch boundary; UI labels, prompt text, or advisory branch discovery are not sufficient policy enforcement.

# Verification actually performed

- Rechecked canonical GitHub `main` first at `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`, then again at `151a370ef6c0b3d4e6b1d8a306576af1ae231c40` before final push. The intervening commits were archive-cleanup audit/evidence changes and did not alter the reviewed Agent Control v2 head or these conclusions.
- Rechecked PR #59 exact head `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5`.
- Inspected all 15 PR-changed filenames and actual relevant source/test/document bodies.
- Inspected the live routing board issue #60 and PR #59 coordination comments.
- Inspected open PR ownership (#55, #58, #59, #62) and queued issue #56.
- Re-read repository agent/training/continuity/verification guidance relevant to task selection and evidence.
- Cross-checked server APIs against UI, CLI, and ChatGPT skill exposure.
- Cross-checked all usages of `autonomyLevel`, `AUTONOMY_PROFILES`, `implementationTaskId`, and `upgradeCandidate` on the reviewed head.

# Not verified

This audit did **not** run:

- Node tests;
- Windows runtime tests;
- Remote Desktop Commander;
- a live Agent Control server;
- an actual Heaven2 -> Heaven worker dispatch;
- Git worktree/process fault injection;
- repository `Verify-Release.ps1`;
- repository `Build-Release.ps1`;
- hosted CI.

Static inspection therefore establishes the product/workflow gaps above, but it does not claim runtime reproduction beyond what the code paths directly prove.

# Parallel-agent integration notes

- PR #59 remains the sole broad Agent Control implementation lane.
- Support 1 is the specialized authority for process/lease/state-race safety findings.
- This audit owns product/workflow acceptance gaps and should be used as acceptance criteria by the Main Programmer, not merged as competing implementation.
- PR #55 remains frontend scope and PR #58 remains updater-publication scope; neither should absorb these Agent Control backend changes.
- Issue #56 remains queued.
- Recheck live `main`, PR #59 head, and routing board before applying any recommendation.

# Successor handoff

The next agent using this audit should:

1. preserve the permanent recursive continuity constitution;
2. re-establish current canonical main and PR #59 exact head;
3. reconcile any newer Main Programmer fixes before treating a finding as open;
4. implement only one independently verifiable acceptance boundary at a time;
5. add deterministic regression coverage before declaring that boundary closed;
6. report exact source/runtime evidence honestly;
7. explicitly require its successor to preserve and recursively pass the same continuity obligation to the agent after them.

**Do not break the chain.**
