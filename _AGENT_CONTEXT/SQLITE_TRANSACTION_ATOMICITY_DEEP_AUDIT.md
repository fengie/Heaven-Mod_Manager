# Deep SQLite / transaction atomicity audit — 2026-09-27

## Scope and verification honesty

This is the independent follow-up requested by the Support Agent 1 SQLite/transaction assignment. It cross-checks the earlier `STORAGE_TRANSACTION_BOUNDARY_AUDIT.md` against canonical source rather than assuming that document is exhaustive.

Static audit base: `0e561f3c059475ad443a79ac4a27dd68264a7bdb` on canonical `main`.

During the audit, canonical `main` advanced through the PlannerSnapshotRepository extraction. The first candidate compile exposed two callers omitted from the earlier storage audit (`NexusMetadataService` and `GameBuildMonitor`); repair commit `528401925b1d09b3d65c9652de8e4f2024e3677f` migrated them. That repair changes read-side dependency injection only; the write-side transaction conclusions below are unchanged.

Verification performed here:
- static source inspection against canonical GitHub source;
- static test inspection;
- recent Git history/diff inspection;
- no local Windows execution from this support chat, because the connected Windows host was offline;
- no claim of hosted-Windows verification for this audit document;
- no production C# modified.

The permanent recursive continuity constitution remains in force. The successor must preserve it and explicitly require the agent after them to preserve it too.

---

## Executive classification

### Confirmed safe boundaries

1. `ManagerDatabase.InTransactionAsync` owns one connection + one transaction, commits once, and attempts rollback on failure.
2. `ReplaceModFilesAsync` atomically replaces one mod's file index together with blob registrations and armor-index rows.
3. `ChainManualFamilyAsync` keeps stale-rule removal, family/membership rewrite, cycle validation, and new overlay rules in one transaction.
4. `ProfileRepository.SaveCurrentAsync` atomically upserts the profile, replaces its rows, and snapshots current mod state.
5. `ReplaceSupersessionAsync`, `MarkRevalidationAsync`, `SetEnabledAndPriorityAsync`, `RecordResolverAuditAsync`, `ModTrustService.RecordLaunchAsync`, and the multi-row `ModIssueFallbackService` mutation batches are internally atomic.
6. `UnmanagedAdoptionService.RecordAdoptionAsync` atomically commits `adoption_runs` + `adopted_live_files`; its filesystem copy happens first and normal exceptions compensate by deleting the new source folder.
7. `GameBuildMonitor.CheckAsync` deliberately marks risky mods for revalidation before advancing the game-build fingerprint. A crash between those commits is fail-closed and retryable.
8. `BlobStore`/scanner CAS writes can precede DB registration without corrupting authoritative state because blobs are immutable/content-addressed and `ReplaceModFilesAsync` later registers the referenced hashes atomically.
9. Catalog/category/family-hint best-effort loops use individually idempotent/convergent writes. Partial progress is recoverable by rerunning them.

### Confirmed atomic multi-table operations

- Deployment prepared journal: `operations` + `operation_journal` + qualifying `original_files`.
- Deployment final commit: `deployment_manifest` + `original_files` + optional `mods` state + `operations=Committed`.
- Deployment rollback DB reconstruction: `deployment_manifest` + `original_files` + restored `mods` state + `operations=RolledBack`.
- Mod file replacement: `mod_files` + `blobs` + `mod_file_armor`.
- Manual family chain: `mods` + `mod_families` + `mod_family_members` + `conflict_rules`.
- Profile save: `profiles` + `profile_mods`.

### Actual defects / durable consistency gaps

**D1 — Duplicate archive move can strand a live DB row.**  
`DuplicateCleanupService.ArchiveSafeAsync` moves the source directory first, then independently executes `DELETE FROM mods`. If the process dies or the delete fails after `Directory.Move`, the source is safely present in the archive but the `mods` row still points to the now-missing original source path. On the next cleanup pass the code skips that row because `Directory.Exists(mod.SourcePath)` is false, so this does not self-heal.

**D2 — Launch trust and launch history are separate commits for one observation.**  
`AutomationCoordinator.LaunchAndObserveAsync` first commits `ModTrustService.RecordLaunchAsync`, then separately inserts `launch_history`. A failure between them increments trust/failure counters with no corresponding launch-history record. Those datasets are later consumed together by issue scoring/diagnosis, so this is a real consistency window rather than merely missing telemetry.

