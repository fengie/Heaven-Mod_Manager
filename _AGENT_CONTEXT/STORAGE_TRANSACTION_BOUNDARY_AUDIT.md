# ManagerDatabase transaction-boundary audit — 2026-09-27

## Decision

This checkpoint is a storage architecture audit, not a database refactor.

The current code has a connection-per-operation ManagerDatabase facade plus several domain services/repositories that legitimately own their own short SQLite transactions. The safe decomposition strategy is therefore:

1. preserve current transaction owners;
2. move read/query assembly before write paths;
3. never make a repository commit independently when its caller currently supplies one SqliteConnection + one SqliteTransaction;
4. take one storage extraction at a time and independently verify it.

Exactly one first extraction is recommended at the end of this document. It is not implemented in this checkpoint.

## Connection lifecycle

ManagerDatabase builds Microsoft.Data.Sqlite connections with:

- ReadWriteCreate mode;
- default SQLite cache (shared-cache intentionally disabled);
- pooling enabled;
- ten-second default/busy timeout.

OpenAsync opens a new connection and reapplies foreign_keys=ON and busy_timeout=10000. Most methods therefore own a short connection per call.

InitializeAsync uses one raw connection to establish WAL, synchronous=FULL, foreign keys, busy timeout, WAL checkpoint/journal limits and Schema.Sql. Schema/version ownership remains ManagerDatabase + Schema and should not be distributed into repositories during early extraction.

InTransactionAsync is the generic shared-transaction primitive. It owns one connection and one SqliteTransaction, passes both to its callback, commits once, and explicitly attempts rollback on failure.

## Critical invariant

If a logical operation currently uses one SqliteConnection and one SqliteTransaction, every statement in that operation must keep participating in that same transaction after extraction.

A repository used inside such a boundary must accept the shared transaction context (at minimum SqliteConnection + SqliteTransaction) or expose transaction-aware internal methods. It must not silently open another connection or commit its own transaction.

## ManagerDatabase operation map

