# Legacy v7 → v8 migration / recovery deep audit — 2026-09-27

## Scope and why this support lane was selected

This is an independent support-agent audit of the one-shot legacy migration path implemented by:

- `src/MhwModManager.Storage/LegacyV7Migrator.cs`
- `src/MhwModManager.Storage/Schema.cs`
- `src/MhwModManager.Storage/ManagerDatabase.cs`
- `src/MhwModManager.App/App.xaml.cs`
- `docs/MIGRATION.md`

Canonical base inspected before branching:

`6ada5a5c4cc83afadfba42bc6af6559540920e3d`

Branch:

`agent/support-7-legacy-migration-recovery-audit-20260927`

This lane was chosen only after inspecting current `main`, continuity state, recent history, and open PRs.

Parallel work already owns:

- PR #1 — product workflows / FOMOD / loadouts / update workflows;
- PR #2 — MainWindow/UI architecture;
- PR #3 — verification infrastructure / supply chain;
- PR #4 — broad test-gap / failure / performance audit;
- PR #5 — multi-source discovery + safe bulk fill;
- PR #6 — async lifetime / cancellation / shutdown;
- PR #7 — Windows filesystem containment / ReplaceFileW / CAS;
- PR #8 — MHW semantic coverage / bulk-fill slot model;
- canonical `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` — write-side SQLite atomicity.

No active branch or PR owns migration semantic fidelity, retry convergence, migration ownership, backup/report behavior, or end-to-end v7→v8 recovery.

This audit deliberately does **not** duplicate the deep SQLite audit's transaction findings. Where the two overlap, the SQLite audit remains authoritative for SQLite statement/transaction behavior; this document owns the migration protocol as a whole.

No production C# is changed by this checkpoint.

---

## Executive summary

The migration design has several strong safety properties:

- legacy `State\V2` remains authoritative until an explicit completion marker exists;
- migration runs before catalog/automation/UI startup is allowed to continue;
- referenced blob bytes are SHA-256 verified before completion;
- enabled mod order is checked exactly, not merely by count;
- normal exceptions attempt to mark the migration failed and reset imported v8 rows;
- `nativePC` is not modified by the migration;
- startup stops when migration returns failure.

However, the migration protocol is substantially less tested than its importance warrants.

**No migration-specific test reference was found in any of the 21 current C# test files.**

The highest-value confirmed/static gaps are:

1. **Retry can become permanently stuck on a corrupt pre-existing v8 CAS object.**
   `EnsureLegacyBlob` trusts `nextBlobRoot\<sha>` by existence and refuses to replace it. Final verification detects bad bytes, but failure reset removes only DB blob rows, not the physical bad file. A later retry sees the same file and fails again.

2. **Failure reporting can claim cleanup succeeded when cleanup itself failed.**
   The catch path wraps report writing, run-status update, and `ResetIncompleteImportAsync` in an inner `try/catch{}`; after any inner failure it still returns: “Partial v8 import rows were discarded.” That statement is not guaranteed.

3. **A single completion-marker absence authorizes destructive reset of broad v8 state.**
   If legacy state exists and `schema_info.legacy_migration_complete` is absent, startup immediately executes `ResetIncompleteImportAsync`. This is safe for the intended “crashed first migration” protocol, but there is no second ownership/provenance guard protecting a previously used v8 DB if its marker is lost, copied incorrectly, or manually/corruptly removed.

4. **No whole-migration ownership/lease prevents two app processes from migrating the same workspace concurrently.**
   Inspection of all 20 App C# files found no process-wide application mutex/single-instance lease. SQLite busy timeout serializes individual writes, not the multi-step migration protocol. Two processes can both see “not complete,” both reset/import, and one failing process can reset rows while another is still importing.

5. **Success validation is narrower than imported semantics.**
   Completion verifies referenced blob hashes and exact enabled order. It does not prove parity of migrated winners, pair relations, resource-provider pins, profile contents, original baselines, or manifest provider inference. Several import loops silently skip unresolved references rather than failing or recording a structured warning.

