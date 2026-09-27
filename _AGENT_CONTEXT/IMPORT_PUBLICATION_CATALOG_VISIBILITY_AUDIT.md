# Import publication and catalog-visibility integrity audit — 2026-09-27

## Status

**Documentation-only independent support audit. No production behavior changed.**

Canonical `fengie/mhw-mods` `main` inspected at task lock:

`4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`

That canonical HEAD persists hosted Windows verification evidence for source
`5619604e88a27176726ada8518f53d385abc7b0f` / run `36340312353`.

This audit was selected only after checking the current branch/PR map. It deliberately does not duplicate:

- PR #15 / `MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`, which owns successful mod retirement and stale semantic references;
- `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`, which owns reparse traversal, physical containment, CAS integrity, and native replacement safety;
- `ASYNC_LIFETIME_CANCELLATION_AUDIT.md`, which owns shell task lifetime and foreground/background coordination;
- PR #13 Smart Pack work, which owns provider-neutral selection/planning;
- PR #14 launch-health/game-build revalidation.

This document owns a narrower invariant:

> **A library import is not published merely because a directory exists under ModsRoot. Partial, failed, canceled, or crash-interrupted import state must remain invisible to the catalog until the import is complete.**

---

# 1. Why this lane was selected

The broad filesystem and test-gap audits already note missing archive failure/collision/cleanup tests, but they do not trace the current publication boundary end-to-end.

Current source has two import paths:

1. manual archive import through `ArchiveImportService`;
2. automatic archive/folder import through `SmartInboxService`.

Both ultimately place filesystem state under the same `ModsRoot` enumerated by
`CatalogService.RefreshFoldersAsync`.

The important discovery is not only that failed extraction can leave files behind.
It is that **catalog visibility is based solely on being a top-level directory under
`ModsRoot`**, so an unfinished directory can later become a persisted mod row.

That turns an ordinary recoverable extraction/copy failure into durable library-state contamination.

---

# 2. Internal assignment

## Exact scope

Inspect:

- `ArchiveInspector.Inspect/ExtractSafely`;
- `ArchiveImportService.ImportAsync`;
- `SmartInboxService.ProcessAsync`, `CopyDirectoryAsync`, and wrapper normalization;
- `CatalogService.RefreshFoldersAsync`;
- application startup ordering around catalog refresh and automation;
- manual import command orchestration;
- current archive/inbox regression coverage;
- current parallel support branches and overlapping audits.

## Questions

1. Is incomplete import state physically isolated from catalog-visible library state?
2. What remains after extraction/copy failure or cancellation?
3. Can that leftover state be persisted as a normal mod later?
4. Does retry converge cleanly, or can stale folders/rows survive?
5. Which existing safeguards are strong and should be preserved?
6. What is the smallest independently verifiable future implementation boundary?

## Exclusions

This audit does **not**:

- redesign reparse handling or Windows physical containment;
- implement archive decompression limits beyond current policy;
- redesign SharpCompress or add another archive library;
- change global mod identity;
- fix successful mod deletion/referential lifecycle;
- change Smart Pack/provider acquisition;
- modify production C#, tests, schema, verification cache, or workflows.

---

# 3. Executive result

## P1 — confirmed: Smart Inbox writes unfinished imports directly into catalog-visible ModsRoot

`SmartInboxService.ProcessAsync` chooses:

`destination = Unique(Path.Combine(modsRoot, name))`

before import work begins.

For a directory item it calls:

`CopyDirectoryAsync(entry, destination, ct)`

For an archive item it calls:

`archive.ExtractSafelyAsync(entry, destination, ct)`

Both paths create/write the **final library directory itself** before the item has succeeded.

Recoverable exceptions are caught at the per-item level:

- `IOException`;
- `UnauthorizedAccessException`;
- `InvalidDataException`.

The catch records a failed result, but does not delete or quarantine `destination`.

Therefore a failed import can leave a partial top-level folder under `ModsRoot`.

This is directly confirmed by source control flow. No runtime reproduction is claimed.

## P1 — confirmed: a failed Smart Inbox folder can be registered as a real mod

If any item in that same Inbox run succeeds, `SmartInboxService` later calls:

`catalog.RefreshFoldersAsync(ct)`

That catalog refresh enumerates **every top-level directory under `modsRoot`** and inserts any path not already present in the DB.

It has no notion of:

- committed import;
- incomplete import;
- staging name;
- readiness marker;
- failed Inbox item.

