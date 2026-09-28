# Deployment multi-instance mutation ownership audit — 2026-09-28

## Status

**P1 runtime-confirmed. Documentation/governance only; no production fix is included in this branch.**

- Canonical repository: `fengie/mhw-mods`.
- Canonical base inspected: `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Support branch: `agent/support-multi-instance-mutation-audit-20260928`.
- Runtime environment: `heaven2`, Windows, .NET SDK/runtime 10.0.401/10.0.12.
- Durable raw reproduction note: `_AGENT_CONTEXT/EVIDENCE/deployment-multi-instance-live-peer-recovery-probe.md`.

## Why this was selected

The canonical next implementation lane, archive streaming cancellation/output budgeting, is already owned by PR #35. PR #34 owns support-bundle privacy, PR #33 owns migration/CAS hardlink aliasing, PR #32 owns updater release-security research, and PR #36 owns the open-PR disposition ledger.

This audit therefore selected a separate mutation-integrity boundary: **whether two manager processes sharing the same state/game workspace can distinguish a crashed operation from a still-live peer operation before recovery mutates it**.

This is narrower than the canonical SQLite audit and legacy-migration audit. Those already note missing logical writer serialization / startup ownership at a broad level; this document is the specialized runtime authority for **DeploymentExecutor live-peer recovery**.

## Existing strengths

Current deployment recovery has strong single-owner crash semantics:

- `ApplyAsync` journals before live mutation.
- file writes are individually precondition-checked and journaled;
- manifest/original ownership/mod-state plus the Committed marker share one SQLite transaction;
- rollback validates actual live bytes before restoring;
- startup recovery handles genuine process-death checkpoints;
- recent filesystem/reparse and native replacement hardening remain intact.

The defect below does not invalidate those single-owner guarantees. It shows that the code has no proof that an incomplete journal is actually orphaned before another live process takes recovery ownership.

## Confirmed P1 — recovery can roll back a still-live peer writer

### Source path

`DeploymentExecutor.ApplyAsync` calls `RecoverIncompleteAsync` at method entry, then later marks a journal row `Writing`, mutates the live file, and only afterward changes the row to `Applied`:

- `src/MhwModManager.Filesystem/DeploymentExecutor.cs:32-105`
- especially lines 42, 56-65, 73-101 on the audited base.

`RecoverIncompleteAsync` selects **every** operation in Prepared/Applying/FilesWritten/StateCommitting/RollingBack/RecoveryRequired and calls `RollbackAsync` without checking whether another process still owns that operation:

- `src/MhwModManager.Filesystem/DeploymentExecutor.cs:172-193`.

Application startup likewise has no process-wide lease before database/migration/recovery work. `App.OnStartup` discovers paths, constructs/initializes the database and services, runs migration/catalog/intelligence, then directly invokes `executor.RecoverIncompleteAsync`:

- `src/MhwModManager.App/App.xaml.cs:23-169`;
- recovery call at lines 156-157.

The only UI busy guard is instance-local. `MainWindowViewModel.RunBusy` checks the current view model's `BusyVisibility`; it cannot serialize a second process.

### Deterministic Windows reproduction

A disposable IntegrationTests probe created two `ManagerDatabase` objects and two `DeploymentExecutor` instances over the same database, blob root, and game root.

1. Writer A starts an Add deployment over an existing `nativePC\x.tex`.
2. A reaches the real `after-file-write` fault-injection seam and pauses. Live bytes are now `MOD`; the journal still says `Writing`.
3. Executor B calls the real `RecoverIncompleteAsync`.
4. B sees A's `Applying` operation and rolls it back to `ORIGINAL`.
5. A resumes, marks its journal Applied, advances through FilesWritten/StateCommitting, commits the manifest, and returns `Success=true`.

6. The final durable manifest says `nativePC\x.tex` is provided by `m1` and expects the mod SHA-256, but the actual live bytes are `ORIGINAL`.

The exact red invariant failed twice, deterministically:

```text
Expected: "MOD"
Actual:   "ORIGINAL"
Total: 1, Failed: 1
```

All assertions before the final live-byte check passed, including writer success and the manifest provider/expected-hash checks. This is a runtime-reproduced committed metadata/live-file divergence, not a theoretical race.

## Root cause

The durable journal currently answers **what state an operation is in**, but not **whether its writer is dead**. Recovery treats “incomplete” as equivalent to “orphaned.”

SQLite WAL and the 10-second busy timeout serialize database mechanics, not the full filesystem+database protocol. Once writer A's SQLite statement/transaction releases its lock, writer B can legitimately read and mutate A's operation while A is still alive and paused between protocol steps.

A recovery journal therefore cannot double as a cross-process ownership lease.

## Secondary risk — stale whole-mod-state overwrite

`MainWindowViewModel.CaptureStage` captures the full UI mod-state dictionary (`MainWindowViewModel.cs:296-297`). `DeploymentExecutor.CommitAfterStateAsync` writes every entry supplied in `afterState` (`DeploymentExecutor.cs:303-335`, especially 323-330).

Two independently planned writers could therefore overwrite each other's enabled/priority state even when their file sets do not overlap. The attempted two-writer probe was intercepted earlier by the live-peer recovery problem, so this remains a **source-supported risk, not a separately runtime-confirmed defect** in this audit.

## Existing test gap

Current recovery tests model an abandoned writer: one executor throws a simulated crash, then a new executor recovers after the first operation has stopped. They correctly cover genuine crash recovery.

No retained test covers:

- a second executor/process recovering while the original writer is still alive;
- exclusive ownership of the recovery domain;
- two app instances targeting the same state/game workspace;
- abandoned-owner acquisition followed by safe recovery;
- distinct workspaces operating concurrently.

The only existing parallel database regression found is `Sqlite_integrity_and_wal_survive_parallel_reads`; it does not exercise a mutation protocol.

## Recommended implementation checkpoint

Prefer a **workspace-scoped OS ownership lease** acquired before any shared-state mutation/recovery and held for the manager lifetime, rather than adding a narrow in-process semaphore to `DeploymentExecutor`.

The lease identity should be derived deterministically from the canonical state/database workspace (and, if needed, game root) so two installations that resolve to the same workspace collide while genuinely independent workspaces do not.

On Windows, a named mutex or equivalently strong OS-owned exclusive lease can provide liveness/abandonment semantics. The implementation must define what happens when the previous owner died, how diagnostics identify the owner, and how path normalization affects lease identity.

Do not use a SQLite row, busy timeout, or “operation exists” check as proof of process ownership. A durable record can outlive its process; an OS ownership primitive should determine whether recovery may take over.

Acquire the lease early enough to cover legacy migration, startup recovery, startup mutation/maintenance, deployment, undo, safe mode, crash-diagnosis deployment, and other workflows sharing the same mutable workspace. A narrower deployment-only lock would leave the already-documented migration ownership race intact.

## Regression / acceptance contract

A future implementation should add tests before changing behavior:

1. active owner A holds the workspace lease; B cannot enter mutation/recovery for the same workspace;
2. reproduce this exact `after-file-write` schedule and prove B cannot roll A back while A is alive;
3. abrupt death/abandoned ownership lets B acquire ownership and then run the existing recovery path to convergence;
4. two different workspace identities can run concurrently;
5. startup fails closed with a clear actionable message rather than continuing partly initialized;
6. lease release is reliable on normal shutdown and abandonment is recoverable after abnormal death;
7. mutation workflows do not bypass the lease through direct service calls;
8. stale full-state commits cannot be produced by two independently active owners.

Then run focused Windows tests plus the exact repository `Verify-Release.ps1` / `Build-Release.ps1` and hosted Windows Release Gate for the production candidate.

## Things deliberately not changed

- no production C#;
- no retained test source;
- no schema or SQLite transaction redesign;
- no updater/archive/diagnostics/migration implementation;
- no verification cache or historical verification claim.

The disposable red probe exists only as characterization evidence and is removed before commit.

## Verification actually performed

- fetched canonical `origin/main` and inspected current open PR/branch ownership;
- inspected actual startup, DeploymentExecutor, ManagerDatabase, UI busy/staging, canonical audits, and current tests;
- IntegrationTests Release build with disposable probe: **PASS, 0 warnings / 0 errors**;
- direct xUnit v3 execution of the live-peer recovery probe: **1/1 FAIL as expected**, twice, each failing only at the final live-byte invariant (`MOD` expected, `ORIGINAL` actual);
- after removing the disposable probe, IntegrationTests Release build: **PASS, 0 warnings / 0 errors**;
- retained `DeploymentTests` direct xUnit v3 class run: **6/6 PASS**;
- `CURRENT_REVISION.json` and `handoff-manifest.json`: parse PASS;
- `scripts/Test-AgentHandoff.ps1`: PASS;
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1`: baseline accepted and **4/4 broken fixtures rejected**;
- the earlier `dotnet test --filter` attempt selected zero tests and is explicitly **not evidence**;
- no full `Verify-Release.ps1`, `Build-Release.ps1`, hosted Windows gate, or cache promotion is claimed for this documentation-only branch.

## Learned rule / reusable doctrine

This audit adds **LR-012 — recovery must prove writer orphanhood before takeover**. LR-011 is intentionally not reused because active PR #33 already reserves LR-011 for immutable content-store byte ownership.

The generic trainer is updated with the reusable rule: durable “in progress” state describes protocol state, not writer liveness; cross-process recovery must first establish exclusive ownership or prove the previous owner is gone.

## Parallel-agent integration notes

PRs #32-#35 and #37 remain separate active implementation/audit lanes. This audit is PR **#38**. PR #36 created a 30-open-PR disposition ledger before PRs #37 and #38 appeared, so that ledger must be refreshed before being treated as current inventory.

This audit should be integrated as research/continuity without overwriting newer verification state. Its future production checkpoint should remain separate from archive streaming, support-bundle sanitation, updater work, and the migration hardlink repair.

## Successor handoff

Preserve the permanent continuity constitution and active Learned Rules, including the parallel LR-011 reservation and this branch's LR-012. Before implementing this finding, re-check canonical main and active PR ownership. The successor must explicitly require the agent after them to preserve and recursively propagate the same continuity system.