**D3 — Snapshot pruning deletes payload directories but not their DB rows.**  
`SaveBackupService.CreateAsync` inserts a successful `save_snapshots` row and then `Prune(30)` deletes old snapshot directories only. After enough snapshots, durable DB rows continue to advertise `root_path` values whose payload has been deleted. A last-known-good record may also retain a snapshot path that was later pruned, although current restore logic primarily uses saved mod state.

**D4 — Legacy migration run status can remain permanently stale after process death.**  
Migration completion is recorded in two independent writes: first `schema_info.legacy_migration_complete`, then `migration_runs.status='Complete'`. A crash between them causes the next startup to trust the completion marker and skip migration, leaving that run permanently marked `Running`. An abrupt crash earlier in migration similarly leaves a `Running` row because `ResetIncompleteImportAsync` does not mark abandoned runs.

### Recoverability gaps / non-destructive crash litter

**R1 — Adoption can leave an orphan imported source folder on abrupt process death.**  
Normal exceptions clean the copied folder, but process death after files are copied and before `RecordAdoptionAsync` commits bypasses compensation. Restart sees the live files as unmanaged and can adopt them again into a new folder; the old folder remains orphaned.

**R2 — Save backup can leave an unindexed snapshot directory.**  
Filesystem payload and `snapshot.json` are written before the `save_snapshots` insert. A DB failure/process death after the disk write leaves an orphan directory. Later pruning may eventually remove it, but no immediate reconciliation exists.

**R3 — CAS capture can leave unregistered blobs.**  
A process can die after a blob is durably moved into the CAS but before DB registration / `ReplaceModFilesAsync`. This is non-corrupting because the blob is immutable and reusable, but it can leak disk space indefinitely without a CAS garbage collector.

### Uncertain behavior that needs explicit tests/architecture decisions

**U1 — PlannerSnapshotRepository preserves a two-connection, non-transactional read model.**  
This was intentionally preserved during extraction. `GetModsAsync` reads on one connection and the remaining planner inputs are read on another. A concurrent writer can therefore produce a mixed-time snapshot. Do not silently “fix” this inside a repository extraction; decide the desired snapshot/isolation semantics first, then test them.

**U2 — DeploymentExecutor has no internal single-writer gate.**  
Current UI orchestration appears to serialize major mutations through busy-operation flows, but `DeploymentExecutor` itself does not prevent two callers from entering `ApplyAsync` concurrently. WAL/busy_timeout only serialize SQLite writes; they do not serialize filesystem mutation or journal meaning. Treat application-level single-writer operation ownership as an invariant until a concurrency test proves otherwise.

**U3 — SaveBackupService gathers one logical snapshot using several independent reads.**  
Mod state, game-build state, and deployment manifest are read separately, then written to disk/DB. Concurrent mutation can produce a mixed-time backup. Current UI sequencing may make this unlikely, but the service itself does not enforce a read snapshot.

---

## SQLite connection / WAL model

`ManagerDatabase` is connection-per-operation:
- `SqliteOpenMode.ReadWriteCreate`;
- default SQLite cache (not shared-cache);
- pooling enabled;
- 10-second default/busy timeout;
- `journal_mode=WAL`;
- `synchronous=FULL`;
- foreign keys enabled per connection;
- WAL autocheckpoint configured.

This is suitable for parallel readers and bounded writer contention, but it does **not** make higher-level multi-call workflows atomic. A method that must join an existing logical transaction must receive the caller-owned `SqliteConnection` and `SqliteTransaction`; opening a fresh connection creates a separate commit boundary.

---

## Write-path inventory