| Method | Domain | R/W | Tables / state touched | Connection / transaction ownership | Principal callers | Atomicity / extraction assessment |
|---|---|---|---|---|---|---|
| InitializeAsync | schema/bootstrap | RW | schema DDL, schema_info; may rebuild mod_file_armor from mod_files | one raw connection; armor rebuild has its own explicit transaction | App startup | Keep with schema/bootstrap. Not a first repository seam. |
| OpenAsync | infrastructure | connection | PRAGMAs | returns caller-owned connection | repositories and many services | Infrastructure primitive. Preserve until DB access is narrowed. |
| RebuildArmorIndexAsync | catalog index maintenance | RW | mod_file_armor, mod_files | explicit transaction on one connection | public maintenance path / initialization helper | Delete + rebuild must stay one transaction. |
| GetModsAsync | mod catalog read model | R | mods, mod_provenance, mod_supersession, mod_family_members, settings preview, mod_revalidation | own connection, no explicit transaction | MainWindow, Catalog, Nexus, automation, diagnostics, deployment state reads | Cohesive read projection but high fan-out. Good later CatalogReadRepository candidate, not the first recommended extraction. |
| SetEnabledAndPriorityAsync | mod state | W | mods | explicit bulk transaction | LastKnownGoodService | Bulk state update must stay atomic. Do not split per mod. |
| UpsertModAsync | catalog | W | mods | own connection, single statement | CatalogService, tests | Easy command seam later, but write-side extraction is lower priority than queries. |
| ReplaceModFilesAsync | catalog capture / CAS index | W | mod_files, blobs, mod_file_armor | one explicit transaction | ModScanner, tests | Critical atomic unit: delete previous file index + register blobs + insert files + semantic armor index. Never split into independently committed repositories. |
| GetModFilesAsync | catalog read | R | mod_files | own connection | MainWindow, issue diagnosis, health | Candidate for catalog query repository with GetModsAsync later. |
| LoadPlannerSnapshotAsync | planner read model | R | mods projection, mod_files, conflict_rules, resource_providers, deployment_manifest, original_files | current implementation calls GetModsAsync on one connection, then opens a second connection for remaining reads; no encompassing transaction | MainWindow analysis/restore/safe mode, EffectiveInspector, GameUpdateImpact, LaunchHealthGate, UnmanagedAdoption, Health | Strong read-only domain boundary. Recommended first extraction, preserving current connection semantics exactly in the first move. |
| SetFamilyIdAsync | family metadata | W | mods | own connection, one update | NexusMetadataService | Simple command, but family writes are related to richer atomic ChainManualFamily operation; do not create a misleading repository API first. |
| ChainManualFamilyAsync | explicit user family/rule workflow | RW | mods, mod_families, mod_family_members, conflict_rules; reads existing roles/rules and RuleGraph | one explicit transaction | MainWindow conflict-family command; integration tests | Critical atomic domain transaction. Stale rule removal, membership rewrite, cycle validation and overlay insertion must commit together. |
| UpsertProvenanceAsync | Nexus/provenance | W | mod_provenance | own connection, one upsert | NexusMetadataService | Plausible ProvenanceRepository later. |
| GetProvenanceAsync | Nexus/provenance | R | mod_provenance | own connection | currently low/limited fan-out | Moving an isolated/unused read alone risks code motion; defer until provenance boundary includes actual callers. |
| ReplaceSupersessionAsync | Nexus/provenance lineage | W | mod_supersession | explicit transaction: delete all + insert replacement set | NexusMetadataService | Whole replacement set is atomic. Keep together. |
| RecordResolverAuditAsync | resolver audit | W | resolver_audit | explicit batch transaction | MainWindow Apply | Batch should remain atomic. Could become ResolverAuditRepository later. |
| GetGameBuildFingerprintAsync | game build state | R | game_build_state | own connection | GameBuildMonitor, backup/recipe/launch automation | Cohesive small domain, but paired with writes/revalidation workflow. |
| SetGameBuildFingerprintAsync | game build state | W | game_build_state | own connection, one upsert | GameBuildMonitor | Preserve current sequencing with revalidation; do not imply cross-call atomicity that does not exist today. |
| MarkRevalidationAsync | game compatibility state | W | mod_revalidation | explicit batch transaction | GameBuildMonitor, MainWindow after Apply | Batch IDs must update atomically. |
| GetSettingAsync | generic settings | R | settings | own connection | Nexus, visual, last-good automation | Generic key/value access is not a strong domain boundary by itself. |
| SetSettingAsync | generic settings | W | settings | own connection, one upsert | Nexus, visual, last-good automation | Same: generic utility, not first extraction. |
| GetSettingsByPrefixAsync | generic settings projection | R | settings | own connection | MainWindow update badges | Read-only but too generic to justify a repository merely to move code. |
| ExecuteAsync | persistence escape hatch | W/DDL-dependent | arbitrary caller-supplied SQL | own connection, one command | BlobStore, DuplicateCleanup, telemetry, legacy migrator, DeploymentExecutor state markers | Do not expand. Gradually shrink through domain repositories only when boundaries are proven. |
| InTransactionAsync | transaction infrastructure | transaction | caller-defined | owns one connection + one transaction | DeploymentExecutor | Must remain available until transaction-aware repositories can participate in caller-owned transactions. |
| IntegrityCheckAsync | DB health | R | PRAGMA integrity_check | own connection | Health, Support | Diagnostics query. Low priority. |
| CheckpointAsync | SQLite/WAL maintenance | W-ish PRAGMA | WAL | own connection | maintenance path | Infrastructure, not repository domain. |

## Existing repository/query boundaries

### PresentationReadRepository

Read-only presentation projections:
- recent Activity: operations + automation_events;
- Outfit/Coverage: armor_catalog + mod_file_armor + mods + deployment_manifest + preview settings.

This is the preferred precedent for early extraction: cohesive queries, no transaction ownership change, no WPF SQL.

### ProfileRepository

Owns one profile aggregate:
- LoadAsync reads profile_mods;
- ListAsync reads profiles/profile_mods;
- SaveCurrentAsync opens one connection and one transaction that upserts the profile row, replaces all profile_mods rows, and snapshots current mods.

SaveCurrentAsync is already a correct atomic boundary. Do not split profile-row write from profile_mod snapshot write.

## Service-owned transaction boundaries outside ManagerDatabase

These are real storage boundaries even though they are not repository classes yet.

### UnmanagedAdoptionService.RecordAdoptionAsync

One transaction:
- insert adoption_runs row;
- upsert every adopted_live_files row;
- commit once.

The filesystem package copy happens before this DB record; failure cleanup removes the copied source folder. Do not split the two DB writes into separate autocommits.

### ModTrustService.RecordLaunchAsync

One transaction updates mod_trust for all distinct enabled mod IDs for a launch. Preserve the launch-wide batch.

### ModIssueFallbackService

Atomic batches include:
- MarkBisectResultAsync: all isolated suspects upserted in one transaction;
- RecordSuccessfulLaunchAsync: resolving non-confirmed suspects across batches under one transaction;
- AnalyzeAndPersistAsync: selected suspect upserts in one transaction.

Read queries span mod_issue_suspects, mods and launch_history. A future issue repository is plausible, but preserve these batch boundaries.

