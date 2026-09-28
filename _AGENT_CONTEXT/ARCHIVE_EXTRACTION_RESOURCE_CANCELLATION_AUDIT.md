# Archive extraction resource/cancellation audit — 2026-09-28

## 2026-09-28 implementation status update

The core implementation checkpoint recommended by this audit is now **implemented on canonical `main`** through PR #57 / merge commit `a8b581176aac0e6bcf09c049285ed40f4b2b392c`. The current `ArchiveInspector` uses a cancellation-aware async entry-stream copy loop, enforces cumulative actual-output bytes before writes, and removes an owned partial output file on cancellation or actual-output-budget failure. Focused integration regressions cover those behaviors.

PR #57 records a full fresh local Windows release verification on integration head `fd8b48fc92e6f5e64591fd1938b7ccce5ac94083` (25/25 verifier; 728/728 functions; 7,772 / 0 uncovered call sites; Core 79/79; Automation 24/24; Integration 177/177; self-test 11/11; strict build/analyzers; ReadyToRun/updater-helper package; Build-Release PASS; artifact SHA-256 `AC3571853650CFA91243199B23A44007488F9244780FCBD18A7A38552B652734`).

At the separate support provenance checkpoint, exact merge commit `a8b581...` had no hosted workflow/status record. Keep that distinction explicit: this audit's former single-entry cancellation/actual-output implementation gap is closed in source and locally release-verified on the integration head, while exact-main hosted provenance remains pending. See `ARCHIVE_STREAMING_INTEGRATION_PROVENANCE_AUDIT_2026-09-28.md`.

The older text below is retained as historical evidence of the original defect and implementation design. Remaining free-space reserve/compression-ratio policy is still separate future work.

## Status and canonical baseline

Documentation-only support checkpoint. Source inspection and the runtime reproduction began from canonical `main` at `831da365c0c67e0239ad668f60fe3c78513dc63b`. During the task, canonical `main` advanced first to `ef9c0dc93ca5270e9ee5ab3dea080b2fcb461585` with the generic agent-training/continuity system, then to `2d6969c7d94438dd64a540dab93fc9a910a8458b` with hosted-verification evidence/cache persistence. Neither interval changes `src/` or `tests/`. This branch was rebased onto `2d6969c7d94438dd64a540dab93fc9a910a8458b` before final verification and push.

Selected because recursive source reparse containment is already being implemented independently on `agent/recursive-source-reparse-hardening-20260927`. That branch also advanced during this audit; it remains the owner of recursive reparse production changes. This audit deliberately does not edit `ArchiveInspector`, Smart Inbox, import publication, reparse handling, CAS, deployment, migration, or verification infrastructure.

## Support assignment

Audit the current archive ingestion boundary for availability/resource-safety guarantees that are independent of path containment:

- cancellation during decompression;
- declared versus actually written expansion accounting;
- total/entry limits;
- compression-ratio and disk-capacity controls;
- existing regression coverage;
- smallest future implementation/test checkpoint.

The primary implementation inspected is `src/MhwModManager.Filesystem/ArchiveInspector.cs`, with callers in `ArchiveImportService` and `SmartInboxService`.
## Existing strengths to preserve

`ArchiveInspector` already has meaningful defenses:

- `MaxEntries = 200_000`;
- `MaxExpandedBytes = 200 GiB`;
- both inspection and extraction independently enforce path safety;
- extraction re-checks destination containment and parent reparse components;
- callers inspect suspicious paths before extraction;
- archive parsing uses pinned SharpCompress `0.50.4`.

These are real controls. The issue below is not “archives are unbounded”; it is that the current resource/cancellation contract is incomplete.

## P1 — confirmed runtime: cancellation during one entry is ignored and the operation can return success

`ExtractSafelyAsync` wraps synchronous `ExtractSafely` in `Task.Run`. Inside `ExtractSafely`, the token is checked once before each entry. The actual payload copy is synchronous:

`e.WriteToFile(dest, new ExtractionOptions { ExtractFullPath=false, Overwrite=false });`

No cancellation token reaches that copy, and there is no token check after the write. For a one-entry archive, cancellation after the write begins can therefore be completely unobserved.