| Logical mutation | Entry / owner | Tables | DB boundary | Filesystem ordering / recovery | Extraction rule |
|---|---|---|---|---|---|
| schema initialize | `ManagerDatabase.InitializeAsync` | schema + `schema_info` + possible `mod_file_armor` rebuild | schema statements outside one app TX; armor rebuild has explicit TX | none | Keep schema/version/rebuild ordering together. CREATE IF NOT EXISTS is restart-friendly. |
| enabled + priority batch | `SetEnabledAndPriorityAsync` | `mods` | explicit TX | none | Batch is atomic; future repository must keep batch transaction. |
| mod upsert | `UpsertModAsync` | `mods` | single autocommit statement | catalog source folder already exists | Safe standalone command. |
| replace mod files | `ReplaceModFilesAsync` | `mod_files`, `blobs`, `mod_file_armor` | one explicit TX | CAS bytes captured before call | **Do not split.** All three tables belong to one aggregate. |
| set one family id | `SetFamilyIdAsync` | `mods` | one autocommit update | none | Standalone only; do not substitute for manual-family transaction. |
| manual family chain | `ChainManualFamilyAsync` | `mods`, `mod_families`, `mod_family_members`, `conflict_rules` | one explicit TX | none | **Do not split.** Repository helpers must accept shared connection/TX. |
| provenance upsert | `UpsertProvenanceAsync` | `mod_provenance` | one autocommit upsert | metadata/network work precedes | Independent per-mod enrichment is acceptable. |
| supersession replacement | `ReplaceSupersessionAsync` | `mod_supersession` | one explicit TX | none | Delete+rebuild must remain one TX. |
| resolver audit batch | `RecordResolverAuditAsync` | `resolver_audit` | one explicit TX | written before deployment apply | Atomic batch, but it is decision/audit history, not proof deployment committed. |
| game-build fingerprint | `SetGameBuildFingerprintAsync` | `game_build_state` | one autocommit upsert | executable hashed first | In changed-build flow keep it **after** revalidation marking. |
| revalidation batch | `MarkRevalidationAsync` | `mod_revalidation` | one explicit TX | none | Atomic batch. |
| settings | `SetSettingAsync` | `settings` | one autocommit upsert | varies by caller | Do not assume multiple setting writes form one unit. |
| profile save | `ProfileRepository.SaveCurrentAsync` | `profiles`, `profile_mods` | one explicit TX | none | **Do not split.** |
| deployment prepare | `DeploymentExecutor.WritePreparedJournalAsync` | `operations`, `operation_journal`, qualifying `original_files` | one `InTransactionAsync` | before first live mutation | **Critical shared TX.** |
| deployment phase markers | `SetOperationStateAsync`, `SetJournalStatusAsync` | `operations`, `operation_journal` | individual autocommits | interleaved with filesystem | Intentionally durable breadcrumbs; recovery semantics depend on this. |
| deployment final state | `CommitAfterStateAsync` | `deployment_manifest`, `original_files`, optional `mods`, `operations` | one `InTransactionAsync` | after all live-file writes | **Critical shared TX.** |
| deployment rollback DB state | `RollbackAsync` final DB phase | `deployment_manifest`, `original_files`, optional `mods`, `operations` | one `InTransactionAsync` | after per-file before-image restoration | **Critical shared TX.** |
| adoption record | `UnmanagedAdoptionService.RecordAdoptionAsync` | `adoption_runs`, `adopted_live_files` | one explicit TX | source-package copy first; normal failure deletes folder | Keep both tables together; add crash reconciliation before extraction. |
| trust launch batch | `ModTrustService.RecordLaunchAsync` | `mod_trust` | one explicit TX | after observed process result | Batch itself safe; cross-call launch aggregate is not atomic. |
| issue suspect batch | `ModIssueFallbackService` | `mod_issue_suspects` | explicit TX for bisection/success/failure batches; Clear is one update | timeline write follows separately | Keep suspect batches atomic; timeline is advisory. |
| automation timeline | `ChangeTimelineService.RecordAsync` | `automation_events` | one autocommit insert | follows domain action | Advisory; should not be pulled into domain TX. |
| launch history | `AutomationCoordinator.RecordLaunchAsync` | `launch_history` | one autocommit insert | follows trust update | See D2. |
| save snapshot index | `SaveBackupService.RecordSnapshotAsync` | `save_snapshots` | one autocommit insert | save copy + snapshot.json written first | See D3/R2/U3. |
| auto category | `AutoCategoryService.AssignMissingAsync` | `mods.category` | one autocommit update per mod | source enumeration may precede | Partial progress is idempotent/convergent. |
| duplicate archive | `DuplicateCleanupService.ArchiveSafeAsync` | `mods` delete (+ FK cascades) | one autocommit delete per candidate | **Directory.Move first** | See D1; requires compensation/recovery. |
| blob registration | `BlobStore.CaptureWithHashAsync` | `blobs` | one autocommit insert | CAS file publish first | Orphan CAS is safe but needs GC. |
| scanner capture | `ModScanner.CaptureAsync` -> `ReplaceModFilesAsync` | `blobs`, `mod_files`, `mod_file_armor` | final DB aggregate one TX | CAS captures first | Safe because CAS is immutable; do not split final TX. |
| catalog refresh | `CatalogService.RefreshFoldersAsync` | `mods` | one upsert per new folder | folder already exists | Partial run converges by stable source path/id. |
| Nexus family hints | `NexusMetadataService` -> `SetFamilyIdAsync` | `mods.family_id` | per-member autocommit | local/network metadata before | Partial run converges on retry; manual assignments preserved. |
| Nexus supersession | `NexusMetadataService` -> `ReplaceSupersessionAsync` | `mod_supersession` | one explicit TX | enrichment reads first | Safe batch. |
| diagnostics | `DiagnosticTelemetry` | `diagnostics`, `error_reports` | separate single inserts | after/beside operation | Non-authoritative best effort by design. |
| legacy migration | `LegacyV7Migrator` | many import tables + `migration_runs` + completion marker | many independent commits; some inner aggregate TXs | backup + CAS copies interleaved | Restart protocol, not one atomic migration; see migration section. |