6. **Current migration documentation is stale relative to code.**
   The existing guide says migration is skipped when the v8 DB already contains mods and says the migration backup uses hardlinks when possible. Current code actually uses the explicit completion marker, performs a reset when the marker is absent, copies backup metadata, and uses hardlink/copy only for CAS blob import.

7. **The completion marker and migration-run status remain separate durable writes.**
   This is already documented as D4 in the deep SQLite audit. A crash after the marker but before `migration_runs.status='Complete'` leaves an authoritative completed migration with a permanently stale Running row unless future code reconciles it.

8. **Same-second backup/report paths can collide.**
   Backup directory names use local time with second precision only. Rapid retries or concurrent migrators can target the same backup/report directory, overwriting metadata copies/report evidence.

These are mostly recoverability / fidelity / operator-truth issues rather than evidence that the current happy path corrupts a normal migration.

---

## Current migration protocol reconstructed from source

### Startup ordering

For MHW, `App.OnStartup`:

1. discovers paths;
2. initializes the v8 SQLite DB;
3. constructs services;
4. invokes `LegacyV7Migrator.MigrateIfNeededAsync`;
5. if `Performed && !Success`, startup stops and exits;
6. only after migration success/skip does startup refresh catalog, import armor catalog, refresh Nexus metadata, check build state, recover deployment operations, run automation, and create the main window.

This is a strong safety boundary: most ordinary v8 application workflows cannot create state after a reported migration failure in the same startup.

### Skip / retry decision

`MigrateIfNeededAsync`:

1. if `State\V2\state.json` does not exist → no migration;
2. if `schema_info.legacy_migration_complete` contains a nonblank value → migration considered complete;
3. otherwise → `ResetIncompleteImportAsync` runs before a new migration attempt.

The current implementation does **not** use “DB contains mods” as the completion test.

### New run initialization

After reset:

- migration ID = random GUID;
- backup directory = `State\NextMigrationBackup\yyyyMMdd-HHmmss`;
- migration report = `migration-report.md`;
- a `migration_runs(... status='Running')` row is inserted;
- metadata backup is created.

### Backup contents

`CreatePointInTimeBackup` copies:

- `state.json`;
- `ui-settings.json` if present;
- `ui-draft.json` if present;
- `History\**\*.json`;
- `legacy-location.txt`.

Copied backup files are made read-only when possible.

The backup routine itself uses `File.Copy`, not hardlinks.

Legacy content blobs are **not** copied into the metadata backup. They remain in legacy `State\V2\Blobs`; referenced blobs are separately linked/copied into the v8 CAS during migration.

### Import semantics

The migrator imports:

- enabled legacy mods;
- mod files and blob metadata;
- disabled local Mod folders as disabled catalog entries;
- original-file baselines;
- exact-file winner rules;
- mod-pair Overlay/Incompatible relations;
- resource-provider pins;
- expected deployment manifest entries;
- profile metadata and profile mod order;
- armor index derived from imported mod files.

### Referenced blob transfer

For a referenced SHA:

`EnsureLegacyBlob(hash)`

- checks legacy blob exists;
- ensures next CAS root exists;
- if destination SHA path does not exist:
  - tries `CreateHardLinkW` on Windows;
  - falls back to `File.Copy`;
- if destination exists, reuses it without validating bytes at that moment.

### Success validation

Before completion:

- every referenced next-CAS object is re-read and SHA-256 verified against its filename/hash;
- enabled imported mod names ordered by imported priority must exactly `SequenceEqual` legacy `order` case-insensitively.

Then:

1. PASS report is written;
2. `schema_info.legacy_migration_complete = migrationId` is written;
3. `migration_runs` row is updated to Complete.

### Failure handling

Any `Exception`, including cancellation, enters one catch block.

The catch *attempts*:

1. write FAILED report;
2. mark current migration run Failed;
3. call `ResetIncompleteImportAsync(CancellationToken.None)`.