### ChangeTimelineService

Each automation_events record is currently a single autocommitted statement. RecentAsync is a read projection. No multi-statement transaction to preserve.

### SaveBackupService

Reads mod/build/manifest state, writes snapshot.json to disk, then inserts save_snapshots. This is deliberately a cross-filesystem workflow rather than one SQLite atomic unit. A repository extraction must not pretend the file copy + DB insert are a single DB transaction.

### AutomationCoordinator launch history

LaunchAndObserve records trust, then launch_history, then issue/last-good/timeline effects as separate operations. RecordLaunchAsync inserts one launch_history row. Preserve current sequencing unless a future product requirement explicitly redefines launch atomicity.

### AutoCategoryService

Reads mods, reuses one open connection for per-mod mod_files reads and conditional category updates. It does not currently wrap the full categorization run in one transaction. Extraction should preserve that semantic unless separately redesigned/tested.

### DuplicateCleanupService

Moves a source directory, then deletes its mods row through ExecuteAsync. The filesystem move and DB delete are not one SQLite transaction. This is application/filesystem workflow debt, not a reason to couple it to deployment transactions.

### BlobStore

CAS capture is completed on disk before optional single-statement blobs registration. ReplaceModFilesAsync separately registers all referenced blobs inside the mod-file replacement transaction. Preserve CAS immutability and do not fold blob filesystem mutation into unrelated DB repository commits.

## DeploymentExecutor: boundaries that must not be split

DeploymentExecutor is the highest-risk caller and owns the key commit protocol.

### 1. Prepared journal transaction

WritePreparedJournalAsync calls ManagerDatabase.InTransactionAsync once.

That single transaction:
- inserts operations with state Prepared and before/after state JSON;
- inserts every operation_journal row;
- records original_files baselines for first takeovers.

Do not create repositories that independently commit operations, journal rows, or original baselines.

### 2. Filesystem mutation phase

After the prepared journal is durable:
- whole-plan preconditions are checked;
- known file locks are checked;
- operation state moves through Applying / FilesWritten / StateCommitting;
- each target is revalidated immediately before mutation to reduce TOCTOU;
- journal per-file status markers are persisted separately.

The state markers are intentionally outside the final commit transaction. Do not fold or reorder them casually.

### 3. Durable post-files SQLite commit boundary

CommitAfterStateAsync calls InTransactionAsync exactly once.

The same SqliteConnection + SqliteTransaction:
- updates/deletes deployment_manifest;
- updates/deletes original_files ownership;
- optionally updates every mods.enabled / mods.priority value;
- marks operations.state=Committed with committed_at.

This is the SQLite deployment commit point. A future DeploymentStateRepository may expose transaction-aware methods, but it must receive the caller-owned shared transaction. It must not call OpenAsync internally here.

### 4. Rollback commit boundary

Rollback first restores each filesystem path fail-closed: if live bytes match neither the journal before-image nor after-image, recovery refuses to overwrite the external change.

After filesystem restoration, one InTransactionAsync:
- reconstructs deployment_manifest;
- reconstructs original_files ownership;
- restores mods enabled/priority from state_before_json;
- marks the operation RolledBack.

All four must remain one transaction.

### 5. Startup recovery and undo

RecoverIncompleteAsync selects Prepared, Applying, FilesWritten, StateCommitting, RollingBack and RecoveryRequired operations and runs RollbackAsync.

UndoLastAsync reads a prior committed operation/journal, builds an inverse DeploymentPlan and calls ApplyAsync. Undo therefore inherits the same journal/filesystem/commit/recovery protocol rather than bypassing it.

## Migration/schema ownership

Schema.cs and ManagerDatabase.InitializeAsync own schema creation/versioning.

LegacyV7Migrator is a bounded one-shot migration workflow. It currently performs many individual ExecuteAsync calls and on failure runs ResetIncompleteImportAsync, whose multi-statement DELETE batch is sent as one SQLite command but is not wrapped in an explicit application transaction. This audit records that behavior; it does not redesign it.

Do not use the first repository extraction to change migration atomicity, schema versioning, WAL setup, or reset semantics.

## Transactional regression coverage already present

The integration suite directly protects the deployment boundary:

- crash before durable DB commit at after-journal, before-first-mutation, after-file-write, after-files-written and before-db-commit recovers to the exact before image;
- crash after-db-commit / before-cleanup remains committed;
- interrupted rollback enters RecoveryRequired and restart completes recovery;
- recovery refuses to destroy an external post-crash edit;
- whole-plan preflight prevents a stale later path from causing partial writes;
- takeover / restore-original / undo preserve manifest/original ownership;
- SQLite WAL/integrity survives parallel reads;
- manual family-chain tests cover persisted roles, explicit overlay replacement and deterministic ordering of multiple optional groups.