---

## DeploymentExecutor protocol — independently verified

### 1. Prepared journal transaction

Before live mutation:
1. `ApplyAsync` calls `RecoverIncompleteAsync`.
2. Current mod state is read.
3. `PrepareChangesAsync` captures pre-existing unmanaged bytes into the CAS when needed.
4. `WritePreparedJournalAsync` uses one `InTransactionAsync` to:
   - insert `operations(... state='Prepared' ...)`;
   - insert every `operation_journal` row with `status='Pending'`;
   - persist qualifying original baselines in `original_files`.
5. Only after that commit is `journalDurable=true`.

Crash before journal durability returns/fails without claiming any live transaction began.

### 2. Filesystem mutation phase

After prepared journal durability:
- the whole plan is preflight-verified;
- known Windows file locks are checked;
- operation state becomes `Applying`;
- each file is re-verified immediately before mutation;
- journal row becomes `Writing`;
- remove or atomic restore occurs;
- journal row becomes `Applied`.

Journal-status/state writes are intentionally separate autocommits so restart can infer progress even though SQLite cannot participate in the filesystem transaction.

### 3. Per-file TOCTOU behavior

`VerifyAllPreconditionsAsync` prevents entering mutation with a known stale later path. `VerifyPreconditionAsync` then repeats immediately before each individual write. Recovery refuses to overwrite a file whose live hash matches neither the durable before-image nor after-image.

### 4. Final durable DB commit

After all live writes:
- state marker: `FilesWritten`;
- state marker: `StateCommitting`;
- one transaction updates:
  - `deployment_manifest`;
  - `original_files`;
  - optionally all target `mods.enabled/priority`;
  - `operations.state='Committed'` with commit timestamp.

This is the durable commit point. A SQLite failure before commit leaves the operation recoverable to the before-image. A process death after commit leaves the committed state authoritative.

### 5. Rollback DB commit

Rollback first restores filesystem rows in reverse journal order and durably marks each row `RolledBack`. Then one transaction reconstructs:
- manifest ownership;
- original-baseline ownership;
- prior mod enabled/priority state;
- operation state `RolledBack`.

If rollback itself cannot complete, operation state becomes `RecoveryRequired`.

### 6. Interrupted rollback/recovery

Startup scans `Prepared`, `Applying`, `FilesWritten`, `StateCommitting`, `RollingBack`, and `RecoveryRequired` operations and runs the same rollback logic. Recovery is fail-closed if a live file matches neither known before nor after hash.

### 7. Undo

Undo reads the last committed eligible operation + journal, builds an inverse plan, and re-enters ordinary `ApplyAsync`. The inverse therefore receives the same precondition, journal, crash, rollback, and final-commit protections.