So a partial directory left by failed item A is catalog-visible when successful item B causes the global refresh.

The successful-import filtering that follows does not undo this:

`importedPaths` is built only from successful results, so `EnsureCapturedAsync` captures only successful imported paths.

Resulting state can therefore be:

- failed item A: partial directory exists;
- catalog creates a mod row for A;
- A is not captured because it was not successful;
- successful item B proceeds normally.

That is a **ghost/partial mod descriptor created from a failed import**.

## P1 — confirmed: even a lone failed import can contaminate the next startup

The same defect does not require another Inbox item to succeed.

Application startup currently performs:

1. `catalog.RefreshFoldersAsync`;
2. intelligence/build work;
3. deployment recovery;
4. startup automation / Smart Inbox.

Therefore any partial directory left under `ModsRoot` by a previous failed or canceled Smart Inbox operation is enumerated by the next startup **before** Smart Inbox gets another chance to retry or report it.

The failed partial folder can thus become a persisted mod row on restart.

## P1 — confirmed: manual archive “staging” is under ModsRoot and is catalog-visible on restart

`ArchiveImportService.ImportAsync` is safer than Smart Inbox in one important way:

- extraction goes to `destination + ".importing"`;
- wrapper normalization happens there;
- successful publication uses `Directory.Move(staging, destination)`.

However, `staging` is still a direct child of `ModsRoot`.

There is no `try/finally` cleanup around extraction/normalization/publication.

If extraction or normalization fails, or cancellation occurs after staging creation,
the `.importing` directory can remain.

At next startup, `CatalogService.RefreshFoldersAsync` has no reserved-name exclusion and will treat that `.importing` directory like any other mod source directory.

The UI describes this path as a “quarantined staging folder,” but the current catalog boundary does not actually quarantine it from discovery.

## P1/P2 — confirmed: retry can leave persistent DB ghosts because catalog refresh is add-only

`CatalogService.RefreshFoldersAsync` adds newly discovered source directories.
It does not delete a DB mod merely because a source directory later disappears.

Manual import retry does this at the start:

`if (Directory.Exists(staging)) Directory.Delete(staging, true);`

If a prior startup already registered the stale `.importing` directory as a mod row,
the retry can delete that physical staging folder and proceed to a successful final destination, but the already-created DB row for the old staging path is not removed by catalog refresh.

So the sequence can be:

1. failed manual import leaves `Foo.importing`;
2. restart catalogs `Foo.importing` as a mod;
3. retry deletes `Foo.importing`;
4. retry succeeds and publishes `Foo`;
5. catalog adds `Foo`;
6. stale DB row for vanished `Foo.importing` remains.

This is a durable convergence problem, not only temporary disk litter.

## P2 — confirmed: Smart Inbox retries can accumulate suffixed partial destinations

Smart Inbox uses `Unique(...)` against both files and directories.

A failed attempt that leaves `Foo` behind means the next retry can choose `Foo (2)`.
If that succeeds, a later global catalog refresh can see both:

- the old partial `Foo`;
- the successful `Foo (2)`.

Repeated failures can continue producing suffixed destinations.

This is secondary to the catalog-visibility defect but makes recovery less convergent.

---

# 4. Existing strengths to preserve

This audit should not erase the safety work that already exists.

## Archive path validation is performed both before and during extraction

Manual and Smart Inbox archive paths use `ArchiveInspector.InspectAsync` first.

`ArchiveInspector.ExtractSafely` then validates each entry again before writing.

That is stronger than trusting inspection metadata as a one-time authorization.

## ArchiveInspector has explicit expansion limits

Current limits:

- 200,000 entries;
- 200 GiB declared expanded bytes.

Those limits are useful even though malformed/collision/stream-behavior coverage is still incomplete.

## Archive extraction has reparse-aware parent checks

`ArchiveInspector.ExtractSafely` rejects an existing reparse destination and walks parent components before each write.

The Windows filesystem audit remains the authority for its remaining TOCTOU limitations.

## Manual import already has a publish-after-normalize shape

`ArchiveImportService` extracts and normalizes before the final:

`Directory.Move(staging, destination)`

That is the right general shape for a commit-on-success import.

The defect is that staging lives inside the catalog-visible namespace and lacks failure cleanup/visibility isolation.

## Inbox source is only moved to Processed after success

`SmartInboxService.MoveToProcessed(entry)` runs only after the destination is imported/classified.