Any write-side repository extraction near these paths needs new tests plus the full Windows release gate. Existing tests are not permission to weaken transaction ownership.

## Repository boundaries that should not be split

Do not independently commit:
- ReplaceModFilesAsync: mod_files + blobs + mod_file_armor;
- ChainManualFamilyAsync: family membership + mods.family_id + removed/replaced conflict rules + new overlay rules;
- ProfileRepository.SaveCurrentAsync: profiles + full profile_mods snapshot;
- DeploymentExecutor prepared journal: operations + operation_journal + original_files;
- DeploymentExecutor final commit: deployment_manifest + original_files + mods state + Committed marker;
- DeploymentExecutor rollback commit: deployment_manifest + original_files + mods state + RolledBack marker;
- UnmanagedAdoptionService RecordAdoptionAsync: adoption_runs + adopted_live_files;
- ModTrustService launch-wide counter batch;
- ModIssueFallbackService suspect batches;
- ReplaceSupersessionAsync replacement set;
- MarkRevalidationAsync batch;
- SetEnabledAndPriorityAsync batch.

## Exactly one recommended first storage extraction

### PlannerSnapshotRepository (read-only)

Move only LoadPlannerSnapshotAsync and its planner-input query assembly out of ManagerDatabase into a new read-only PlannerSnapshotRepository.

Why this is the best first seam:
- it is already expressed as a domain-level read model rather than arbitrary SQL;
- callers are all consumers of planner/effective-state input: MainWindow analysis/restore/safe mode, EffectiveInspectorService, GameUpdateImpactService, LaunchHealthGateService, UnmanagedAdoptionService and HealthService;
- it has no write transaction ownership;
- it does not participate inside DeploymentExecutor's shared commit/rollback transaction callbacks;
- extracting the existing query wholesale creates a real architecture boundary instead of grouping methods by table;
- it reduces direct ManagerDatabase responsibility without inventing a generic repository abstraction.

First extraction constraints:
1. Preserve the current query results and ordering exactly.
2. Preserve the current optional fileModIds filtering behavior exactly, including empty-list behavior.
3. Preserve the current connection semantics on the first move. Today GetModsAsync is read through its own connection before the remaining planner tables are read through another connection; do not silently turn the extraction into a snapshot-isolation redesign.
4. Do not move GetModsAsync in the same checkpoint.
5. Do not touch DeploymentExecutor transaction code.
6. Wire one PlannerSnapshotRepository through the existing AppServices composition only as needed; do not combine this with Generic Host/DI work.
7. Add focused parity/regression coverage for full and filtered snapshots and confirm planner output remains unchanged.
8. Treat that source change as a new production verification boundary; source -> adjacent continuity -> hosted Windows evidence -> closure before any second storage extraction.

## Rejected first extractions

- SettingsRepository: easy but generic key/value code motion, not a meaningful domain boundary.
- DeploymentStateRepository: meaningful but too close to critical shared transactions for a first storage move.
- ProvenanceRepository: plausible later, but moving isolated methods before mapping Nexus lineage workflow would optimize file organization rather than coupling.
- CatalogRepository containing reads and writes: too broad for the first slice; ReplaceModFiles atomicity makes a combined repository unnecessarily risky.
- IssueRepository: plausible, but current service-owned batch transactions and launch coupling make it a second-stage design task.
- broad UnitOfWork abstraction: unnecessary. Existing explicit SqliteConnection/SqliteTransaction ownership is clearer and safer.

## Exact next action

Implement PlannerSnapshotRepository only, with no behavioral changes. Add focused query/parity tests, keep MainWindow/deployment architecture unchanged, update continuity, push the source checkpoint, and require a fresh full Windows Release Gate before beginning any other production change.

The next agent must re-read AGENTS.md, this document, MAINWINDOW_RESPONSIBILITY_AUDIT.md and the canonical continuity files before editing. Preserve the same source -> evidence -> closure discipline. Do not break the chain.


## Implementation status — PlannerSnapshotRepository candidate

Production source commit `8e0068bd44cc6735ffa9478067923ad5d9c54506` implements exactly the recommended first extraction. Focused parity/cancellation coverage is hardened in `64e666a19ce17c21bc696b46cce9c07bb257a686`.

The implementation confirmed the audit assumptions rather than disproving them: `GetModsAsync` still owns its independent read connection; the new repository then uses one separate `OpenAsync` connection for the remaining planner inputs; no write-side transaction owner or DeploymentExecutor path changed. The candidate remains unverified until a fresh exact hosted Windows Release Gate passes.