### Existing deployment coverage is strong

Static test inspection confirms explicit crash coverage for:
- after journal;
- before first mutation;
- after a file write;
- after all files written;
- before final DB commit;
- after durable DB commit;
- before cleanup;
- interrupted rollback followed by restart;
- whole-plan stale preflight;
- external-change refusal.

This is substantially stronger than the non-deployment maintenance workflows.

---

## Other critical aggregates

### ReplaceModFilesAsync

Order inside one transaction:
1. delete previous `mod_files` for the mod (armor rows cascade);
2. for each supplied file, `INSERT OR IGNORE blobs`;
3. insert `mod_files`;
4. insert derived `mod_file_armor` when applicable;
5. commit once.

A future BlobRepository/FileIndexRepository split must not independently commit steps 2–4.

### ChainManualFamilyAsync

The same transaction:
- reads existing family/roles;
- removes stale pair relationships between the newly chained groups;
- upserts the manual family;
- rewrites memberships + `mods.family_id`;
- builds/validates the rule graph for cycles;
- inserts full ordered overlay edges;
- commits once.

Cycle failure rolls back all preceding changes in that transaction.

### ProfileRepository.SaveCurrentAsync

The same transaction:
- upserts profile by case-insensitive name and obtains its id;
- deletes existing `profile_mods`;
- inserts the current `mods.enabled/priority` snapshot;
- commits.

A future profile repository should keep all three statements under one owner.

### Adoption

Filesystem copy is deliberately before DB ownership. The DB record itself is atomic across `adoption_runs` and `adopted_live_files`. Normal exceptions delete the copied source package. Abrupt process death is the unhandled R1 window.

### Trust / issue state

`ModTrustService.RecordLaunchAsync` is an atomic batch across all distinct enabled mods. `ModIssueFallbackService` uses explicit transactions for multi-suspect upserts and successful-launch clearing. Their later timeline events are intentionally separate, but launch-history coupling is currently split (D2).

### Supersession / revalidation / enabled-priority

Each batch is atomic. Of particular importance, game-build change handling performs revalidation marking **before** fingerprint advancement, making the cross-call sequence retry-safe despite not being one transaction.

### Legacy migration/reset

The migration is a restart protocol, not a single transaction:
- incomplete v8 import state is reset before retry;
- legacy state remains authoritative until `legacy_migration_complete` exists;
- mod/file sub-aggregates reuse their own atomic helpers;
- CAS blobs are copied/hardlinked before/alongside DB rows;
- referenced blobs and enabled order are verified before completion marker;
- on normal exception, current run is marked Failed and partial import rows are reset.

Important limitations:
- `ResetIncompleteImportAsync` is a multi-statement command with no explicit application transaction. Process death can leave a partially reset DB, but deletes are idempotent and the next startup retries reset before importing.
- abrupt death leaves the current `migration_runs` row `Running`;
- completion marker + run-status completion are independent commits (D4).

No production refactor is warranted from this audit alone; add migration interruption tests before redesigning this protocol.

---

## Filesystem-before/after-DB windows

### Intentionally safe / self-healing

- CAS file publish -> DB blob/index registration: orphan immutable blob only; retry reuses bytes.
- archive import destination publish -> catalog upsert: next catalog refresh can discover an unregistered source folder.
- smart-inbox destination publish/source move -> later catalog/capture: destination persists and next catalog refresh can recover it.
- game-build revalidation -> fingerprint: conservative duplicate work on retry.
- Nexus per-mod provenance/family enrichment: partial work reruns and converges.

### Requires repair or explicit recovery

- duplicate cleanup: archive move -> DB delete (D1);
- adoption: source-package copy -> DB adoption record under abrupt death (R1);
- save backup: disk snapshot -> DB row under failure (R2);
- launch trust batch -> launch-history row (D2);
- migration completion marker -> run Complete status (D4).

---

## Read/write races and stale-data assumptions