Any exception in those cleanup steps is swallowed.

The method then returns `MigrationResult(Performed=true, Success=false, ...)`, and App startup exits.

---

## Existing strengths worth preserving

### 1. Legacy state remains untouched by normal migration logic

No migration step intentionally writes v7 `state.json`, History, legacy Mods contents, or live `nativePC`.

### 2. Migration is a startup gate

A reported failure prevents normal catalog/automation/UI startup from proceeding.

### 3. Blob validation is authoritative before the completion marker

The final verification reads actual bytes from next CAS and computes SHA-256.

### 4. Enabled order validation is stronger than the current documentation says

The code checks exact case-insensitive sequence equality, not only count equality.

### 5. Mod-file replacement uses an existing atomic helper

Per-mod `ReplaceModFilesAsync` has its own transaction boundary.

### 6. Reset is intentionally retry-oriented

The design clearly treats incomplete v8 imported state as disposable while legacy v7 remains authoritative.

### 7. Migration history rows are preserved

`ResetIncompleteImportAsync` deliberately does not delete `migration_runs`, keeping diagnostics from prior attempts.

### 8. Failure cleanup ignores user cancellation

Once cleanup starts from the catch path, it passes `CancellationToken.None`, which is appropriate for best-effort recovery rather than abandoning reset merely because the initiating token was canceled.

---

# Findings

## P1 — corrupt existing CAS object creates a retry trap

### Source

`LegacyV7Migrator.EnsureLegacyBlob`:

- if `nextBlobRoot\hash` exists, it is reused;
- only a missing destination is linked/copied.

Final migration validation later catches mismatched bytes.

`ResetIncompleteImportAsync` deletes the `blobs` table row but does **not** remove the physical hash-named object.

### Failure path

1. destination CAS file H already exists;
2. its bytes do not hash to H;
3. migration reuses it;
4. final verification fails;
5. failure reset removes imported DB rows;
6. physical bad file H remains;
7. next startup retries;
8. `EnsureLegacyBlob(H)` sees the same existing file and reuses it;
9. final verification fails again.

The protocol does not converge without external/manual repair.

### How it can arise

Confirmed source supports the “pre-existing corrupt object” path.

A partial destination from abrupt death during fallback `File.Copy` is a plausible way to create such an object, but this audit did not reproduce Windows/.NET copy interruption behavior and therefore does not label that trigger runtime-confirmed.

### Exact regression

`Migration_retry_repairs_or_rejects_corrupt_existing_CAS_object_without_livelock`

Fixture:

- valid legacy blob A, SHA H;
- pre-create next CAS path H with different bytes B;
- create minimal valid legacy state referencing H.

Run #1 / retry expectation:

- migration must not endlessly reuse B;
- either:
  - atomically replace/quarantine B with verified A, then succeed; or
  - fail with an explicit non-retryable corruption diagnosis that identifies H and required operator action.

On a supported automatic-repair design, a second invocation must converge to success without manual deletion.

DB expectation:

- no completion marker until H contains verified bytes;
- no false Complete migration run.

---

## P1 — cleanup failure is swallowed but success-of-cleanup is asserted to the user

### Source

Migration catch:

- inner `try` writes failed report, marks run Failed, resets import;
- inner `catch{}` suppresses every cleanup error;
- outer method always returns message:

“Migration failed. Partial v8 import rows were discarded; v7 state and nativePC were left untouched.”

### Problem

The first clause is not guaranteed.

Examples:

- database locked beyond timeout during reset;
- I/O failure while writing report;
- DB error before/during reset;
- process failure during reset.

The deep SQLite audit already establishes that reset is not an explicit atomic application transaction and can be partially applied by process death.

### Exact regression

`Migration_cleanup_failure_never_reports_partial_rows_as_discarded`

Fixture:

- inject a failure in reset after migration has written some rows.

Expected result:

- `Success=false`;
- message distinguishes:
  - migration failed **and cleanup completed**, vs
  - migration failed **and cleanup could not be confirmed; retry/recovery required**;
