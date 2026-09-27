# v7 -> v8 migration

The v7 source of truth is `State\V2\state.json` plus its immutable legacy blob store and history. v8 imports that state during MHW startup before normal catalog, automation, recovery, or UI initialization continues.

Migration is considered complete only when the v8 database contains a non-empty `schema_info.legacy_migration_complete` marker. Merely having rows in the v8 database is **not** the completion test.

## Current algorithm

1. If `State\V2\state.json` does not exist, migration is skipped.
2. If `schema_info.legacy_migration_complete` exists, migration is skipped as already complete.
3. If legacy state exists but the completion marker does not, the migrator treats prior imported v8 migration state as incomplete and runs `ResetIncompleteImportAsync` before retrying.
4. A new `migration_runs` row is created with status `Running`.
5. A metadata point-in-time backup is created under `State\NextMigrationBackup\<timestamp>`. It copies legacy state/UI/history metadata and writes `legacy-location.txt`; it is not a self-contained copy of all legacy blobs.
6. Legacy schema `2` is required.
7. Enabled legacy mods, their exact captured file hashes, disabled local Mod folders, original-file baselines, exact winners, pair relationships, resource-provider pins, expected deployment state, and profile metadata/order are imported.
8. Referenced legacy blobs are made available in the v8 CAS. On Windows the migrator first attempts a hardlink from the legacy blob; otherwise it falls back to a byte copy. Existing destination hash paths are currently reused.
9. The armor index is rebuilt from imported mod files.
10. Every referenced v8 CAS object is re-read and SHA-256 verified against its expected hash.
11. Imported enabled mod names ordered by priority must exactly match the legacy `order` sequence case-insensitively.
12. A PASS migration report is written.
13. The `legacy_migration_complete` marker is written with the migration ID.
14. The matching `migration_runs` row is then updated to `Complete`.

## Failure / retry behavior

Any ordinary exception after the migration run has entered its main `try` block causes the migrator to attempt all of the following with cancellation disabled:

- write a FAILED report;
- mark the current run `Failed`;
- reset incomplete imported v8 rows.

Startup then stops instead of continuing into normal application workflows.

The current implementation is a **restart protocol**, not one giant transaction. Reset is a multi-statement database command without an explicit encompassing application transaction, so abrupt process death can leave a partial reset; the next startup retries reset before importing again.

Cleanup is best-effort. If report/status/reset cleanup itself throws, that cleanup exception is currently swallowed and startup still reports migration failure. Therefore support/debugging should not infer that cleanup definitely completed solely from the returned failure message.

A process death after the completion marker is written but before the current `migration_runs` row becomes `Complete` leaves the migration logically complete while that run can remain stale as `Running`.

## Safety invariants

Current migration deliberately preserves these boundaries:

- legacy `State\V2` is not modified by normal migration logic;
- live game `nativePC` is not modified by migration;
- no completion marker is written until referenced blobs and enabled order have been validated;
- a reported migration failure prevents normal startup from continuing;
- migration history rows are retained for diagnostics.

## Known hardening work

The detailed migration/recovery audit is:

`_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md`

Important future test-first checkpoints include:

- corrupt/pre-existing CAS object retry convergence;
- truthful reporting when failure cleanup cannot be confirmed;
- exactly-one migration owner when two processes target the same workspace;
- restart recovery at every migration phase;
- reconciliation of completion marker vs stale Running run;
- explicit policy/reporting for unresolved winner/relation/resource/profile references;
- protection against destructive reset when the completion marker is missing from an otherwise established v8 workspace;
- unique backup/report identity for concurrent or same-second attempts.

Do not redesign these behaviors without focused migration characterization/fault tests and a fresh exact verification boundary.
