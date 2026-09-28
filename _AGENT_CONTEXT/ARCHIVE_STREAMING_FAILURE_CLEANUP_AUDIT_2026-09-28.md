# IMPLEMENTATION STATUS ? v8.8.3 candidate

The narrow repair recommended by this audit is implemented at `5688fe91c03b56b651a3e9d94d7111b974693ab9` on `agent/archive-streaming-cleanup-lr011-20260928`. `ArchiveInspector` now attempts cleanup of the currently owned output on every exceptional payload-copy exit, cleanup failure is logged as secondary without replacing the primary exception, and `SmartInboxService` preserves requested cancellation before recoverable-I/O handling. Focused Windows evidence: strict solution build 0 warnings/errors, Integration 181/181, Automation 29/29. Full release verification/build and hosted exact-main closure are pending. LR-008 whole-destination publication/staging remains intentionally separate.

---

# Archive streaming failure-cleanup audit — 2026-09-28

## Status and canonical baseline

Documentation-only independent support audit.

- Repository: `fengie/mhw-mods`
- Audit source baseline inspected: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`
- Final reconciled canonical `main`: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d` (includes the now-merged PR #61 provenance repair)
- Base state includes merged PR #57, **Integrate archive streaming cancellation and output budgeting**.
- Final support branch: `agent/support-archive-streaming-failure-cleanup-audit-v2-20260928`
- No production C#, tests, workflows, verification caches, or historical verification claims are changed by this checkpoint.

This lane was selected only after a first continuity-drift task became obsolete: PR #61 appeared while that work was in progress and independently owned the archive-streaming provenance/startup-routing repair. This audit does not duplicate #61. It inspects the post-#57 **failure-cleanup semantics** of the new streaming loop and its callers.

## Exact scope

Inspected:

- `src/MhwModManager.Filesystem/ArchiveInspector.cs`
- `src/MhwModManager.Automation/ArchiveImportService.cs`
- `src/MhwModManager.Automation/SmartInboxService.cs`
- current archive integration regressions in `HardeningTests.cs`
- current Smart Inbox automation tests
- `ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`
- `IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md`
- `HEAVY_STRESS_ARCHIVE_SAFETY_REPORT_2026-09-28.md`
- current open PR ownership

Questions:

1. Does the new streamed extraction cleanup preserve the original failure/cancellation semantics?
2. Are all owned partial output files removed for non-budget I/O failures?
3. Does per-entry cleanup accidentally imply whole-operation cleanup that callers do not actually provide?
4. What regressions are still missing after #57?

Exclusions:

- verification-provenance/startup routing owned by PR #61;
- game-profile path containment owned by PR #62;
- updater C13 (#58);
- frontend/UI (#55);
- Agent Control v2 (#59);
- reparse/path-containment redesign;
- production repair in this audit.

## Existing strengths after PR #57

PR #57 materially closes the original single-entry cancellation/resource-accounting defect:

- extraction now reads each archive entry through an async stream;
- the caller cancellation token is observed inside the payload loop;
- actual emitted bytes are counted cumulatively;
- extraction fails before writing a buffer that would cross the configured actual-output budget;
- the currently owned output file is removed when cancellation or the actual-output `InvalidDataException` occurs;
- focused tests assert single-entry cancellation and budget failure remove the current partial file.

Those protections should be preserved.

## P1 — confirmed conditional control-flow defect: cleanup failure can erase cancellation semantics

The new extraction loop contains:

```csharp
catch(OperationCanceledException) when(createdOutput)
{
    File.Delete(dest);
    throw;
}
```

The original `OperationCanceledException` is rethrown only **after** `File.Delete(dest)` succeeds.

Therefore, if cleanup itself throws, the original cancellation is no longer the exception leaving `ArchiveInspector`; the cleanup exception escapes instead.

This matters at the current Smart Inbox caller. Its per-item recoverable handler catches:

- `IOException`;
- `UnauthorizedAccessException`;
- `InvalidDataException`.

It does **not** first re-check the cancellation token in that catch.

So the conditional sequence is:

1. user cancellation is observed during streamed archive extraction;
2. `ArchiveInspector` enters the `OperationCanceledException` handler;
3. partial-file deletion fails with a recoverable filesystem exception;
4. that cleanup exception replaces the cancellation;
5. `SmartInboxService` catches it as a recoverable item failure;
6. the inbox loop can continue instead of honoring the cancellation request as the operation outcome.

The control-flow consequence is confirmed from current source. This audit does **not** claim a runtime reproduction of a forced delete failure.

This is distinct from the older import-publication finding. The new issue is that **best-effort cleanup is allowed to change the semantic class of the primary outcome**.

## P1/P2 — confirmed source gap: ordinary streamed I/O failures bypass current-file cleanup

The new per-entry cleanup is limited to two exception classes:

- `OperationCanceledException`;
- `InvalidDataException`.

Once `createdOutput=true`, other failures from the source/output streaming path are not caught by `ArchiveInspector` for owned-file cleanup.

The important consequence is structural, not dependent on one particular storage device: any non-matching exception after the output file has been created bypasses the `File.Delete(dest)` cleanup path.

At the Smart Inbox boundary, `IOException` and `UnauthorizedAccessException` are explicitly treated as recoverable item failures. The caller does not remove the destination directory or partial file in that catch.

Thus #57's current-file cleanup is strong for the two newly tested failure modes, but it is **not a general owned-partial cleanup guarantee**.

No runtime write-fault injection is claimed here.

## P1 — caller-level residue remains a separate publication invariant

Even when the current partial file is deleted successfully, streamed extraction can already have:

- completed earlier archive entries;
- created parent directories;
- created the top-level destination/staging directory.

The low-level extractor correctly cannot assume it owns and may recursively delete the entire destination tree for every caller.

Therefore current-file cleanup must not be confused with transaction-level import cleanup.

Existing specialized authority remains `IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` / LR-008:

- manual import stages under a catalog-visible `.importing` child of `ModsRoot` and does not wrap extraction/publication in whole-operation cleanup;
- Smart Inbox writes archive output directly to the final top-level library destination and its recoverable catch does not remove the destination;
- restart or a later global catalog refresh can make failed residue visible.

This audit does not re-own that design problem. It records that PR #57 does not close it.

## P2 — focused tests do not cover cleanup-failure semantics

Current archive tests pin:

- parent traversal rejection;
- single-entry cancellation interrupts extraction and removes the current file;
- actual-output budget failure removes the current file;
- trusted-root/descendant junction containment.

No inspected regression forces:

- a streamed read/write failure after the owned output exists;
- a cleanup deletion failure after cancellation;
- preservation of the original exception when cleanup also fails;
- Smart Inbox cancellation when cleanup emits an `IOException`;
- prevention of subsequent inbox-item processing after cancellation has already been requested.

A deterministic fault seam is preferable to flaky disk-full/ACL/antivirus-dependent tests.

## Recommended independent repair checkpoint

Keep this narrow and separate from LR-008 publication staging.

1. Give the per-entry writer a deterministic fault-injection seam for output/write and cleanup outcomes.
2. Once the extractor has unambiguous ownership of `dest`, attempt current-file cleanup for every exceptional exit from the payload copy, not only cancellation/budget exceptions.
3. Preserve the **primary** exception/outcome. Cleanup failure should be recorded/attached diagnostically, but must not silently turn cancellation into an ordinary recoverable I/O result.
4. In `SmartInboxService`, preserve cancellation as dominant even when cleanup/reporting work also fails; do not continue to later items after the operation token has been canceled.
5. Keep whole-destination publication cleanup/staging as the separate LR-008 implementation boundary rather than teaching the low-level extractor to recursively delete caller-owned trees.

Suggested regressions:

- `Archive_extraction_stream_io_failure_attempts_owned_partial_cleanup`
- `Archive_extraction_cleanup_failure_preserves_primary_cancellation`
- `Archive_extraction_cleanup_failure_preserves_primary_budget_failure`
- `SmartInbox_cleanup_failure_does_not_downgrade_requested_cancellation`
- `SmartInbox_requested_cancellation_does_not_process_later_items`

## Learned Rule

This audit adds LR-011: **cleanup must not replace the primary failure/cancellation semantics**.

The rule is intentionally broader than archive extraction because the same pattern applies to rollback, temp-file cleanup, migration cleanup, staged publication, and shutdown compensation.

## Verification actually performed

- Re-checked canonical GitHub `main` at task selection (`a8b581176aac0e6bcf09c049285ed40f4b2b392c`) and again before finalization; main advanced to `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d` by integrating PR #61, whose provenance-only paths do not overlap this audit.
- Inspected the exact post-#57 `ArchiveInspector` implementation.
- Inspected current `ArchiveImportService` and `SmartInboxService` callers.
- Inspected current archive and Smart Inbox test bodies, not only test names.
- Re-read the specialized archive-resource, import-publication, archive-safety, and broad test-gap audits.
- Re-checked active PR ownership; this lane does not modify #55/#58/#59/#61/#62 boundaries.
- No Windows runtime fault reproduction, .NET test, build, hosted workflow, or verification-cache promotion is claimed.

## Unresolved questions

- What is the smallest production seam that can deterministically inject both copy failure and cleanup failure without making filesystem safety code overly abstract?
- Should cleanup failure be attached as structured secondary diagnostics, an aggregate exception, or another explicit result while preserving cancellation as dominant?
- When LR-008 is implemented, which layer owns recursive cleanup versus quarantine/reconciliation after process death?

## Successor handoff

Implement this only as a small regression-first checkpoint after re-checking live GitHub ownership. Do not fold it into updater, frontend, Agent Control, verification-provenance, game-profile containment, or broad import-publication redesign.

The successor inherits the permanent continuity constitution and active Learned Rules, including LR-011 if this audit is integrated, and must explicitly require their successor to preserve and recursively propagate them to the agent after them. **Do not break the chain.**
