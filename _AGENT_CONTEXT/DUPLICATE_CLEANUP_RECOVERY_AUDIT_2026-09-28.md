# Duplicate Cleanup Move/Delete Recovery Audit — 2026-09-28

## Status

**Documentation-only support audit. No production code, schema, tests, verification cache, or workflow behavior changed by this checkpoint.**

Canonical task-lock base: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d` on `fengie/mhw-mods` `main`.

Selected scope: the cross-domain recovery boundary in `DuplicateCleanupService.ArchiveSafeAsync` between moving a source package into `Mods Archive` and retiring its database row.

This audit is deliberately separate from:

- LR-007 / `MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`, which owns **what live semantic state must be retired** when a mod entity is removed;
- updater work and release-publication PRs;
- archive-extraction streaming/failure-cleanup work;
- save-snapshot pruning;
- launch-observation persistence;
- profile-save transaction rollback;
- Agent Control v2;
- frontend UX work.

The existing `SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` remains the authority for the original D1 cross-filesystem/database atomicity finding. This document specializes D1 around ordinary cancellation, deterministic fault coverage, and a narrow recovery protocol.

---

## Executive result

### P1 — ordinary user cancellation can strand a live mod row after its source folder has already moved

Current source order in `DuplicateCleanupService.ArchiveSafeAsync` is:

1. inspect candidates;
2. check the supplied cancellation token;
3. choose a unique archive destination;
4. synchronously execute `Directory.Move(mod.SourcePath, dest)`;
5. execute `DELETE FROM mods WHERE id=$m` through `ManagerDatabase.ExecuteAsync(..., ct)`;
6. increment the moved count.

That is a real split durability boundary. The filesystem mutation completes before the database transition begins.

Both current product call sites expose cancellation:

- `MainWindowViewModel.ProcessInbox` calls `RunBusy(..., cancellable: true, ...)` and then `ArchiveSafeAsync(ct)`;
- `MainWindowViewModel.SmartCleanup` does the same.

`RunBusy` creates `busyCts`, exposes the Cancel control, and passes `busyCts.Token` into the action. `CancelBusy` calls `busyCts?.Cancel()`.

The same user-cancellable token is then passed to the database delete **after the folder has moved**. Therefore cancellation requested in the move→delete window can cause the post-move database operation to throw/cancel while the source directory remains in the archive.

This is not merely a process-death scenario. It is reachable through the normal UI cancellation contract.

### Durable consequence

After that split outcome:

- the original `mod.SourcePath` no longer exists;
- the archive destination contains the package;
- the `mods` row can still exist and still reference the missing original source path;
- a later `ArchiveSafeAsync` pass skips the row because it requires `Directory.Exists(mod.SourcePath)`;
- `CatalogService.RefreshFoldersAsync` is add-only for directories it can currently enumerate and does not retire a database mod just because its source folder is missing.

So the inconsistency does not self-heal through the ordinary cleanup/catalog flow.

### No P0 found

The inspected path archives rather than destroys the package; the payload remains at the chosen archive destination in the split state. The high severity is durable state inconsistency and broken retry/cancellation semantics, not confirmed payload loss.

---

## Scope and methodology

Inspected against exact task-lock base `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`:

- `src/MhwModManager.Automation/DuplicateCleanupService.cs`
  - `AnalyzeAsync`
  - `ArchiveSafeAsync`
  - `Unique`
- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs`
  - `ProcessInbox`
  - `SmartCleanup`
  - `CancelBusy`
  - `RunBusy`
- `src/MhwModManager.Filesystem/CatalogService.cs`
  - `RefreshFoldersAsync`
- `src/MhwModManager.Storage/ManagerDatabase.cs`
  - `OpenAsync`
  - `ExecuteAsync`