1. **Planner snapshot mixed-time reads.** Preserved historical behavior; two connections, no encompassing snapshot transaction.
2. **Backup mixed-time reads.** Mods, build fingerprint, and manifest are separate reads.
3. **Nexus refresh.** Repeated GetMods reads intentionally observe progressively enriched state. Family/supersession logic is therefore a convergent workflow, not a snapshot.
4. **Duplicate analysis vs cleanup.** Analysis and later cleanup re-read mods, but source filesystem can still change between selection and move.
5. **Deployment concurrent callers.** No internal async mutex; correctness currently depends on higher-level operation serialization.
6. **SQLite busy timeout is not a domain lock.** A repository extraction must not treat WAL/10s timeout as equivalent to logical single-writer ownership.

---

## Dangerous write extraction candidates

Do not give the following future repositories permission to open their own connection when invoked inside the owning operation:

- deployment journal repository during prepared/final/rollback phases;
- deployment manifest/original baseline repository during final/rollback commit;
- mod-state repository when `DeploymentExecutor` is committing target/restored state;
- mod file/blob/armor repositories inside `ReplaceModFilesAsync`;
- family/member/rule repositories inside `ChainManualFamilyAsync`;
- profile/profile-mod repositories inside `SaveCurrentAsync`;
- adoption run/file repositories inside `RecordAdoptionAsync`;
- trust/suspect repositories for their existing batch methods.

Required pattern: caller owns `SqliteConnection` + `SqliteTransaction`; inner helpers accept both and never commit.

---

## Safe read-only extraction candidates

Read-only repositories remain the lowest-risk seams, provided current consistency semantics are preserved intentionally:
- presentation/activity reads;
- profile list/load reads;
- provenance/supersession query projections;
- diagnostic/history readers;
- catalog file-index reads.

`PlannerSnapshotRepository` is already extracted, but its historical two-connection semantics must not be accidentally advertised as a consistent SQLite snapshot.

---

## Detailed defect records

### D1 — DuplicateCleanupService move-before-delete

**Source:** `src/MhwModManager.Automation/DuplicateCleanupService.cs`, `ArchiveSafeAsync`.

**Failure condition:** candidate is disabled and source directory exists; `Directory.Move(mod.SourcePath,dest)` succeeds; process dies or DB delete throws before `DELETE FROM mods WHERE id=$m` commits.

**Result/risk:** payload survives in archive, but DB retains the mod row pointing to a missing original source. Future `ArchiveSafeAsync` skips it because the original source directory no longer exists. FK-dependent rows also remain because the delete never happened.

**Suggested fix:** at minimum compensate normal DB failure by moving the directory back when safe. For crash durability, persist a cleanup operation/journal before the move or make startup reconcile archive moves against stale `mods.source_path` entries.

**Suggested tests:**
- injected failure immediately after move, assert normal failure restores original path;
- simulated process death after move, restart reconciliation deletes/tombstones the DB row or completes the archive transition;
- ensure no archive data is destroyed during reconciliation.

### D2 — Trust/history split launch observation

**Source:** `src/MhwModManager.Automation/AutomationCoordinator.cs`, `LaunchAndObserveAsync`; `ModTrustService.RecordLaunchAsync`.

**Failure condition:** process result has been observed; trust batch commits; launch-history insert then fails or process dies.

**Result/risk:** `mod_trust` counts a launch that `launch_history` does not contain. Issue diagnosis uses both datasets, so scores/history can disagree. Retrying the trust write would double-count because there is no launch-id idempotency key in `mod_trust`.

**Suggested fix:** make one storage owner persist the launch observation + trust deltas in one transaction, or introduce a launch-id keyed observation ledger and derive/idempotently apply trust from it. Keep timeline emission outside the authoritative transaction.

**Suggested tests:**
- fault after trust commit before history insert;
- retry does not double-count;
- failure diagnosis can always resolve the launch id it was asked to analyze.

### D3 — Save snapshot payload/DB prune drift

**Source:** `src/MhwModManager.Automation/SaveBackupService.cs`, `CreateAsync` + `Prune`.

**Failure condition:** create more than 30 snapshots.

**Result/risk:** `Prune(30)` deletes old directories but leaves corresponding successful `save_snapshots` rows. DB metadata no longer proves payload availability.

**Suggested fix:** prune DB rows in the same lifecycle operation, or explicitly retain historical rows with a `payload_pruned`/availability state. Do not delete the last-known-good payload without deciding that policy explicitly.

