# Agent Control v2 Safety Verification — 2026-09-28

Status: **documentation-only independent Support 1 verification**  
Support branch: `agent/support-agent-control-v2-safety-verification-20260928`  
Canonical `main` at branch creation: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`  
Target implementation: draft PR #59, `feature/agent-control-plane-v2-20260928`  
Exact target head inspected: `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5`

This audit follows live swarm routing issue #60. It owns **Support 1 / Agent Control safety verification** only. It does not modify PR #59 production code and does not overlap Support 2 product/workflow review, frontend PR #55, updater PR #58, or queued updater issue #56.

## Assignment

Verify the v2 control plane against the safety/ownership invariants inherited from `_AGENT_CONTEXT/AGENT_CONTROL_PLANE_SAFETY_AUDIT.md`, with special attention to:

- exact process ownership and stop semantics;
- PID reuse / controller-restart reconciliation;
- lease retention and release;
- concurrent capacity / state mutation races;
- corrupt-state recovery;
- loopback-only unauthenticated binding;
- deterministic regression coverage.

Required evidence is actual source/test control flow at the exact PR head, not filenames or implementer summaries.

## Parallel-work state

At selection time:

- canonical `main` was initially `a8b581176aac0e6bcf09c049285ed40f4b2b392c` and advanced during this audit to `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`;
- PR #59 remained at exact reviewed head `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5`;
- PR #55 owns frontend finalization;
- PR #58 owns updater-publication closure;
- issue #56 remains queued rather than an active implementation lane;
- PR #62 is a separate persisted game-profile path-containment audit.

The Support 1 claim was recorded on live routing issue #60 before durable edits. Findings were also posted directly to PR #59 so the Main Programmer can act without waiting for this documentation PR.

## Existing strengths / prior-audit closure

PR #59 materially improves the earlier PR #47 safety boundary. The following prior findings are closed or substantially closed in the exact reviewed v2 source.

### Prior ACP-P1-01 — stop failure could release a live worker lease

**v2 disposition: materially closed.**

`server.mjs:848-959` now:

- treats nonzero `taskkill` exit as failure;
- waits for PID disappearance after kill;
- transitions a failed/unproven stop to `blocked`;
- preserves the lease when termination is unproven;
- releases the lease only after a proved exit path.

Preserve this invariant.

### Prior ACP-P1-02 — persisted PID trusted as ownership after restart

**v2 disposition: materially closed.**

`server.mjs:340-403` requires both the current controller session and the live in-memory `children` entry before treating an active worker as owned. A session mismatch becomes `orphaned` with no authoritative completion evidence. `stopAgent` separately refuses destructive stop unless `ownerSessionId === SESSION_ID` and the tracked `ChildProcess` PID matches.

This is a substantial safety improvement over numeric-PID-only restart handling.

### Prior ACP-P1-03 — last-message text could promote unknown restart outcome to done

**v2 disposition: closed in inspected path.**

On controller-session mismatch, `refreshState()` sets `orphaned` and clears completion evidence. When an owned process disappears without an observed exit event, it becomes `interrupted`. A last-message file updates diagnostics/progress but no longer proves successful completion.

### Prior ACP-P1-04 — unauthenticated listener could be configured off-loopback

**v2 disposition: closed for the current architecture.**

`server.mjs:1921-1926` rejects startup unless the configured bind-host string is one of `127.0.0.1`, `localhost`, or `::1`. The focused server safety suite contains a non-loopback rejection test.

### Prior ACP-P2-01 — concurrent deploys could oversubscribe capacity

**v2 disposition: core race closed, with a separate batch-atomicity gap below.**

The controller now serializes direct deploy, review deploy, and workflow deploy paths through `withDeployLock()`. Capacity accounting includes active `reserved`, `starting`, `running`, `waiting`, `blocked`, `stale`, and `stopping` states.

### Prior ACP-P2-02 — unreadable state could fail open to an empty authoritative registry

**v2 disposition: materially closed.**

`loadState()` attempts the backup and otherwise returns explicit degraded state with read-only + dispatch-paused settings. `saveState()` and mutation guards refuse ordinary mutation while the registry is degraded. The server safety suite covers both corrupt-primary+backup degraded mode and backup recovery preserving uncertain work/lease ownership.

### Prior ACP-P2-03 — artifact/worktree retention

**v2 disposition: not reclassified here.**

A long-term reap/retention lifecycle remains operational work rather than a blocker for this narrow safety review.

---

# New findings at PR #59 head 41d377d1

## ACV2-P1-01 — child-exit callback can overwrite newer authoritative registry state after an await

**Severity:** P1  
**Confidence:** confirmed static whole-state lost-update race; not runtime-reproduced

### Evidence

In `tools/agent-control/server.mjs:781-819`, the child `exit` callback:

1. calls `loadState()` and receives a complete in-memory registry snapshot;
2. mutates that snapshot for the exiting agent;
3. executes `await git(["rev-parse", item.branchName])`;
4. only after that asynchronous yield calls `saveState(current)`.

`saveState()` (`server.mjs:116-130`) serializes and replaces the **entire** control-plane JSON registry.

The child-exit callback is not inside `withDeployLock()` and there is no global state-mutation transaction/serialization primitive.

### Concrete race

A reproducible interleaving is:

1. exit callback A loads registry version N;
2. A reaches the awaited Git call and yields;
3. a deploy request persists a new task + lease (and potentially later a new agent), producing registry N+1;
4. A resumes with its stale version-N object;
5. A writes the whole object back through `saveState(current)`.

The newer task, lease, settings mutation, notification, or agent can be lost.

A more dangerous variant can remove the durable record for a newly launched worker while the OS process and in-memory `children` object remain live. That breaks the control plane's ownership/lease/capacity model rather than merely losing cosmetic telemetry.

Two nearly simultaneous child exits can also overwrite one another's terminal transition because each callback can load before its asynchronous Git evidence lookup and save afterward.

### Required regression contract

Do not carry a whole-registry snapshot across an `await` and later overwrite authoritative state.

A safe implementation checkpoint should:

- obtain non-state async evidence before opening the mutation, then load fresh state and revalidate the target agent identity/session before writing; **or**
- introduce a single serialized state-mutation boundary that covers all read-modify-write operations and never allows a stale snapshot to commit after another transaction;
- preserve atomic temp/backup file replacement;
- deterministically inject an overlap between an exit callback and another mutation and prove both updates survive;
- also test two overlapping exits.

Do not solve only the deploy path; state/settings/review/evidence mutations share the same whole-file registry.

## ACV2-P1-02 — operator-stopped worker can become integration-eligible when its exit code is zero

**Severity:** P1  
**Confidence:** confirmed static state-classification defect; zero-exit stop race not runtime-reproduced

### Evidence

`stopAgent()` first persists `agent.status = "stopping"` (`server.mjs:903-911`).

The authoritative child exit handler later classifies the result using:

`item.status = code === 0 ? "done" : (item.status === "stopping" ? "stopped" : "failed");`

at `server.mjs:787`.

Because the zero-exit test is evaluated first, an agent whose prior durable status is `stopping` becomes `done` when the process exits with code 0.

The same callback records:

- `exitCode = 0`;
- `completionEvidence = "authoritative-exit"`;
- lease release.

`lib/control-core.mjs:119-120` defines integration eligibility as exactly:

- `status === "done"`;
- `exitCode === 0`;
- `completionEvidence === "authoritative-exit"`.

Therefore an operator-requested stop that races with a normal zero exit can satisfy the integration eligibility predicate and surface an intentionally canceled/incomplete branch as a candidate.

### Required regression contract

Stop intent must dominate ordinary success classification.

At minimum:

- prior `stopping` + any authoritative exit => terminal `stopped`, never `done`;
- `stopped` must remain ineligible for integration;
- deterministic test the exact `stopping + exitCode 0` transition;
- test the queue output, not only a pure enum helper, so cancellation cannot become an eligible candidate through another path.

## ACV2-P2-01 — setup failure after worktree creation can strand a boundary lease with no agent owner

**Severity:** P2 availability / coordination integrity  
**Confidence:** confirmed static cleanup gap; not runtime-reproduced

### Evidence

`deployOne()`:

1. acquires and persists the lease + reserved task at `server.mjs:603-643`;
2. creates the worktree inside a cleanup catch at `645-665`;
3. after worktree success, calls `findCodex()` at line 667;
4. proceeds through log open, spawn, prompt creation/write, and later agent registration.

The cleanup catch covers only worktree creation failure.

`findCodex()` explicitly throws when the Codex install cannot be found. If that happens after the worktree succeeds, the persisted task remains `reserved`, the lease remains `active`, and no agent record exists for `stopAgent()` to own/release.

Similar synchronous setup failures after the worktree catch and before durable agent registration can produce the same class of stranded reservation.

### Required regression contract

Every failure after durable reservation and before durable running-agent registration must converge to a known safe state:

- task becomes failed/blocked with error evidence;
- lease is released only because no worker process was successfully launched;
- created worktree/branch retention or cleanup is explicit and evidence-safe;
- no ownerless active lease remains;
- inject at least `findCodex` failure and prompt/log setup failure.

## ACV2-P2-02 — direct counted deploy can partially launch workers but return only HTTP failure

**Severity:** P2 operator/API idempotency risk  
**Confidence:** confirmed static request semantics; not runtime-reproduced

### Evidence

The direct `POST /api/deploy` path is correctly wrapped in `withDeployLock()`, but it loops from zero to `count` and executes `deployOne()` one worker at a time.

There is no capacity preflight for the whole requested count and no local partial-result catch.

Example with one free slot and `count=2`:

1. first `deployOne()` succeeds and its worker remains live;
2. second `deployOne()` sees capacity full and throws;
3. the outer request falls into the generic HTTP error path;
4. caller receives an error rather than the already-created worker list.

A retry can create work the caller did not realize already exists.

### Required regression contract

Choose and test explicit request semantics:

- either reject the batch before launching anything when requested count exceeds available slots; or
- return structured partial success including all created worker IDs and the exact blocked remainder.

Do not return an opaque request failure after irreversible worker launches.

---

# Test coverage assessment

Exact inspected `tools/agent-control/test/server-safety.test.mjs` covers:

- non-loopback startup refusal;
- corrupt primary+backup => read-only degraded mode;
- backup recovery => persisted uncertain worker becomes orphaned and retains its active lease.

Exact inspected `control-core.test.mjs` covers integration eligibility as a pure predicate, machine policy, workflow planning, migration, recommendations, and takeover context.

Missing focused regressions for this safety boundary:

1. stop requested + child exit code 0 => stopped, never done/eligible;
2. termination failure => blocked and lease retained (old C1 behavior now implemented but not directly exercised by the current server fixture);
3. concurrent exit callback + independent state mutation => no lost update;
4. two concurrent exits => both terminal transitions preserved;
5. post-worktree `findCodex`/setup failure => task/lease converge safely;
6. counted deploy near capacity => explicit atomic or partial-success semantics;
7. same-session PID/process identity destructive-stop seam, including the narrow liveness-to-`taskkill` TOCTOU window if a stronger Windows process primitive is later introduced.

A test harness should use a fake worker executable / injected child-process boundary and temporary state/worktree roots. Do not require a real Codex account or the user's working checkout for deterministic safety regressions.

# PID reuse / descendant-process note

The most dangerous **restart** PID-reuse problem from the old audit is fixed because v2 refuses ownership when the controller session / live `ChildProcess` record is absent.

A narrower same-session PID-only termination race remains theoretically possible because Windows stop ultimately invokes `taskkill /PID`, but this audit does **not** label it a confirmed runtime bug without a demonstrated process-lifetime interleaving.

Similarly, natural root-process exit currently releases its lease without proving that no independently surviving descendant can continue repository mutation. This is an unresolved process-tree ownership design question, not promoted to a confirmed finding here.

# Deliberately not changed

This support checkpoint does not modify:

- PR #59 production JavaScript;
- PR #59 tests;
- Agent Control UI/product workflow;
- updater/frontend/archive/filesystem code;
- `CURRENT_REVISION.json`, `CURRENT_STATE.md`, `NEXT_STEPS.md`, or the handoff manifest;
- verification cache/evidence;
- Git tags, releases, remotes, or machine repositories.

Hot continuity snapshots are deliberately left untouched because live swarm issue #60 is the current routing authority and PR #59 is concurrently owned by the Main Programmer. This audit is additive evidence only.

# Verification actually performed

- established canonical GitHub `main` and rechecked it repeatedly while work was in progress;
- observed `main` advance from `a8b581176aac0e6bcf09c049285ed40f4b2b392c` to `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`;
- inspected recent canonical history and open PR inventory;
- read live swarm routing issue #60 and claimed Support 1 before durable edits;
- read the earlier `AGENT_CONTROL_PLANE_SAFETY_AUDIT.md` and compared every relevant prior finding against v2;
- inspected exact PR #59 source bodies in `server.mjs`, `lib/control-core.mjs`, and current safety/core tests;
- traced the deploy lock/capacity, lease acquisition, process launch, child-exit, stop, restart/orphan, state recovery, loopback binding, and integration-eligibility call paths;
- posted the four findings to PR #59 for immediate Main Programmer visibility;
- rechecked PR #59 head before branch creation; it remained `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5`.

# Not verified

- no Node test suite was executed by this support checkpoint;
- no fake-Codex process harness was run;
- no Windows `taskkill` failure/exit-zero stop race was runtime-injected;
- no concurrent state-mutation stress fixture was run;
- no actual controller restart/PID-reuse fixture was run;
- no worktree/setup failure was runtime-injected;
- no GitHub Actions run is claimed for this support branch;
- PR #59 is not declared merge-ready by this audit.

The conclusions marked confirmed are static control-flow/data-integrity conclusions. Runtime-dependent platform claims remain labeled as unverified/theoretical.

# Learned-rule decision

No new project Learned Rule is added yet.

The strongest new reusable lesson is that a whole-registry read-modify-write snapshot must not survive an asynchronous yield and later overwrite newer authoritative state. However, the triggering implementation is still an unmerged draft PR. Promote that lesson into `LEARNED_RULES.md` only after the implementation checkpoint confirms the final transaction model, rather than encoding an unmerged candidate's defect as canonical project history.

# Recommended next checkpoint

Main Programmer / focused repair checkpoint on PR #59:

1. fix **ACV2-P1-01** with a serialized/fresh-state mutation strategy;
2. fix **ACV2-P1-02** so stop intent dominates zero-exit success;
3. add deterministic regressions for both P1s;
4. then close the two P2 deploy-convergence semantics;
5. reconcile PR #59 onto current canonical `main`;
6. run exact-head Agent Control tests plus repository-required verification before integration review.

Support 1 should re-review the exact repaired head rather than assuming a patch closes the race.

# Parallel-agent handoff

- PR #59 remains the sole Agent Control v2 implementation lane.
- This support branch is documentation/review evidence only.
- Support 2 should preserve ownership of product/workflow acceptance rather than duplicating these process/state safety findings.
- Frontend PR #55, updater PR #58, queued issue #56, and unrelated support PRs remain outside this boundary.
- If PR #59 changes after `41d377d1`, re-evaluate the exact modified functions/tests before carrying any severity forward.
- Preserve the permanent continuity constitution and active Learned Rules, and require the next successor to preserve and recursively pass them to the agent after them.

**Do not break the chain.**
