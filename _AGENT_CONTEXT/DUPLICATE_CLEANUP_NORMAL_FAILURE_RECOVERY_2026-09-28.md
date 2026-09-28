# Duplicate cleanup normal-failure recovery checkpoint — 2026-09-28

## Status

**Narrow production + regression checkpoint. Not crash-durable closure.**

Canonical repository: `fengie/mhw-mods`  
Canonical `main` inspected immediately before branch creation: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`  
Support branch: `agent/duplicate-cleanup-normal-failure-recovery-20260928`

This checkpoint implements only the minimum normal-failure compensation for D1 in
`_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`.

It deliberately does **not** claim to close process-death / power-loss recovery, and it
does not replace the separate mod-retirement semantic work in
`_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`.

## Why this task was selected

At selection time, active work already owned:

- updater publication verification;
- Agent Control v2;
- launch-observation atomicity;
- persisted game-profile ID containment;
- frontend UX.

The branch inventory had no duplicate-cleanup implementation branch and no open
duplicate-cleanup PR. The existing SQLite audit had already confirmed the defect, so
another documentation-only rediscovery would have duplicated prior work.

D1 is a high-value independent destructive-operation boundary:

1. `DuplicateCleanupService.ArchiveSafeAsync` moves a disabled duplicate/superseded
   source directory to the archive;
2. it then independently deletes the `mods` row;
3. if the delete fails after the move, the row can remain while its original source path
   no longer exists;
4. the next cleanup pass skips that row because the original source directory is absent.

## Internal assignment

Scope only the ordinary exception/cancellation window after a successful archive move
and before/while the mod-row delete is persisted.

Required behavior:

- never destroy the archived payload while persistence state is uncertain;
- after a delete exception, re-read authoritative DB state without using the already
  cancelled caller token;
- restore the moved source only when the row is proven still present;
- never overwrite a newly recreated source path;
- never assume a missing archive path is safe;
- preserve the original delete exception when compensation succeeds;
- surface both errors if compensation itself cannot be completed safely;
- add a deterministic regression without adding a production-only test hook.

Exclusions:

- no durable cleanup journal;
- no startup reconciliation after abrupt process death;
- no mod semantic-retirement redesign;
- no conflict-rule/resource-provider cleanup;
- no deployment/CAS/updater/UI changes;
- no archive reparse/topology redesign;
- no verification-cache changes.

## Implementation

### `DuplicateCleanupService.ArchiveSafeAsync`

The existing filesystem-first ordering is preserved.

After `Directory.Move(source, archive)`, the DB delete now runs inside an exception
boundary.

If the delete throws:

1. a fresh database connection is opened with `CancellationToken.None`;
2. `SELECT EXISTS(SELECT 1 FROM mods WHERE id=$m)` checks the durable mod-row state;
3. if the row is absent, the archive move is left in place because the delete may have
   committed despite the observed error;
4. if the row is present, compensation restores the archived directory to the original
   source path only when the original path is absent and the archive path still exists;
5. if DB state cannot be read, the source path already exists, the archive path vanished,
   or the restore move fails, compensation fails closed and both the delete and recovery
   errors are surfaced.

The caller's original delete exception is rethrown after successful compensation.

This is intentionally conservative around ambiguous external-state failures: it does not
guess that an exception means the database was unchanged.

### Regression

`DuplicateArchiveDeleteFailureRestoresSourceAndKeepsRow` uses a real SQLite
`BEFORE DELETE ON mods` trigger with `RAISE(ABORT,...)` for the chosen duplicate.

The fixture proves the concrete normal-failure postcondition:

- the delete fails after the source directory has already been moved;
- the original source directory is restored;
- a marker file remains intact;
- the archive contains no stranded moved directory;
- the mod row remains present;
- the recommended keeper remains untouched.

No production test seam was added.

## Existing strengths preserved

- exact-duplicate selection and keeper ordering are unchanged;
- only disabled candidates are archived;
- successful DB deletion still uses existing FK behavior;
- archive payload is retained rather than deleted on uncertainty;
- caller cancellation can still cancel the operation, but recovery after an already
  completed move uses a non-cancelable bounded SQLite read/restore attempt;
- the new private production body includes the required method-entry tracing.

## Residual risks / deliberately open work

### P1 — abrupt process death remains open

A process can still die after `Directory.Move` and before the DB delete or before the
exception compensation runs. This checkpoint adds no durable intent record and no startup
reconciliation.

The crash-durable closure remains one of:

- persist a cleanup operation/journal before the move and reconcile on startup; or
- implement a narrowly specified startup reconciliation that can prove ownership of the
  archived directory and stale mod row before completing/rolling back the transition.

Do not call D1 fully closed until that behavior is implemented and restart-tested.

### P1 — successful retirement semantic references remain separate

The existing mod-lifecycle audit documents planner-active and mod-scoped semantic state
that can survive a successful raw `DELETE FROM mods`.

This checkpoint intentionally does not solve that lifecycle boundary. The successful
cleanup path has unchanged retirement semantics.

### P2 — filesystem topology / concurrent recreation

The compensation refuses to overwrite an original source path that appears after the
archive move. It also refuses to invent recovery when the archive path disappears.

Physical link/reparse containment and cross-process mutation serialization are separate
boundaries and are not claimed by this checkpoint.

## Verification actually performed

In this GitHub-connected support session:

- re-read live canonical `main` and recent history immediately before branch creation;
- re-read all current open PRs and support branches relevant to ownership;
- confirmed there was no active duplicate-cleanup implementation lane;
- read the actual DuplicateCleanup source, its UI callers, existing duplicate-analysis
  test, the deep SQLite audit, the mod-lifecycle audit, and destructive-operation /
  verification / multi-agent doctrine;
- created the isolated branch from exact main
  `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`;
- added production commit `878f19c8e0c5f0c89b342f260ff8b9364c44269f`;
- added regression commit `02acc270db67e9588fa8675ededdef987fc27f61`;
- re-read the changed production and regression bodies from the pushed branch;
- compared branch to its exact base after those commits: **2 commits ahead, 0 behind**,
  changing only DuplicateCleanup source and its Automation test at that checkpoint.

## Not verified in this session

No local/Windows execution environment was used after Work mode was declined.

Therefore this checkpoint does **not** claim:

- `dotnet test` passed;
- the new SQLite-trigger regression executed;
- compilation/analyzers passed;
- `Verify-Release.ps1` passed;
- `Build-Release.ps1` passed;
- hosted Windows Release Gate passed;
- abrupt-process-death recovery works.

Required focused verification on Windows:

`dotnet test tests/MhwModManager.AutomationTests/MhwModManager.AutomationTests.csproj -c Release --filter "FullyQualifiedName~DuplicateArchiveDeleteFailureRestoresSourceAndKeepsRow"`

Then run the repository-native full exact-source verification/release gates before merge.

## Learned-rule review

No new Learned Rule is added.

The general lesson is already represented by the permanent destructive-operation
doctrine and the existing SQLite D1 audit: filesystem + database mutation is not one
atomic operation, and failure must be reconciled from actual postconditions rather than
guessed from an exception.

## Parallel-agent integration notes

Keep these active boundaries separate:

- updater publication verification PR;
- Agent Control v2 implementation/audit;
- launch-observation persistence atomicity;
- persisted game-profile ID containment;
- frontend UX.

This branch changes only:

- `src/MhwModManager.Automation/DuplicateCleanupService.cs`;
- `tests/MhwModManager.AutomationTests/AutomationServiceTests.cs`;
- this checkpoint document.

It intentionally avoids shared global handoff/index files because active support PRs are
currently editing those files. The PR and this durable artifact should be integrated
without overwriting newer canonical continuity snapshots. After merge, the integration
agent should add a current-state/index link if it can do so without discarding newer
parallel handoff changes.

## Recommended next independent checkpoint

After this branch compiles and its focused/full Windows gates pass, implement the
**crash-durable duplicate-cleanup reconciliation** as a separate checkpoint:

1. define durable ownership/intent before the archive move;
2. kill/interupt after move and before DB retirement;
3. restart from the durable state;
4. reconcile without deleting archive payload on ambiguity;
5. prove retry/idempotency;
6. only then describe the move-before-delete D1 boundary as crash-durably closed.

Keep the separate mod-retirement semantic-reference repair independent.

## Successor handoff

Start from current canonical `main`, not the base SHA above if main advances. Re-read
`AGENTS.md`, the permanent continuity constitution, active Learned Rules, the deep
SQLite audit, the mod-lifecycle audit, this checkpoint, and live PR ownership before any
edit.

Preserve exact verification provenance. Do not promote this static/pushed checkpoint to a
tested or crash-durable claim until the required evidence exists.

The successor must preserve the permanent continuity constitution and explicitly require
their successor to inherit, preserve, and recursively propagate the same system again.

**Do not break the chain.**


## Final canonical-main revalidation before PR

Canonical `main` advanced once during this checkpoint, from
`3d24155823b278662cc2aa9ecf9f1bb1a4d7353d` to
`3d625dfbda54b34848f4da7369c02f065bf00f09`.

The intervening commit is `Persist hosted Windows verification evidence [skip ci]`.
Its diff updates verification cache/evidence for exact source
`3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`; it does not modify DuplicateCleanup,
Automation tests, storage behavior, or the findings/repair in this checkpoint.

The branch is therefore intentionally left one evidence-only commit behind rather than
copying or promoting canonical verification evidence onto changed production inputs.
The new DuplicateCleanup source/test fingerprints require their own fresh verification.
