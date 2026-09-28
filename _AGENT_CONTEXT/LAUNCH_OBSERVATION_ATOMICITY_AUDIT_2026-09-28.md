# Launch observation persistence atomicity audit — 2026-09-28

## Status

**Static support audit. No production behavior changed in this checkpoint.**

Canonical repository inspected: `fengie/mhw-mods`  
Canonical `main` inspected: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`  
Support branch: `agent/support-launch-observation-atomicity-audit-20260928`

This audit specializes one defect already identified at a high level by
`SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`: one observed game launch is persisted
through two independent authoritative writes, so the trust aggregate and launch-history
evidence can disagree.

The broad SQLite audit remains the authority for the repository-wide transaction map.
This document is the specialized authority for the **launch observation -> history ->
trust -> issue-diagnosis evidence chain**.

## Why this lane is independent

The current open draft PRs at task selection time own:

- PR #58: updater post-upload publication / stale-main safety;
- PR #55: frontend UX and responsive presentation;
- PR #47: agent control panel.

Recent canonical work also closed archive extraction streaming cancellation/resource
budgeting. Existing support branches cover updater, multi-instance mutation, migration
hardlink behavior, remote-preview egress, support-bundle sharing, and visual/category
reparse topics.

This audit does not edit any of those boundaries. It is limited to automation/storage
evidence persistence after a game-launch outcome has already been observed.

## Internal assignment

Inspect the actual source and tests for:

- `AutomationCoordinator.LaunchAndObserveAsync`;
- `AutomationCoordinator.RecordLaunchAsync`;
- `ModTrustService.RecordLaunchAsync`;
- `ModIssueFallbackService` launch-history/trust consumers;
- `Schema.Sql` definitions for `launch_history` and `mod_trust`;
- MainWindow foreground-operation ownership around Just Play;
- existing automation tests for trust and issue diagnosis;
- overlapping launch-health, crash-diagnosis, storage-transaction, and test-gap audits.

Answer:

1. What durable state represents one launch observation?
2. Can part of that state commit while another part fails or is cancelled?
3. Is a retry safe and idempotent?
4. Do trust counters and `launch_history.state_json` necessarily describe the same
   mod snapshot?
5. What diagnostic behavior consumes these records later?
6. What is the smallest repair boundary and fault-injection contract?

Exclusions:

- updater publication/install behavior;
- WPF visual redesign;
- agent-control-plane work;
- game-build freshness/revalidation policy itself;
- process-running launch policy;
- crash-bisector narrowing logic;
- deployment/CAS/filesystem transaction redesign;
- timeline events as authoritative state.

## Methodology

The audit inspected source bodies rather than relying on filenames or test names.

Evidence inspected on exact canonical main:

- `src/MhwModManager.Automation/AutomationCoordinator.cs`;
- `src/MhwModManager.Automation/ModTrustService.cs`;
- `src/MhwModManager.Automation/ModIssueFallbackService.cs`;
- `src/MhwModManager.Storage/Schema.cs`;
- `src/MhwModManager.Storage/ManagerDatabase.cs`;
- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs`;
- `tests/MhwModManager.AutomationTests/AutomationServiceTests.cs`;
- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`;
- `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`;
- `_AGENT_CONTEXT/LAUNCH_HEALTH_REVALIDATION_AUDIT.md`;
- `_AGENT_CONTEXT/CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md`;
- `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md`.

No runtime fault was injected in this support environment. Findings below distinguish
source-confirmed consistency windows from behavior that still requires a focused runtime
regression.

---

# Current launch observation protocol

`LaunchAndObserveAsync` currently:

1. runs the pre-launch health workflow;
2. reads the currently enabled mod IDs;
3. creates a fresh `launchId` and starts the game process;
4. observes the startup window and determines `survived` / `exitCode`;
5. calls `ModTrustService.RecordLaunchAsync(enabled, survived, false, ct)`;
6. calls its private `RecordLaunchAsync(...)`, which independently:
   - reads the current game-build fingerprint;
   - re-reads the current mods and builds `state_json`;
   - opens a new SQLite connection;
   - inserts one `launch_history` row;
7. only after both writes does it clear ordinary issue suspects / write last-known-good
   state on success, or diagnose a modded startup failure by exact `launchId`.

`ModTrustService.RecordLaunchAsync` opens its own connection and transaction and
increments the trust counters for every distinct enabled mod.

The schema has:

- `launch_history(id PRIMARY KEY, ..., state_json, ...)`;
- `mod_trust(mod_id PRIMARY KEY, successful_launches, failed_launches, ...)`.

There is no launch ID recorded with a trust increment.

---

# Existing strengths

1. A launch-wide trust batch is already atomic **within `mod_trust`**. If one trust
   update fails before that transaction commits, the batch can roll back together.
2. `launch_history.id` is unique, so the repository already has a natural durable
   idempotency key for one external observation.
3. Issue diagnosis reads a specific failed launch by ID before persisting startup-crash
   suspects, which is safer than guessing from only the latest state.
4. The normal WPF Just Play path is wrapped in `RunBusy`, and the foreground handoff
   gate serializes normal foreground `RunBusy` operations. This reduces ordinary
   same-UI concurrent mutation during the observation window.
5. Timeline writes occur after the authoritative work and are appropriately treated as
   advisory rather than being forced into unrelated domain transactions.
6. The code already uses explicit SQLite transactions in adjacent storage boundaries;
   no generic UnitOfWork redesign is required to close this gap.

---

# Finding LO-01 — P1: one launch can commit trust without a launch-history record

## Classification

**Confirmed source-level consistency defect. Runtime failure injection still required.**

The authoritative calls are sequential and independently committed:

`trust.RecordLaunchAsync(...)`

then:

`RecordLaunchAsync(...)`

The first call can commit its SQLite transaction before the second call opens/reads/writes
on a different connection.

If the process terminates, the second DB operation fails, or cancellation becomes
effective after the trust commit and before the history insert commits, the durable
result is:

- `mod_trust` counts the launch;
- `launch_history` has no row for that launch ID.

No enclosing transaction can roll the first commit back.

## Why this matters

`ModIssueFallbackService` consumes both evidence sets:

- it uses `launch_history` to identify the failed launch, its mod-state snapshot, and
  the previous successful baseline;
- it uses `mod_trust` to increase suspicion from prior failed launches and reduce
  suspicion after repeated successful launches.

A trust-only failed observation can therefore increase future suspicion even though no
launch-history record exists for the observation that caused the increment. A trust-only
successful observation can reduce future suspicion without corresponding baseline
evidence.

For a failed launch, `LaunchAndObserveAsync` later calls
`RecordLaunchFailureAsync(launchId, ...)`. If history persistence failed, execution
never reaches that step; and an explicit diagnosis for that ID would fail because the
history row does not exist.

This is persistent evidence drift, not merely missing telemetry.

---

# Finding LO-02 — P1: post-outcome user cancellation can split or discard authoritative evidence

## Classification

**Confirmed cancellation window from source ordering; exact timing should be reproduced
with a focused test seam.**

The startup outcome becomes externally true before persistence:

- the process has either survived the observation window or exited;
- `survived`, `exitCode`, and elapsed time have already been determined.

The same user-cancellable token is then passed into:

- the trust transaction;
- game-build lookup;
- mods re-read;
- launch-history insert.

The normal Just Play command is explicitly cancellable in `RunBusy`.

Once an external process outcome has been observed, cancellation cannot undo that launch.
Allowing the user token to interrupt authoritative persistence can therefore create:

- no durable observation at all; or
- the LO-01 partial state if cancellation arrives after the trust transaction commits.

Cancellation should still stop optional/downstream work, but it should not be able to
erase or split the already-observed launch result.

---

# Finding LO-03 — P1: the current protocol has no idempotent retry for a partially persisted launch

## Classification

**Confirmed from schema and write semantics.**

`launch_history` has a launch primary key, but `mod_trust` stores only aggregate
per-mod counters. Trust updates do not carry `launchId`.

After a trust-only partial commit, retrying the current pair of calls with the same
launch observation would increment trust again before attempting the history insert.
There is no durable marker that says “the trust delta for launch X was already applied.”

The current API therefore cannot safely distinguish:

- first application;
- retry after pre-commit failure;
- retry after an ambiguous/partial prior attempt.

The existing launch ID can solve this if history + trust are committed in one
transaction and exact replay semantics are defined.

---

# Finding LO-04 — P2 contract gap: trust IDs and history state are captured at different times

## Classification

**Service-level consistency risk, not reproduced as a normal UI race.**

The enabled IDs passed to trust are read **before process start**.

The `launch_history.state_json` is built later by re-reading all mods **after the
startup observation window**.

The normal WPF path reduces ordinary UI concurrency because Just Play owns `RunBusy`
and the foreground gate. That is a useful protection and this audit does not claim a
reproduced user-interface race.

However, the persistence service itself has no contract tying both evidence products to
one immutable mod snapshot. A future caller, background mutation, or out-of-process DB
writer could make trust count one set while history records another.

The clean repair is not another lock around the UI. Capture one immutable launch-state
snapshot before starting the process and use that same snapshot for both persisted
products.

---

# Finding LO-05 — downstream effects should remain outside the authoritative transaction

Not every post-launch side effect should be pulled into one giant transaction.

After launch observation persistence:

- issue-suspect clearing/scoring;
- last-known-good update;
- timeline emission

have different ownership and failure semantics.

The narrow authoritative atomic unit is:

> **one launch-history observation plus exactly-once trust deltas derived from the same
> immutable launch-state snapshot.**

Timeline remains best-effort/advisory. Issue and last-known-good work can reference the
durable launch ID and retry independently.

This keeps the repair aligned with the repository rule that transaction boundaries
follow logical ownership rather than table organization.

---

# Recommended implementation boundary

Create one operation-level storage owner, for example
`LaunchObservationRepository.PersistAsync` or an equivalently named command.

Do **not** create generic repositories merely to move SQL.

## Input

Pass one immutable observation object containing at least:

- `launchId`;
- started/ended or elapsed time;
- launch mode;
- success/startup-survived;
- exit code;
- exact game-build fingerprint chosen by the canonical pre-launch policy;
- the exact immutable mod-state snapshot captured for this launch;
- rollback flag if that concept is later part of this same observation contract.

Capture the mod state once before process start and derive the trust-target mod IDs from
that same object.

This audit does not redefine game-build freshness; the launch-health/revalidation audit
remains authority for when that fingerprint must be refreshed.

## Transaction

On one `SqliteConnection` + one `SqliteTransaction`:

1. look up `launch_history` by `launchId`;
2. if it already exists:
   - verify that immutable observation fields/state agree;
   - return an explicit already-persisted result without touching trust;
   - if the payload conflicts, fail closed;
3. if it does not exist:
   - insert `launch_history`;
   - apply every `mod_trust` delta using the same transaction;
4. commit once.

Ordering the history insert and trust updates inside the same transaction means any
statement failure rolls back both. Exact replay by launch ID can then be made
idempotent without a second trust-application table.

If future requirements allow trust processing to be asynchronous/separate, introduce an
explicit launch-ID-keyed application ledger instead. Do not rely on aggregate counters
alone for exactly-once replay.

## Cancellation after external outcome

Before process outcome is known, normal user cancellation should continue to work.

After the outcome is known, authoritative observation persistence should complete under
a short bounded persistence policy independent of the user's cancel button. SQLite's
existing busy timeout bounds lock waiting. Do not use this recommendation to make
arbitrary downstream work non-cancellable.

---

# Required regression / fault-injection matrix

Implement tests before declaring this boundary fixed.

1. **Failure after history insert, before first trust delta**
   - inject a statement failure;
   - assert no `launch_history` row and no trust increment survive.

2. **Failure after at least one trust delta**
   - use two or more mods and inject failure after the first trust update;
   - assert the launch row and all trust updates roll back together.

3. **Exact replay**
   - persist launch X successfully;
   - persist exact launch X again;
   - assert trust counters do not increment twice.

4. **Conflicting replay**
   - persist launch X;
   - replay X with different success/state/build data;
   - fail closed and preserve original evidence.

5. **Cancellation after process outcome**
   - deterministically request user cancellation after the launch result is known but
     before persistence;
   - assert the authoritative observation still reaches a complete all-or-nothing
     durable state.

6. **Snapshot identity**
   - assert enabled IDs receiving trust deltas are exactly the enabled IDs represented
     by the persisted `state_json`.

7. **Failed-launch diagnosis linkage**
   - after persisting a failed modded launch, assert
     `RecordLaunchFailureAsync(launchId,...)` can resolve that exact row.

8. **No partial multi-mod trust batch**
   - a practical SQLite fault fixture can use a valid first mod and an invalid
     FK-referencing second mod to force a mid-transaction failure, if the final command
     surface preserves deterministic input order.

A dedicated internal fault hook is acceptable only if natural SQLite constraints cannot
give deterministic phase coverage; do not weaken production validation for tests.

---

# Existing test coverage and exact gap

Current automation tests prove:

- `ModTrustService` accumulates successful/failed/rollback counts;
- issue fallback can rank a new texture/plugin mod from manually inserted launch rows;
- failed-launch issue scoring can resolve a manually inserted launch ID;
- successful launch issue clearing works;
- confirmed bisect marks survive ordinary success clearing.

No inspected test owns the cross-table invariant:

> one external launch observation is either absent from both authoritative evidence
> products or present exactly once in both.

The broad test-gap audit calls for more statement-level fault injection around
non-deployment SQLite batches but does not provide this launch-specific contract.

---

# Severity summary

| Finding | Severity | Status |
|---|---|---|
| LO-01 split trust/history commit | **P1** | confirmed by source transaction boundaries |
| LO-02 post-outcome cancellation evidence loss/split | **P1** | confirmed window; timing regression needed |
| LO-03 non-idempotent partial retry | **P1** | confirmed by schema/API semantics |
| LO-04 different-time trust/history snapshots | **P2** | service-contract risk; normal UI race not reproduced |
| LO-05 keep downstream effects separate | design constraint | recommendation |

No P0 was identified. This defect distorts persistent diagnosis/trust evidence but does
not directly mutate the live game tree or user mod payloads.

---

# Things deliberately not changed

This checkpoint does not modify production C#, schema, tests, updater code, UI, process
launch behavior, game-build freshness policy, crash-bisector logic, deployment
transactions, or verification caches.

No existing green verification claim is promoted or reused for hypothetical changes.

No new Learned Rule is proposed: the permanent transaction-ownership doctrine and the
existing deep SQLite audit already encode the general lesson. This document adds the
specialized launch-observation contract rather than duplicating the rule ledger.

---

# Verification actually performed

For exact canonical main `a8b581176aac0e6bcf09c049285ed40f4b2b392c`:

- verified current remote `main` HEAD and recent merge history;
- inspected all current open PRs and avoided their owned boundaries;
- confirmed no branch/PR named for this launch-observation atomicity lane;
- inspected the actual source bodies listed in Methodology;
- inspected relevant current automation tests;
- reconciled overlap against the broad SQLite, storage-boundary, launch-health,
  crash-diagnosis, and test-gap audits;
- verified the normal Just Play caller is inside the serialized `RunBusy` foreground
  path;
- created this isolated support branch from the exact main SHA.

Not performed:

- no .NET build;
- no unit/integration test execution;
- no Windows runtime fault injection;
- no process-kill/crash reproduction;
- no verification-cache promotion;
- no production behavior change.

These limitations are intentional and must not be described as runtime proof.

---

# Unresolved questions for implementation

1. Should exact replay of an already persisted launch return a normal
   `AlreadyPersisted` result or a typed exception? Either is safe if it never reapplies
   trust and validates payload identity.
2. Which canonical pre-launch component should supply the exact build fingerprint after
   the separate launch-health freshness repair? Do not duplicate build-freshness logic
   inside the storage command.
3. Should historical `mod_trust` drift be repaired by a one-time rebuild from
   `launch_history`? Current history may already be incomplete, so a destructive
   recomputation cannot be assumed correct without migration evidence.
4. Should `launch_history.state_json` include all mod state or only enabled state long
   term? Preserve current diagnostic semantics in the first fix.

---

# Recommended next checkpoint

Implement **only** the launch-observation persistence command and its focused
all-or-nothing/idempotency regressions.

Acceptance criteria:

- one immutable launch snapshot feeds history and trust;
- history + trust share one connection/transaction;
- a mid-command failure leaves neither partial history nor partial trust;
- exact retry cannot double-count;
- conflicting replay fails closed;
- once an external launch outcome is known, user cancellation cannot erase/split its
  authoritative persistence;
- failed-launch diagnosis resolves the exact durable launch ID;
- strict build/focused tests pass;
- then run the normal full Windows verification/release gate before closure.

Do not combine this checkpoint with the broader launch-health/revalidation fixes.

---

# Parallel-agent integration notes

- PR #58 remains authority for updater publication.
- PR #55 remains authority for frontend UX.
- PR #47 remains authority for the agent control panel.
- `SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` remains the broad cross-repository
  transaction authority; this file specializes its D2 finding.
- `LAUNCH_HEALTH_REVALIDATION_AUDIT.md` remains authority for build freshness,
  revalidation clearing/gating, and running-process launch policy.
- `CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md` remains authority for baseline/control
  validity and confirmed bisect evidence.
- Timeline/diagnostic persistence should not be pulled into this atomic unit.
- If any active branch changes these source bodies before implementation, rebase the
  reasoning onto the new canonical bodies rather than copying line-level instructions.

---

# Successor handoff

The next agent implementing this lane must re-read canonical `main`, the permanent
continuity constitution, active Learned Rules, the broad SQLite transaction audit, and
this specialized audit before editing.

Start with the focused transaction/idempotency tests. Keep the first production change
to one launch-observation persistence boundary. Preserve exact verification evidence,
update durable handoff state, commit/push meaningful checkpoints, and explicitly require
your successor to inherit, preserve, and recursively propagate the same continuity
constitution to the agent after them.

**Do not break the chain.**