That preserves the source for retry after a recoverable failure.

A future fix should keep this property.

---

# 5. Catalog boundary is the real invariant

The core design mismatch is:

- import services think in terms of **in-progress vs published**;
- `CatalogService` thinks **every directory under ModsRoot is published**.

There is no durable commit marker between those models.

The manager currently has no contract equivalent to:

> A directory is eligible for catalog discovery only after the import workflow has completely validated, normalized, and committed it.

Naming a directory `.importing` is not a quarantine if the catalog enumerates it.

A robust solution should make publication explicit rather than teaching every caller to hope cleanup always succeeds.

---

# 6. Failure/cancellation paths

## Smart Inbox archive failure

Possible control flow:

1. final destination directory is created by extraction;
2. one or more entries are written;
3. later entry causes `IOException` / `InvalidDataException` / access failure;
4. per-item catch records failure;
5. destination remains.

If another item succeeds, same-run catalog refresh can register it.

Otherwise next startup can register it.

## Smart Inbox directory-copy failure

`CopyDirectoryAsync` creates the final destination, then recursively creates directories and copies files.

If a later copy fails, the recoverable catch records the failed item but does not remove the partial destination.

The reparse-recursion risk in this method is already owned by the Windows filesystem audit. This audit owns the separate failure-publication consequence.

## Cancellation

Both manual import and Smart Inbox pass the caller token into extraction/copy.

Neither path has a cleanup `finally` that establishes the postcondition:

- no catalog-visible partial import exists after cancellation.

Smart Inbox does not swallow caller cancellation, which is correct for cancellation semantics, but the filesystem postcondition remains undefined.

## Process death / crash

A crash can bypass ordinary exception cleanup even if cleanup is later added.

Therefore “delete temp in catch/finally” alone is not a sufficient publication model if the staging location remains catalog-visible.

Crash safety is strongest when incomplete state is structurally outside the discovery namespace.

---

# 7. Existing test coverage and gaps

## Existing useful coverage

`HardeningTests.Archive_extraction_rejects_parent_traversal` proves a `../` entry is rejected and does not create the outside target.

`AutomationServiceTests.SmartInboxImportsDirectoryAndLeavesProcessedCopy` exercises a normal successful direct-directory Inbox import.

Those are useful, but neither asserts commit-on-success visibility.

## Missing focused regression coverage

No inspected test proves:

- failed Smart Inbox archive import leaves no catalog-visible destination;
- failed Smart Inbox directory copy leaves no catalog-visible destination;
- cancellation leaves no catalog-visible partial import;
- a failed item followed by a successful item cannot be registered by the global refresh;
- stale manual `.importing` state is ignored/cleaned rather than cataloged at startup;
- retry after a failed manual import does not leave a stale DB mod row;
- process-crash residue remains outside the catalog namespace;
- successful publication creates exactly one visible source directory.

The broad test-gap audit already asks for malformed/collision/cleanup/retry cases.
This audit supplies the missing end-to-end state invariant those tests should assert.

---

# 8. Exact regression backlog

## A. Mixed-result Smart Inbox contamination

### `SmartInbox_failed_item_is_not_cataloged_when_later_item_succeeds`

Arrange:

- Inbox item A fails after creating some destination state;
- Inbox item B imports successfully.

Act:

- run `SmartInboxService.ProcessAsync`.

Assert:

- result reports A failed and B imported;
- DB contains B;
- DB does not contain A or any partial/staging source;
- no catalog-visible partial A directory remains.

The fault mechanism should be deterministic. If current archive formats do not provide a stable cross-platform partial-write fixture, add a narrow injectable import/publish seam rather than depending on undefined parser failure timing.

## B. Restart after failed Smart Inbox import

### `Startup_catalog_does_not_publish_failed_inbox_residue`

Arrange failed import residue representing an interrupted attempt.

Act with the same catalog-before-maintenance ordering used by startup.

Assert the residue cannot become a normal mod row.

## C. Manual archive staging failure

### `ArchiveImport_failure_never_exposes_importing_folder_to_catalog`

Fault after staging creation.

Assert:

- final destination absent;
- staging either removed or stored outside catalog root;
- catalog refresh cannot create a mod row for it.

## D. Manual cancellation

### `ArchiveImport_cancellation_never_publishes_partial_mod`

Cancel after import work begins.

Assert the same visibility contract as ordinary failure.

## E. Manual retry convergence