- run diagnostics preserve both original migration exception and cleanup exception;
- no statement claims rows were discarded unless reset completion was actually observed.

---

## P1 — completion-marker absence is a destructive reset authority

### Source

When legacy state exists and no completion marker exists:

`await ResetIncompleteImportAsync(ct);`

runs **before** creating a new run or backup.

Reset deletes broad operational/import state including:

- operations/journal;
- profiles/profile rules;
- families;
- manifest/original files;
- conflict/resource rules;
- external changes;
- mod files/mods;
- blob DB rows.

### Intended case

This is appropriate after a first-run migration crash while legacy state remains authoritative.

### Unsafe ambiguity

There is no separate migration ownership/version witness proving the database is merely an incomplete migration.

If a previously operational v8 database loses only the completion marker while legacy `State\V2\state.json` still exists, startup treats that DB as disposable migration state and resets it.

This audit did not reproduce marker loss; the risk is the source-level trust model.

### Stale state outside the reset

The reset is not a literal whole-database reset.

Rows can survive in tables not directly deleted or cascaded, such as:

- `family_preferences` rows (selected mod may become NULL);
- `resolver_audit`;
- `game_build_state`;
- `adoption_runs`;
- `adopted_live_files`;
- `settings`;
- `diagnostics`;
- `error_reports`;
- `migration_runs`;
- `automation_events`;
- `save_snapshots`;
- `launch_history`.

Some mod-linked tables *are* cleaned by FK cascade; others are intentionally historical/global.

That is reasonable for normal first-start retry, but it reinforces why “marker missing” must not casually mean “this is definitely migration-owned disposable state.”

### Exact regression

`Migration_missing_completion_marker_with_established_v8_state_fails_closed_before_destructive_reset`

Fixture:

- valid legacy state still exists;
- v8 DB contains evidence impossible for an incomplete first migration, e.g. committed post-migration operation / launch / snapshot / explicit post-migration witness;
- completion marker absent.

Expected:

- migrator does not automatically erase user v8 state;
- returns a recovery-required result or requires explicit ownership reconciliation;
- no mutation until migration ownership is established.

The exact witness policy should be designed before production change.

---

## P1 — no whole-migration single-owner lease

### Evidence inspected

All 20 C# files under `src/MhwModManager.App` were inspected for common process-wide single-instance/lease mechanisms.

No application mutex, named semaphore, lockfile lease, or equivalent startup ownership guard was found.

The only `SemaphoreSlim` found is `MainWindowViewModel.metadataGate`, unrelated to startup migration.

`ManagerDatabase` uses:

- WAL;
- per-operation connections;
- 10-second SQLite busy timeout.

Those protect individual database operations, not ownership of the migration protocol.

### Race

Two manager processes can:

1. both initialize the same DB;
2. both see legacy state;
3. both see no completion marker;
4. both reset;
5. both create Running migration runs;
6. both import/autocommit data;
7. race on profile unique names, CAS publication, rules, manifest, and completion.

Most concerning:

- process A can fail;
- A's catch executes `ResetIncompleteImportAsync`;
- process B may still be actively importing;
- A can delete B's imported state.

SQLite serialization does not turn these many commits into one migration lease.

### Exact regression

`Concurrent_migration_attempts_allow_exactly_one_migration_owner`

Fixture:

- same tool root, DB, legacy state, CAS;
- two migrator instances/process-like tasks;
- synchronization barrier after “not complete” check.

Expected contract:

- exactly one acquires migration ownership;
- other reports “migration already running” or waits/rechecks;
- loser never runs reset;
- exactly one authoritative completion marker;
- no two active Running runs;
- no cross-attempt deletion.

A real process-level Windows test should complement an in-process deterministic lease test if the production design uses OS/process primitives.

---

## P1 — semantic references can be silently dropped yet migration can PASS

### Exact winner import

If winner name is missing from `nameToId`, the entry is skipped.

### Relation import

If either side cannot map to an imported mod, the relation is skipped.