**Suggested test:** create 31+ snapshots; assert every non-pruned successful DB row has an existing root, and every deleted payload is either removed from DB or marked unavailable.

### D4 — Legacy migration stale run status

**Source:** `src/MhwModManager.Storage/LegacyV7Migrator.cs`, completion writes and `ResetIncompleteImportAsync`.

**Failure condition A:** process dies after `legacy_migration_complete` is written but before current `migration_runs` row becomes Complete.

**Result A:** next startup skips migration because completion marker exists; run remains `Running` permanently.

**Failure condition B:** abrupt death during import before catch executes.

**Result B:** next startup resets import rows and retries, but the previous `migration_runs` row remains `Running`.

**Suggested fix:** commit completion marker + run Complete status together; on startup mark older incomplete migration runs Abandoned/Interrupted before retry.

**Suggested tests:** fault at each migration phase, especially before/after completion marker, then restart and assert exactly one authoritative completion and no stale Running run.

---

## Missing regression / fault-injection coverage

High priority:
1. DuplicateCleanup after filesystem move and before DB delete.
2. Launch trust commit vs launch-history insert.
3. SaveBackup disk-write/DB-insert and prune metadata lifecycle.
4. Legacy migration crash at reset, mid-import, after verification, after completion marker.
5. Adoption crash after copy before DB commit.
6. Concurrent `DeploymentExecutor.ApplyAsync` calls on overlapping paths.
7. PlannerSnapshotRepository read while a writer changes mod/file/rule/manifest state between its two connections.
8. Profile save cancellation/constraint failure mid-replacement.
9. ReplaceModFiles cancellation/constraint failure after delete but before all inserts (should prove transaction rollback).
10. Manual family chain failure after membership rewrite but before rule insertion (should prove transaction rollback).

Existing coverage already exercises deployment crash windows comprehensively and should remain the model for these workflows.

---

## Recommended future architecture seams

1. **Operation-level storage commands, not table repositories.** Prefer names such as `SaveProfileSnapshot`, `ReplaceModFileIndex`, `CommitDeploymentAfterState`, `PersistLaunchObservation`. They encode the true atomic aggregate.
2. **Transaction context for nested write helpers.** A small internal command/context object carrying `SqliteConnection` + `SqliteTransaction` is safer than letting nested repositories self-open.
3. **Durable filesystem workflow journals** for duplicate cleanup/adoption if those flows become more destructive or automated.
4. **Explicit snapshot consistency contracts** for planner/backup read models rather than accidental semantics.
5. **Single-writer mutation coordinator** if deployment/maintenance can ever run from multiple callers concurrently.
6. **Lifecycle-aware history tables** so filesystem payload pruning and DB history cannot silently disagree.

---

## Successor handoff

- Preserve the permanent recursive continuity constitution and require the next successor to do the same.
- Do not reinterpret this static audit as hosted Windows verification.
- Do not broaden the current PlannerSnapshotRepository production boundary while its exact Windows gate is unresolved.
- The first production fixes worth scheduling from this audit are D1 (duplicate cleanup recovery), D2 (launch observation atomicity/idempotency), D3 (snapshot prune lifecycle), and D4 (migration status closure), each as a separate checkpoint with focused fault injection.
- Before any write-side repository extraction, reread this document and `STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`; if the helper participates in a listed aggregate transaction, it must share the caller-owned connection/transaction.
- Do not break the chain.

---

## 2026-09-28 specialized D1 follow-up — duplicate cleanup cancellation/recovery

`_AGENT_CONTEXT/DUPLICATE_CLEANUP_RECOVERY_AUDIT_2026-09-28.md` specializes D1 against canonical task-lock base `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`. It confirms from current control flow that the move→delete split is reachable not only through process death/DB failure but also through ordinary user cancellation: both Cleanup and Inbox expose cancellation, while `ArchiveSafeAsync` passes the same token into the DB delete after `Directory.Move` has already succeeded. The follow-up defines deterministic post-move cancellation/DB-failure/restart fixtures and a narrow recoverable operation protocol. It does not change production code and does not replace LR-007 semantic-retirement work.

The successor implementing D1 must preserve this audit's transaction/recovery authority, compose with the canonical retirement boundary rather than duplicating it, and recursively pass the permanent continuity constitution to the agent after them.