### `ArchiveImport_retry_after_failure_produces_one_mod_and_no_staging_ghost`

Sequence:

1. fail first attempt;
2. simulate restart/catalog refresh;
3. retry successfully.

Assert exactly one live catalog mod for the import and zero stale staging-source rows.

## F. Smart Inbox directory-copy failure

### `SmartInbox_directory_copy_failure_isolation_matches_archive_failure`

Fault midway through direct directory copy.

Assert the final library namespace remains unchanged.

This test is separate from the Windows reparse tests already proposed elsewhere.

## G. Process-death residue characterization

### `Incomplete_import_workspace_is_not_catalog_discoverable_after_restart`

Create the exact on-disk state expected after process death during staging.

Without relying on cleanup code, run catalog discovery.

Assert zero import residue becomes a mod.

This test is the strongest reason to stage outside `ModsRoot`.

## H. Successful single publication

### `Completed_import_publishes_exactly_once_after_normalization`

Assert that before final commit the import is invisible, and after the commit exactly one final source is discoverable.

---

# 9. Recommended first production checkpoint

Do **not** fix this by adding scattered `Directory.Delete` calls only.

The smallest coherent boundary is:

## Catalog-invisible import staging + commit-on-success publication

Recommended behavior:

1. allocate import staging in a manager-owned workspace **outside `ModsRoot`**;
2. extract/copy there;
3. normalize wrapper layout there;
4. run any pre-publication classification/validation that does not require a catalog row;
5. choose the unique final destination under `ModsRoot`;
6. publish by one final directory move only after all required import work succeeds;
7. move Inbox source to Processed only after publication succeeds;
8. best-effort clean staging on ordinary failure/cancellation;
9. on process death, leave only catalog-invisible staging residue that can be cleaned/retried later;
10. refresh catalog only after publication.

Prefer one shared publication helper/service used by both:

- `ArchiveImportService`;
- `SmartInboxService`.

That prevents the two paths from drifting into different failure semantics.

### Why outside ModsRoot is preferred

A reserved suffix or catalog ignore list can reduce risk, but it still couples safety to naming conventions.

Staging outside the discovery root gives a structural invariant:

> crashes can leave garbage, but garbage cannot become a mod merely by existing.

If a same-volume atomic directory rename is required, choose a workspace topology that preserves the intended move semantics and verify it on supported Windows layouts. Do not silently trade publication integrity for cross-volume copy behavior.

---

# 10. What not to combine with the first fix

Keep these independent:

- parent-junction/reparse hardening;
- `ReplaceFileW` characterization;
- CAS trust hardening;
- successful mod retirement / PR #15;
- global mod-ID redesign;
- Smart Pack acquisition;
- migration retry redesign;
- backup/restore capsule work;
- broad MainWindow refactoring.

A focused import-publication checkpoint should be testable without changing deployment, planner, or schema semantics.

---

# 11. Learned-rule consequence

This audit justifies a durable engineering rule:

> **Import publication requires catalog-invisible staging and a commit-on-success visibility boundary. Failed, canceled, or crash-interrupted work must not become discoverable merely because partial files exist.**

The active parallel mod-lifecycle branch PR #15 already reserves **LR-007**.

To avoid a parallel Rule-ID collision, this support branch records the import-publication rule as **LR-008** and explicitly tells the integration agent to preserve LR-007 and LR-008 as distinct lessons.

If PR #15 is abandoned rather than integrated, do not silently renumber history after this branch is shared; resolve numbering explicitly during integration while preserving both rule texts.

---

# 12. Verification actually performed

Performed:

- established canonical GitHub `main` and re-checked it after it advanced during this task;
- final audit base locked to `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3`;
- inspected recent commits, branches, open PRs, and the support-integration inventory;
- read permanent continuity/read-order documents and active LR-001 through LR-006;
- inspected the active PR #15 lifecycle lane to avoid duplication;
- inspected actual source bodies for:
  - `ArchiveInspector`;
  - `ArchiveImportService`;
  - `SmartInboxService`;
  - `CatalogService`;
  - `AutomationCoordinator`;
  - `App.OnStartup`;
  - manual `ImportArchive` command;
- inspected existing archive/inbox assertions in:
  - `HardeningTests.cs`;
  - `AutomationServiceTests.cs`;