- `tests/MhwModManager.AutomationTests/AutomationServiceTests.cs`
- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`
- `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`
- current branches, open PRs, recent commits, and continuity state.

The production blobs for `DuplicateCleanupService.cs`, `MainWindowViewModel.cs`, `CatalogService.cs`, and the existing Automation service test were re-fetched after canonical `main` advanced during the audit. The duplicate-cleanup source boundary remained unchanged.

No conclusion here depends only on a filename or test name.

---

## Existing strengths

1. Cleanup eligibility is conservative: only disabled candidates are moved.
2. Exact duplicate analysis has an existing test proving duplicate fingerprint grouping.
3. The archive destination is selected before mutation and avoids an already-existing destination directory.
4. Package payload is moved, not deleted, so the primary data is retained in the observed split-state design.
5. Canonical continuity already records D1 and LR-007, so the repository does not need a competing lifecycle model.

These strengths do not close the move/delete durability gap.

---

## Confirmed source behavior

### Finding F1 — move and DB retirement are independent state transitions

`ArchiveSafeAsync` performs:

`Directory.Move(mod.SourcePath, dest)`

followed by:

`await db.ExecuteAsync("DELETE FROM mods WHERE id=$m", ..., ct)`.

There is no durable operation record before the move, no compensating move on delete failure/cancellation, and no startup reconciliation for a moved-but-not-retired candidate.

**Severity:** P1 / High reliability and state-integrity defect.

### Finding F2 — cancellation remains armed after the first externally visible mutation

The operation checks cancellation before each candidate, which is safe before mutation. But after the successful move, it passes the same cancellation token to the database delete.

Once the first irreversible/external state transition has happened, cancellation cannot safely mean “abandon the rest of the method.” It must mean either:

- compensate back to the pre-operation state; or
- finish/persist enough recovery state that restart can deterministically complete or roll back.

Current code does neither.

**Severity:** P1 contributor.

### Finding F3 — ordinary retry cannot repair the stranded row

The candidate loop requires the original source directory to exist. In the split state it does not, so the stale row is skipped.

`CatalogService.RefreshFoldersAsync` does not delete missing database rows. Its normal refresh therefore does not repair the stale record either.

**Severity:** P1 persistence mechanism.

### Finding F4 — existing tests do not exercise the mutation boundary

The existing duplicate test `DuplicateAnalysisFindsExactPayloads` verifies `AnalyzeAsync` only.

No inspected test pins:

- a successful archive + DB retirement;
- cancellation immediately after the move;
- database failure after the move;
- process/restart reconciliation;
- ambiguous recovery topology;
- retry convergence after an interrupted cleanup.

**Severity:** High test gap because the defect sits at a cross-domain mutation boundary.

---

## Distinguishing confirmed behavior from unverified runtime behavior

### Confirmed from current source/control flow

- filesystem move occurs before DB delete;
- the DB delete receives the user-cancellable token;
- both user-facing callers expose cancellation;
- no compensation/recovery record exists in the inspected service;
- the retry loop skips a row once its original source folder is absent;
- catalog refresh does not retire missing rows.

### Not runtime reproduced in this support session

- the exact timing of a click on Cancel after `Directory.Move` and before SQLite completes;
- a real SQLite lock/failure injected at that exact boundary;
- abrupt process termination in that window;
- restart behavior with a synthetic stranded state.

Those should become deterministic regression fixtures before production repair is considered closed.

---

## Required regression/fault-injection contract

The first implementation checkpoint should be **test-first** and should not rely on timing luck.

### T1 — successful cleanup closes both sides

`ArchiveSafe_success_moves_package_and_retires_mod`

Prove:

- source no longer exists;
- exact archive destination exists with expected bytes;
- live mod row is retired through the intended retirement boundary;
- no pending cleanup operation remains;
- returned moved count is correct.

### T2 — cancellation after move does not strand state

`ArchiveSafe_cancel_after_move_converges_to_preoperation_state`

Inject cancellation at the exact post-move/pre-retirement seam.

Preferred contract for a user-requested cancellation:

- source is restored to the original path when restoration is unambiguous;
- archive destination is removed by the reverse move;
- mod row remains live and unchanged;
- operation record ends rolled back/cleared;
- caller may report cancellation only after compensation/recovery state is durable.

If exact compensation cannot be performed safely, the operation must become explicit `RecoveryRequired`; it must not silently present as a clean cancellation.

### T3 — DB failure after move is recoverable

`ArchiveSafe_db_failure_after_move_preserves_recovery_evidence`

Inject a database failure after the move.

Prove either:

- safe compensation restores the source; or
- exact durable recovery evidence remains with source/destination/mod identity and an explicit recovery-required state.

Do not delete or overwrite an unexpected path merely to make the test green.

### T4 — restart reconciles a moved-but-uncommitted operation

`ArchiveSafe_restart_reconciles_moved_pending_operation`

Seed the durable state that would exist after a process dies following the move but before DB retirement.

A conservative restart policy should restore the source and leave the live DB entity intact when the expected source is absent and exact archive destination exists.

### T5 — ambiguous topology fails closed

`ArchiveSafe_recovery_refuses_when_source_and_archive_both_exist`

and

`ArchiveSafe_recovery_refuses_when_neither_expected_path_exists`

Recovery must not guess which directory is authoritative. Preserve evidence and require explicit recovery instead of destructive overwrite/delete.

### T6 — retry converges after recovered interruption

`ArchiveSafe_retry_after_recovery_can_archive_once`

After safe recovery, rerun cleanup and prove exactly one final archive publication / retirement occurs.

### T7 — cancellation before the first move remains cheap and side-effect free

`ArchiveSafe_cancel_before_move_changes_nothing`

This preserves the current good behavior before crossing the mutation boundary.

---

## Narrow implementation design

Do not “fix” this by reversing the order to DB-delete first. That simply moves the split-brain window: a DB commit followed by move failure/process death would retire current state while leaving the source folder in the catalog namespace.

The durable protocol should make the cross-domain workflow restartable.

### Recommended states

A dedicated cleanup operation record can remain narrow, for example:

- `Prepared`
- `Moved`
- `Committed`
- `RolledBack`
- `RecoveryRequired`

Required durable fields:

- operation ID;
- mod ID;
- exact original source path;
- exact chosen archive destination;
- created/updated timestamps;
- current state;
- optional last recovery error.

A dedicated table is preferable to casually overloading deployment journal semantics unless the existing journal is explicitly generalized with equivalent invariants.

### Recommended sequence

1. Revalidate candidate eligibility.
2. Choose the exact destination.
3. Persist `Prepared` **before** moving anything.
4. Perform the move.
5. Persist enough state to identify that the move occurred.
6. Perform the database retirement using the canonical retirement semantics.
7. Mark cleanup `Committed` in the same DB transaction as retirement when practical.
8. If ordinary cancellation/failure is observed after step 4 but before commit, perform guarded compensation with a recovery token that is not itself canceled.
9. If compensation is unsafe or fails, persist/retain `RecoveryRequired` and surface a precise recovery message.
10. Reconcile nonterminal cleanup records before selecting new cleanup candidates.

### Cancellation boundary

Cancellation may freely abort work before the first move.

After a move has succeeded, do **not** pass the canceled user token into the only bookkeeping/compensation that can make the operation safe. Record the cancellation intent, then complete the bounded compensation/recovery transition.

This is the critical test contract exposed by the current source.

### Recovery policy

For a nonterminal operation whose DB mod row still exists:

- source exists, archive destination absent: treat as pre-move/rolled-back and converge safely;
- source absent, exact archive destination exists: prefer restoring the source for an uncommitted/canceled cleanup, then roll back the operation;
- both exist: fail closed, do not overwrite either;
- neither exists: fail closed, evidence is insufficient;
- unexpected source/destination identity or type: fail closed.

If the final semantic-retirement transaction also marks the operation `Committed`, restart never has to infer whether DB retirement committed from filesystem state alone.

---

## Interaction with LR-007 mod-retirement semantics

This audit does **not** authorize retaining the existing raw `DELETE FROM mods` as the final lifecycle model.

The lifecycle audit established that retirement must also close live non-FK semantic references such as applicable conflict rules, resource-provider pins, and mod-scoped settings while preserving historical evidence.

Therefore the eventual cleanup workflow needs both:

1. **this audit's property:** crash/cancellation-safe coordination between filesystem archive movement and the DB transition; and
2. **LR-007's property:** complete semantic retirement inside the DB transition.

The two should compose, not replace one another.

A reasonable implementation order is:

- first add deterministic move/delete recovery characterization and the narrow operation protocol;
- then call the canonical centralized retirement boundary once that boundary exists, without redesigning identity or historical retention inside the recovery checkpoint.

Do not broaden this checkpoint into a global mod-ID migration or blanket FK cascade.

---

## Things deliberately not changed

This support branch does not change:

- `DuplicateCleanupService` production behavior;
- `ManagerDatabase` schema or transactions;
- mod retirement semantics;
- catalog identity;
- any `.verification` cache;
- build/release workflows;
- archive extraction;
- updater code;
- UI commands/bindings.

The goal is to leave exact, independently implementable evidence without colliding with active programmer/PR boundaries.

---

## Verification actually performed

Performed against GitHub canonical state:

- established canonical `main` at initial inspection and re-checked it after it advanced;
- reconciled the advance from `a8b581176aac0e6bcf09c049285ed40f4b2b392c` to task-lock base `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`;
- inspected the intervening changed-file set and confirmed it was archive-streaming provenance/continuity documentation, not duplicate-cleanup source;
- re-fetched duplicate-cleanup, UI, catalog, tests, and SQLite audit blobs at the new task-lock base;
- inspected current open PRs and branches to avoid duplicate ownership;
- inspected actual source bodies and actual existing test assertions;
- verified there was no open PR or matching branch for this duplicate-cleanup recovery boundary at branch creation time.

Not performed:

- no local working tree was available through this chat, so no local `git status`;
- no `dotnet build`;
- no `dotnet test`;
- no Windows runtime fault injection;
- no SQLite lock/cancellation reproduction;
- no hosted Release Gate;
- no function/stage verification-cache promotion.

This documentation-only audit therefore makes no new runtime/build verification claim.

---

## Parallel-agent integration notes

At task selection/reconciliation time, active PRs owned:

- profile-save transaction rollback;
- archive streaming failure-cleanup semantics;
- Agent Control v2 safety;
- updater publication verification;
- launch observation persistence;
- game-profile ID containment;
- updater publication;
- frontend UX.

A separate branch already indicated save-snapshot-prune integrity work.

This audit intentionally touches none of those implementation boundaries.

If another agent implements centralized mod retirement under LR-007, this recovery work should call that boundary rather than recreating semantic cleanup logic.

---

## Recommended independent future checkpoint

**Test-first duplicate-cleanup recovery implementation.**

Start with T2/T3/T4 fault fixtures and the smallest injectable seam needed to deterministically stop immediately after a successful move. Then add the durable operation protocol and guarded compensation/restart reconciliation. Keep semantic retirement delegated to LR-007's authority.

A production change is not closed until:

- focused recovery tests pass;
- whole-solution compile/analyzers pass;
- relevant Automation/Integration tests pass;
- the repository's exact Windows verification/release gate passes for the production SHA;
- continuity/evidence records are updated without manually promoting caches.

---

## Successor handoff

The successor must:

1. re-check canonical `main`, active PRs, and recent branches before implementation;
2. read this audit plus `SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` and `MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`;
3. keep D1 recovery and LR-007 semantic retirement as separate responsibilities;
4. add deterministic post-move cancellation/DB-failure/restart tests before changing production behavior;
5. never report a canceled cleanup as clean if the source has moved and durable rollback/recovery has not completed;
6. preserve exact verification honesty and never manually promote verification state;
7. preserve the permanent continuity constitution and active Learned Rules;
8. before finishing, explicitly require its own successor to inherit, preserve, and recursively propagate the same continuity constitution to the agent after them.

**Do not break the chain.**


---

## Late upstream reconciliation

Before PR creation, canonical `main` advanced from task-lock base `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d` to `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`.

The intervening changes update verification evidence/cache state and add the separate archive-streaming failure-cleanup audit, research note, and **LR-011 — cleanup must not replace primary failure or cancellation semantics**. They do not modify `DuplicateCleanupService`, its UI call sites, `CatalogService`, or the existing duplicate-analysis test inspected here.

LR-011 is compatible with and relevant to a future D1 implementation but does not subsume this audit:

- LR-011 governs preserving a primary cancellation/failure when subordinate cleanup itself fails;
- this audit governs ensuring a post-move cancellation/failure has a durable compensation/recovery path in the first place.

A future implementation should obey both: once a move has happened, attempt bounded compensation/recovery without allowing a secondary recovery exception to erase the primary cancellation/failure outcome.

No new Learned Rule is proposed by this branch, avoiding duplication and numbering collision.