### Windows reproduction on heaven2

A disposable probe outside the repository created a 1 GiB zero-filled ZIP entry and started `ExtractSafelyAsync`. After the output file exceeded 8 MiB, the token was canceled.

Observed:

- compressed archive bytes: `10,375,931`;
- declared expanded bytes: `1,073,741,824`;
- cancellation requested at: `52 ms`;
- bytes already written at cancellation: `12,713,984`;
- task outcome: `SUCCESS`;
- final output bytes: `1,073,741,824`;
- task elapsed: `2,288 ms`.

This is runtime-reproduced behavior, not a theoretical risk.
## P1/P2 — resource ceiling is not tied to available disk

The 200 GiB declared-expansion ceiling is static. No inspected extraction path checks `DriveInfo.AvailableFreeSpace`, reserves headroom, or derives an operation budget from the destination volume.

At audit time the C: volume on heaven2 reported about 182.4 billion bytes free, below the configured 200 GiB archive ceiling. Thus “passes the archive limit” does not imply “can fit on the target volume.”

For manual import this is an availability/recovery problem. For Smart Inbox it is more consequential because processing is automated once an archive is placed in the inbox.

A future disk-space check is defense in depth, not a replacement for streamed byte accounting: free space is inherently race-prone and can change while extraction runs.

## P2 — limits are metadata-based, not output-stream enforced

Both inspection and extraction add `e.Size` before writing. The code does not count bytes that actually pass through the decompression stream.

SharpCompress exposes both uncompressed `Size` and `CompressedSize`, and its current API also exposes cancellable async entry streams. The current manager does not use those surfaces to enforce an actual-output budget.

Confirmed design gap:

- there is no per-write/cumulative output counter;
- there is no compression-ratio policy;
- there is no check that the bytes emitted for an entry remain within the budget used to authorize it.

Whether a malformed supported archive can cause SharpCompress 0.50.4 to emit more bytes than its reported `Size` was not reproduced in this audit and must not be stated as confirmed. The missing stream-level enforcement itself is confirmed from source.

## P2 — regression coverage does not pin resource/cancellation behavior

The inspected archive integration coverage proves parent traversal rejection. No current test found by repository-wide search asserts:

- cancellation after an entry starts;
- cancellation of a single large entry;
- output stops growing after cancellation;
- actual bytes written cannot exceed a configured operation budget;
- entry-count boundary behavior at/over the limit;
- expanded-byte boundary behavior at/over the limit;
- low-free-space refusal/reserve behavior;
- compression-ratio handling, if such a policy is adopted.
## Recommended independent implementation checkpoint

Keep this separate from recursive reparse hardening and import-publication repair.

1. Introduce a streamed extraction primitive for one archive entry.
2. Pass the caller cancellation token into the decompression/read/write loop.
3. Count **actual decompressed bytes written** per entry and cumulatively.
4. Fail closed before a write would exceed the operation budget.
5. On cancellation/resource-limit failure, close streams and remove the currently partial output file when ownership is unambiguous.
6. Preserve the existing lexical/reparse/path checks before opening each output.
7. Add a destination-volume free-space preflight with explicit reserve/headroom; treat it as advisory/race-prone defense in depth.
8. Decide compression-ratio policy separately from absolute byte limits. If used, make exceptions explicit for legitimate large archives rather than silently weakening the byte budget.

SharpCompress 0.50.x documents async archive/entry-stream APIs that accept cancellation tokens, so a fix should prefer those capabilities or an equivalent manual async copy loop instead of adding more outer `Task.Run` cancellation checks.

Do not pick an exact new size/ratio threshold by guesswork in the first source edit. Make limits configurable/testable or derive them from an explicit product policy.

## Exact regression backlog

- `Archive_extraction_cancellation_interrupts_single_entry_and_does_not_return_success`
- `Archive_extraction_cancellation_stops_output_growth_and_removes_owned_partial_file`
- `Archive_extraction_enforces_actual_output_budget_not_only_entry_metadata`
- `Archive_inspection_rejects_entry_count_above_limit_and_accepts_exact_limit`
- `Archive_inspection_rejects_expanded_bytes_above_limit_and_accepts_exact_limit`
- `Archive_extraction_refuses_when_destination_budget_exceeds_safe_free_space`
- optional, only if policy is adopted: `Archive_inspection_rejects_pathological_compression_ratio`

