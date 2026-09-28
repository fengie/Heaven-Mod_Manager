# Archive streaming cancellation and output-budget implementation — 2026-09-28

## Canonical baseline and assignment

- Canonical repository: `fengie/mhw-mods`; inspected canonical `origin/main`: `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Support branch: `agent/archive-streaming-budget-20260928`.
- Selected boundary: close the runtime-confirmed archive single-entry cancellation defect and add actual decompressed-output budgeting.
- Specialized prior evidence: `ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`.
- Deliberate exclusions: free-space reserve policy, compression-ratio policy, archive reparse containment, catalog publication, CAS, migration, diagnostics, networking, UI, updater work.

This was selected because current continuity named it the strongest unimplemented independent production boundary, while physical-root/reparse containment was already closed.

## Red reproduction

A regression was added before the production fix:
`Archive_extraction_cancellation_interrupts_single_entry_and_removes_partial_file`.

On pre-fix production behavior it failed exactly as expected: cancellation was requested after the output file began growing, but no `OperationCanceledException` was thrown and extraction returned success. Test-only checkpoint: `44157f2bc1e051dc4066f78414f1733905a10f90`.

## Implemented behavior

Production checkpoint: `b0be45beed223e0b0fd06ab719f696028581e626`.

`ArchiveInspector` now:
- streams entry payloads through `OpenEntryStreamAsync(ct)` instead of synchronous `WriteToFile`;
- passes the caller token into the actual decompression read and file write loop;
- preserves the existing entry-count, declared-size, trusted-root, path-traversal, and reparse checks;
- counts cumulative actual decompressed bytes and fails before the next write would exceed the actual-output budget;
- uses the existing 200 GiB ceiling as the default declared and actual-output limit, avoiding a guessed new product threshold;
- exposes `ArchiveExtractionLimits` so compact tests can exercise budget behavior without huge fixtures;
- deletes the output file it created when cancellation or an actual-output limit failure interrupts that entry;
- keeps the synchronous `ExtractSafely` surface as a compatibility wrapper over the async implementation.

A second regression, `Archive_extraction_enforces_actual_output_budget_and_removes_owned_partial_file`, proves the stream-level budget independently of archive metadata.

## Safety / compatibility evidence

Existing archive traversal regressions remained green:
- parent traversal rejection;
- junction ancestor rejection;
- descendant junction rejection before redirected parent creation.

The complete Integration suite passed 98/98 on the isolated branch.

No new Learned Rule was added: the company trainer already contains the generalized stream-level cancellation/resource-budget rule, so adding a project rule would duplicate existing durable doctrine.

## Local Windows verification

Exact production candidate `b0be45beed223e0b0fd06ab719f696028581e626` was verified on heaven2 / Windows / .NET SDK 10.0.401.

`scripts/Verify-Release.ps1`:
- 25/25 PASS;
- 616 functions, 0 trace gaps, 6555 explicit call sites / 0 uncovered, 0 parse errors;
- Automation 24/24;
- Integration/fault injection 98/98;
- self-test 11/11;
- strict builds/analyzers PASS.

`scripts/Build-Release.ps1`:
- Core 79/79;
- Automation 24/24;
- Integration/fault injection 98/98;
- self-test 11/11;
- App win-x64 compile/analyzers PASS;
- ReadyToRun self-contained publish PASS; fallback False;
- release ZIP SHA-256 `50B320A6352D2335D77D6864EA456FEE3FF582D82F5102338882C083CE7B2E40`.

The normal verifier promoted exact function fingerprints; no verification state was manually promoted.

## Not closed / unresolved

- This support branch is not canonical main until integrated.
- A hosted GitHub Windows Release Gate for the integrated/final candidate has not yet been recorded here.
- Destination free-space/headroom policy remains a separate product decision.
- Compression-ratio policy remains deliberately undecided.
- No malformed archive was proven to emit more bytes than SharpCompress reports; the implementation nevertheless enforces actual emitted bytes.

## Parallel-agent incident and recovery

A parallel updater agent switched a shared clone while this task was running. The archive production commit briefly landed on that local updater branch, but its push failed because the updater branch had no upstream. Before cleanup, the working tree was verified clean and the exact accidental commit was identified. The archive ref was advanced and pushed, the local updater branch was restored to `origin/main`, and all subsequent archive work moved to the isolated `mhw-archive-streaming-work` clone. No remote updater branch or unrelated work was overwritten.

## Successor handoff

Integrate this branch without importing stale continuity snapshots over newer main. Re-check current `origin/main`, preserve the two archive regressions and existing physical-containment tests, then run the repository-native hosted Windows gate for the exact integrated candidate before declaring the production boundary canonically closed.

Keep free-space reserve/ratio policy as a separate checkpoint. Preserve the permanent continuity constitution and require the successor to recursively pass it to the agent after them.

**Do not break the chain.**