### Resource provider import

If provider name cannot map to a mod, nothing is inserted.

### Profile order import

Unknown names are ignored.

### Success gate

The final gate checks:

- referenced CAS SHA values;
- exact enabled order.

It does **not** compare expected counts/content for:

- winner rules;
- pair rules;
- resource providers;
- profiles/profile membership;
- original baseline entries;
- deployment manifest provider mapping.

### Consequence

An internally inconsistent legacy state can lose user-authored semantics while still reaching `legacy_migration_complete`.

Whether unresolved references should hard-fail or become explicit warnings is a product decision; silent dropping should not be the only evidence.

### Exact regression

`Migration_reports_every_unresolved_legacy_semantic_reference_before_completion`

Fixture includes:

- winner referencing unknown mod;
- relation referencing unknown side;
- resource provider referencing unknown mod;
- profile order referencing unknown mod.

Expected:

- migration result/report contains structured unresolved-reference inventory;
- policy is explicit:
  - strict mode fails completion, **or**
  - tolerant mode can complete only while clearly recording each dropped semantic.

### Parity test

`Migration_success_gate_validates_imported_rules_profiles_resources_originals_and_manifest`

Use a representative legacy state and compare imported semantic projections with expected values.

---

## P2 — documentation does not match implementation

Current `docs/MIGRATION.md` says:

1. “If the v8 database already contains mods, migration is not rerun.”
2. backup is created “using hardlinks where Windows permits and byte copies otherwise.”
3. enabled validation is described as count equality.

Current source instead:

- uses `legacy_migration_complete` marker;
- resets incomplete imported DB rows before retry;
- copies metadata backup files;
- uses hardlink/copy for referenced CAS blobs;
- verifies the exact enabled order sequence.

This checkpoint should correct the guide without changing runtime behavior.

---

## P2 — migration run / completion status split

This is already canonical deep-SQLite finding D4.

Completion sequence:

1. write PASS report;
2. insert/update completion marker;
3. update run Complete.

Abrupt death between 2 and 3:

- next startup trusts marker and skips migration;
- prior run stays Running.

### Exact regression

`Migration_restart_reconciles_completion_marker_with_running_migration_run`

Do not redesign this in the same checkpoint as migration ownership/CAS recovery unless deliberately scoped.

---

## P2 — abrupt death before catch leaves historical Running rows

Also covered by deep SQLite D4.

A later retry resets imported state but does not mark older Running runs Abandoned/Interrupted.

Exact regression:

`Migration_retry_marks_prior_unfinished_run_interrupted_without_erasing_history`.

---

## P2 — backup/report identity can collide

### Source

`DateTime.Now.ToString("yyyyMMdd-HHmmss")`

defines backup directory name.

Two attempts in the same local second use the same path.

`Directory.CreateDirectory` accepts the existing directory.

Metadata copy uses overwrite.

The report path is the same.

### Consequence

- evidence from two attempts can overwrite/merge in one directory;
- separate `migration_runs` can point to the same backup/report;
- concurrent migration race becomes harder to diagnose.

### Exact regression

`Migration_attempts_started_in_same_second_receive_unique_backup_and_report_paths`

Prefer migration ID or higher-precision/monotonic unique suffix in future design.

---

## P2 — cancellation is normalized into migration failure

`OperationCanceledException` is caught by `catch(Exception)`.

Cleanup then runs with `CancellationToken.None`.

This is defensible for startup safety but should be deliberate and tested.

Exact regression:

`Migration_cancellation_after_partial_import_cleans_or_reports_recovery_required_and_never_completes`.

Expected:

- no completion marker;
- deterministic run status;
- no false “cleaned” statement unless cleanup completed;
- App startup remains stopped.

---

## P2 — backup is evidence, not a self-contained restore package

The backup directory contains state/history/UI metadata, not all referenced legacy blobs.

This is not necessarily a defect because legacy `State\V2\Blobs` is explicitly preserved.