Use compact synthetic fixtures. Do not create multi-hundred-gigabyte test artifacts to prove the 200 GiB boundary.
## Parallel-agent boundaries

The active recursive-source branch owns ModScanner / unmanaged adoption / Smart Inbox reparse traversal. Do not modify those semantics from this checkpoint.

Parallel branch `agent/support-archive-hardening-audit-20260928` at `7d49a05` independently runtime-reproduces archive destination-root ancestor-junction escape. That audit explicitly leaves single-entry cancellation as an unmeasured gap; this audit supplies the separate runtime cancellation/resource-budget evidence. Preserve the two findings as complementary rather than merging their scopes.

`IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` remains authoritative for whether failed/canceled import residue becomes catalog-visible. This audit owns the lower-level fact that archive decompression itself does not currently honor cancellation during an entry.

`WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` remains authoritative for archive reparse/physical-containment issues. This audit does not supersede it.

The broad `TEST_GAP_AND_PERFORMANCE_AUDIT.md` already marked archive cancellation/bomb-limit coverage as a gap. This document is the specialized follow-up that runtime-reproduces the single-entry cancellation failure and scopes a concrete repair.

## Verification actually performed

- cloned and fetched current canonical repository;
- clean working tree on branch creation from `831da365c0c67e0239ad668f60fe3c78513dc63b`;
- read actual `ArchiveInspector`, `ArchiveImportService`, `SmartInboxService`, tests, package pins, and prior support audits;
- confirmed SharpCompress pin `0.50.4`;
- `dotnet restore MhwModManager.sln`: PASS on Windows / .NET SDK 10.0.401;
- direct xUnit v3 run of `Archive_extraction_rejects_parent_traversal`: **1/1 PASS**;
- disposable 1 GiB compressed-entry cancellation probe: reproduced cancellation request followed by full extraction and successful task completion;
- initial `dotnet test --filter` attempts selected zero xUnit v3 tests; they are recorded as non-evidence and were replaced by the direct xUnit v3 method run;
- re-fetched canonical state before push and rebased onto `2d6969c7d94438dd64a540dab93fc9a910a8458b`; its post-`ef9c0dc` delta is hosted-verification evidence/cache only;
- inspected new parallel `agent/support-archive-hardening-audit-20260928` / `7d49a05` and confirmed its runtime-reproduced destination-root containment finding is complementary, not duplicate, to this cancellation/resource checkpoint.

No production source, tests, verification scripts, workflows, or verification caches were modified by this support checkpoint.
## Not verified / unresolved

- No full repository verifier or release build was required solely to establish the documentation finding; run the normal exact Windows gate if this audit is integrated with any production/test change.
- No malformed archive was proven to emit more bytes than SharpCompress reports in `Size`; keep that risk labeled unverified until a deterministic fixture exists.
- No product decision was made for a new absolute byte cap, ratio cap, or required free-space reserve.
- No process-death or catalog-publication behavior is claimed here; use the specialized import-publication audit for those semantics.

## Durable lesson

The concrete lesson is narrower than a new global Learned Rule at this time:

> An outer cancellation token and a metadata preflight do not bound a decompression operation unless the token and byte budget are enforced inside the actual streaming copy.

No LR-010 is added here to avoid manufacturing a ledger rule from one specialized checkpoint while parallel branches are active. If implementation confirms this pattern recurs elsewhere, add a uniquely numbered rule then.

## Successor handoff

Before implementation, re-check canonical `main` and current PRs. Preserve the permanent continuity constitution and all active Learned Rules. Add the regression that reproduces cancellation-during-one-entry first, then implement only the streamed cancellation/resource-budget seam. Keep reparse containment, catalog-invisible publication, CAS, migration, diagnostics, networking, and UI work separate.

Any production source edit starts a new exact verification boundary and must earn the repository's full Windows verification/release evidence before closure.

The successor must explicitly require its own successor to inherit, preserve, and recursively propagate the same continuity system to the agent after them.

**Do not break the chain.**