- cross-checked findings against:
  - `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`;
  - `TEST_GAP_AND_PERFORMANCE_AUDIT.md`;
  - `ASYNC_LIFETIME_CANCELLATION_AUDIT.md`;
  - PR #15 mod-lifecycle audit.

Not performed:

- no local checkout / `git status`;
- no runtime fault injection;
- no archive-corruption reproduction;
- no `dotnet test`;
- no Windows process-death fixture;
- no new hosted Windows Release Gate;
- no verification-cache promotion.

Therefore all “confirmed” findings in this audit mean confirmed by current source/control-flow and persistence ordering, not that the failure has been observed in a user's installation.

---

# 13. Unresolved questions for the implementation agent

1. What manager-owned staging root provides the best same-volume behavior across portable and installed layouts?
2. Should stale external staging be automatically pruned by age/run ownership, or surfaced to diagnostics first?
3. Which deterministic test fault seam is smallest:
   - injectable copy/extract publisher;
   - internal fault hook used only by tests;
   - stable malformed/collision archive fixture?
4. Should CatalogService also explicitly reject reserved manager work directories as defense in depth even after staging moves outside ModsRoot?
5. Does any future acquisition provider need a common “publish package” API so Smart Pack cannot bypass this boundary?

Do not answer these by broad refactor before the regression contract exists.

---

# 14. Parallel-agent integration notes

## PR #15 — mod lifecycle / referential integrity

PR #15 owns what happens when a **successfully existing mod is retired**.

This audit owns what happens when a **new import never successfully commits**.

Do not merge these into one database/filesystem lifecycle rewrite.

PR #15 reserves LR-007. Preserve its rule and this branch's LR-008.

## Windows filesystem safety audit

That audit remains authoritative for:

- reparse traversal;
- direct Inbox directory recursion through reparse points;
- archive extraction TOCTOU;
- CAS root/object safety;
- physical containment.

This audit uses its findings but does not supersede them.

## Async/lifetime audit

That audit remains authoritative for cancellation/shutdown coordination.

This audit only requires a deterministic filesystem/catalog postcondition when an import operation ends or the process dies.

## Smart Pack / provider work

Any future downloaded package should enter the library through the same commit-on-success publication boundary rather than writing provider output directly into `ModsRoot`.

---

# 15. Successor handoff

If implementing this finding:

1. verify actual canonical `main`, PRs, and active Learned Rules first;
2. read this audit plus the Windows filesystem and test-gap audits;
3. preserve PR #15's separate mod-retirement boundary;
4. add the mixed-success, restart, cancellation, retry, and single-publication regressions first;
5. introduce one catalog-invisible staging/publication boundary shared by manual import and Smart Inbox;
6. do not weaken ArchiveInspector path/reparse safeguards;
7. do not change deployment/planner/schema semantics in the same checkpoint;
8. any production C# change starts a new exact verification boundary and requires the full hosted Windows Release Gate;
9. update durable handoff state with exact evidence;
10. explicitly require your successor to inherit and recursively pass the continuity constitution to the agent after them.

**Do not break the chain.**

---

# 16. Post-audit upstream reconciliation

After source inspection was completed, canonical `main` advanced from `4de981ab7ee47a3a8f0dd60f38e17644517b2ea3` to `208a66da89632acf36c065dc3bfead76af8d6bf4` while this branch was being written.

A direct comparison showed six documentation/continuity-only commits touching handoff/verification documents. No `src/`, `tests/`, workflow, verifier, schema, or verification-cache input changed in that upstream interval, so the import source bodies inspected by this audit remained unchanged. This branch was merged onto the newer canonical handoff state before its unique audit changes were reapplied.

The active PR map was rechecked as well. PR #16 added a crash-bisector diagnosis evidence audit, which does not overlap this import-publication boundary.

---

# 17. Live-deployment checkpoint reconciliation

Canonical `main` then advanced again to `356fde242046b78e39c7266c57b27e52220141fa` with the separately scoped live-deployment reparse-containment checkpoint.

The `208a66d..356fde2` diff changed `DeploymentExecutor.cs`, focused `HardeningTests.cs`, and continuity/audit documents. It did **not** change `ArchiveInspector.cs`, `ArchiveImportService.cs`, `SmartInboxService.cs`, `CatalogService.cs`, application startup ordering, or the manual import command. Therefore the source/control-flow findings in this audit remain current, while the newly integrated deployment containment work stays authoritative for its own boundary.

This branch again merged canonical main before reapplying its documentation-only audit state. No production import code was modified here.