But documentation/support UI must not imply `NextMigrationBackup\<timestamp>` alone is sufficient to restore all legacy content if the original legacy blob directory is later lost.

The backup contract should say:

- metadata point-in-time evidence;
- legacy blob store remains the content source;
- migration does not delete legacy state.

---

# Reset ownership audit

## Tables explicitly deleted

`ResetIncompleteImportAsync` explicitly deletes:

- operation_journal
- operations
- profile_rules
- profile_mods
- profiles
- mod_family_members
- mod_families
- deployment_manifest
- original_files
- conflict_rules
- resource_providers
- external_changes
- mod_files
- mods
- blobs
- completion marker only from schema_info

## Tables cleared indirectly through mod FK cascade

Depending on the declared FK:

- mod_provenance
- mod_supersession
- mod_revalidation
- mod_trust
- mod_issue_suspects
- mod_file_armor via mod_files

## Tables/history that can remain

- family_preferences
- resolver_audit
- game_build_state
- adoption_runs
- adopted_live_files
- armor_catalog
- settings
- diagnostics
- error_reports
- migration_runs
- automation_events
- save_snapshots
- launch_history

This mix is acceptable only under the intended first-start retry invariant.

It is not a safe generic “restore database to pristine pre-v8” operation.

---

# Failure / restart matrix

Legend:

- **Covered by source design** means a recovery path exists in code.
- **Test gap** means no migration-specific test currently pins the behavior.

| Phase | Failure | Current behavior | Convergence | Dedicated test |
|---|---|---|---|---|
| no legacy state | normal | skip | yes | none |
| marker already present | normal | skip | yes | none |
| during initial reset | exception | bubbles before run creation | next startup retries reset | none |
| process death during initial reset | partial deletes possible | next startup repeats reset | intended idempotent convergence | none |
| create Running row | DB fail | method not yet inside try? run insert occurs before `try` | exception can escape startup wrapper rather than MigrationResult failure | none |
| backup creation | exception | catch marks Failed + reset attempt | usually | none |
| parse state | invalid JSON/schema | catch + reset attempt | repeated legacy error until state fixed | none |
| mod import | ordinary exception | catch + reset | usually | none |
| mod import | abrupt death | Running row remains; partial rows | next reset/retry | none |
| CAS hardlink | success | imported object aliases legacy blob | proceeds | none |
| CAS copy | ordinary exception | catch + reset; partial physical file may remain depending platform behavior | uncertain if partial dest remains | none |
| CAS destination already corrupt | final SHA failure | reset DB, leave bad file | **non-convergent** without repair | none |
| winners/relations/resources/profile unresolved | skipped | may still complete | converges with semantic loss/warning absence | none |
| armor index rebuild | exception | catch + reset | retry | none |
| final SHA verify | mismatch | catch + reset | corrupt existing CAS can livelock | none |
| enabled order verify | mismatch | catch + reset | repeats until legacy/state logic changes | none |
| PASS report | write failure | catch writes FAILED report attempt + reset | retry | none |
| completion marker write | ordinary exception | catch + reset | retry | none |
| after marker / before run Complete | abrupt death | next startup skips; Running row stale | data complete, diagnostics stale | none |
| run Complete update | ordinary exception | catch deletes marker/reset | retry | none |
| catch cleanup | cleanup fails | swallowed; returns cleanup-success wording | next startup may retry reset | none |
| two processes migrate | interleaving commits/resets | no protocol lease | not guaranteed | none |

### Important source nuance: Running-row insert is outside the migration try/catch

The code inserts the `migration_runs(status='Running')` row immediately before entering the `try` block.

If that insert itself throws, `MigrateIfNeededAsync` does not convert it to `MigrationResult(Success=false)`; the exception propagates to the outer startup diagnostic/fatal handler.

That may be acceptable, but it differs from later migration failures and should be covered in the startup contract.

---

# Test coverage audit

All current C# tests were inspected for migration identifiers/terms.

Test files inspected: **21**.

Migration-specific references found: **0**.

This means even basic behavior is currently unpinned:

- no legacy state skip;
- marker-complete skip;
- valid happy-path migration;
- malformed schema;
- missing blob;
- corrupt blob;
- enabled-order mismatch;
- semantic rule/profile/provider import;
- failed reset;
- retry;
- cancellation;
- abrupt death;
- completion-marker/run-status split;
- concurrent migration;
- backup/report uniqueness.

The absence of tests is notable because migration is both destructive to disposable v8 import state and a hard startup gate.

---

# Recommended deterministic fault seam

Do not begin by splitting LegacyV7Migrator into many repositories.

For testability, introduce the narrowest possible migration fault/clock/ownership seams in a dedicated future checkpoint.

A useful test design may expose named phases such as:

- before-reset
- after-reset
- after-run-created
- after-backup
- after-mod-N
- after-rules
- after-profiles
- after-armor-index
- after-blob-verification
- after-order-verification
- after-pass-report
- after-completion-marker
- after-run-complete
- during-failure-reset

The existing DeploymentExecutor phase-injection pattern is a good conceptual precedent, but do not copy its API blindly.

Also inject or centralize:

- migration clock / backup-name factory;
- CAS publish/verify policy;
- migration lease owner;
- cleanup outcome.

Tests should be able to emulate abrupt death separately from ordinary exceptions so catch-based compensation is not accidentally credited for process-death recovery.

---

# Recommended independent checkpoints

## Checkpoint 1 — migration characterization suite only

No production redesign.

Add `LegacyV7MigratorTests` covering:

- no legacy state;
- valid minimal migration;
- completion-marker skip;
- malformed schema;
- missing blob;
- corrupt existing next-CAS blob;
- exact enabled-order preservation;
- winners/relations/resource/provider/profile import;
- ordinary failure followed by retry;
- cancellation.

This establishes current behavior before changing protocol.

## Checkpoint 2 — retry convergence / CAS contamination

Define desired handling for a corrupt pre-existing hash path.

Then make migration retry converge automatically or produce explicit non-retryable diagnosis.

Coordinate with PR #7 / the Windows filesystem/CAS audit if that PR is integrated first.

## Checkpoint 3 — truthful cleanup result

Separate:

- migration failure;
- cleanup success/failure;
- restart recovery requirement.

Do not swallow cleanup error without preserving it in result/run diagnostics.

## Checkpoint 4 — migration ownership lease

Guarantee exactly one migrator can own a workspace at a time.

Keep this separate from general application single-instance UX unless the chosen mechanism intentionally solves both.

## Checkpoint 5 — completion/run-status reconciliation

Implement deep-SQLite D4:

- completion marker + run completion atomic where possible;
- reconcile abandoned Running rows on restart.

## Checkpoint 6 — semantic fidelity gate

Decide strict-vs-warning policy for unresolved legacy semantic references.

Validate/report migrated:

- exact winners;
- pair relationships;
- resource providers;
- profiles;
- originals;
- manifest.

## Checkpoint 7 — marker-loss destructive-reset guard

Define an explicit migration-ownership witness before treating an established v8 DB as disposable.

This should be designed only after tests establish normal crash/retry state.

## Checkpoint 8 — backup/report identity

Use migration ID/unique suffix; document backup as metadata evidence, not a full blob archive.

---

# What should not be changed casually

Do not:

- wrap the entire migration in one giant SQLite transaction while filesystem CAS copying remains outside it;
- delete or mutate legacy v7 state merely to simplify retry;
- weaken final authoritative SHA verification;
- let startup proceed after an unconfirmed failed migration;
- reuse “DB contains mods” as the sole completion criterion;
- broaden reset to every v8 table without defining historical/global-state ownership;
- merge migration ownership, generic app single-instance behavior, CAS redesign, SQLite repository decomposition, and UI changes into one checkpoint.

The current restart-protocol architecture can be hardened incrementally.

---

# Documentation correction required

`docs/MIGRATION.md` should describe current source truth:

- completion is keyed by `legacy_migration_complete`;
- marker absence triggers an incomplete-import reset;
- metadata backup uses copies;
- CAS import may hardlink/copy;
- exact enabled order is validated;
- failure *attempts* cleanup and startup stops;
- cleanup is not currently guaranteed if its own recovery step fails;
- completion marker and run Complete state are separate current writes;
- legacy state and live `nativePC` remain untouched by normal migration.

This audit branch updates the guide accordingly.

---

# Verification honesty

Performed:

- inspected canonical main and recent history;
- inspected all open PRs and their scopes before selecting this lane;
- inspected permanent continuity state and existing SQLite/storage audits;
- inspected the full current `LegacyV7Migrator.cs`;
- inspected migration startup call site;
- inspected schema definitions;
- inspected ManagerDatabase connection/transaction behavior relevant to migration;
- inspected all 21 current C# test files for migration coverage;
- inspected all 20 App C# files for common application-wide single-instance/lease mechanisms;
- reconstructed reset ownership against current Schema tables;
- compared `docs/MIGRATION.md` to current implementation.

Not performed:

- no local Windows runtime test;
- no migration was executed against real v7 user state;
- no process-kill fault injection;
- no concurrent two-process migration reproduction;
- no `dotnet test`;
- no hosted Windows Release Gate;
- no verification cache promotion;
- no production C# change.

Therefore concurrency, interrupted copy, disk-failure, and process-death effects that require runtime reproduction are labeled as risks/test requirements rather than falsely reported as observed failures.

---

# Parallel integration notes

## Deep SQLite audit

Preserve `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`.

It remains the specialized authority for:

- reset non-atomicity;
- stale Running rows;
- completion-marker/run-status split;
- DB transaction boundaries.

This audit adds migration-level ownership, semantic-fidelity, retry, CAS, backup, and user-message analysis.

## Windows filesystem audit / PR #7

PR #7 specializes in reparse containment, ReplaceFileW, CAS object trust, and hardlink aliasing.

If it lands first:

- preserve its CAS/reparse requirements;
- migration tests should reuse the resulting CAS integrity contract rather than inventing a second one;
- this audit remains the authority for migration retry/convergence.

## Broad test-gap audit / PR #4

PR #4 already calls for migration fault tests at a high level.

This document is the detailed migration-specific test/failure matrix and should supersede the broad audit only for migration scope.

## Learned Rule IDs

Open support branches currently reserve:

- LR-003 in PR #4;
- LR-004 in PR #7.

If this audit adds its durable migration rule, use **LR-005** to avoid a future merge collision even though canonical main currently contains only LR-001/LR-002.

---

# Durable learned-rule candidate

## LR-005 — restartable migrations must prove ownership and convergence

A migration that treats partial destination state as disposable must not rely on one missing completion marker plus artifact existence alone.

For cross-filesystem/DB migration:

- establish explicit migration ownership before destructive reset;
- verify pre-existing destination artifacts before trusting/reusing them;
- ensure a failed attempt either converges on retry or clearly reports a non-retryable operator action;
- distinguish cleanup attempted from cleanup proven complete;
- never claim success/cleanup based solely on swallowed recovery exceptions.

This rule is specific enough to be incident-driven and broad enough to protect future schema/data migrations.

---

# Successor handoff

The next agent must:

1. verify actual canonical `main`;
2. inspect all open support PRs because several branches may merge out of order;
3. inherit `CONTINUITY_PROTOCOL.md` and active Learned Rules;
4. read this migration audit plus the deep SQLite and Windows filesystem audits;
5. do **not** start by rewriting LegacyV7Migrator;
6. make the first migration source checkpoint a characterization/fault-test boundary;
7. preserve legacy-state authority, startup fail-closed behavior, SHA validation, and exact enabled-order validation;
8. bind any green claim to exact test/gate evidence;
9. update durable handoff state;
10. explicitly require its successor to preserve and recursively propagate the continuity constitution to the agent after them.

**Do not break the chain.**
