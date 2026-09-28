# Heavy stress-testing safety report — 2026-09-28

## Scope and canonical baseline

This pass audited the existing safety test surface before changing production code. Canonical `origin/main` was `b34bb5a3e2b5fe1b5fc69db83cc6ab19f84297eb`; implementation work was isolated on `agent/recursive-source-reparse-hardening-20260927`.

The highest-value unclosed boundary was the already-audited recursive source reparse risk in ModScanner, unmanaged adoption, and Smart Inbox. CAS byte integrity, live DeploymentExecutor containment, and native ReplaceFileW failure semantics were already separately closed and were not reopened.

Baseline on real Windows `heaven2`:
- Integration/fault-injection: **89/89 PASS**
- Automation tests: **20/20 PASS**
- existing deployment/CAS/rollback/ReplaceFileW hardening remained green.

## Existing coverage map

- Core/unit: planner, conflict/rules, path rules, compatibility, game profiles, texture safety, logical families.
- Automation: save backup, update diff, duplicate analysis, recipes, effective-provider/diagnosis, Smart Inbox happy paths.
- Integration: deployment/journal/rollback/recovery, CAS integrity, archive/path hardening, Windows replacement behavior, planner snapshots, multi-game behavior, function verification, UI/XAML safety.
- Windows-specific: real junction fixtures, ReplaceFileW behavior, Restart Manager/locked-file paths, ReadyToRun release build.
- Persistence/database: schema/self-test, deployment transaction/journal state, planner snapshots, adoption records, timeline/last-known-good.
- Crash/interruption: deployment fault injection and startup recovery are meaningful; broader multi-file crash matrices and statement-level SQLite failure injection remain incomplete.
- Concurrency/stress: CAS concurrency has focused coverage; broader install/uninstall/enable/disable races and large-scale workloads remain only partially covered.
- Update/migration: release/build verification is strong; restartable migration interruption/idempotency remains an audited future risk.

## Gap reproduced

All three recursive source consumers used `SearchOption.AllDirectories` without a reparse policy:
- `ModScanner` could scan and capture bytes reached through a junction.
- `UnmanagedAdoptionService` could discover/copy external bytes and persist adoption state.
- `SmartInboxService` could copy a junction target into Mods and report the item imported.

Tests were added before the production fix. On unmodified production code:
- `Scanner_rejects_descendant_junction_before_capture` failed because no exception was thrown.
- `Adoption_rejects_descendant_junction_before_copy_or_record` failed because no exception was thrown.
- `SmartInboxDirectDirectoryRejectsDescendantJunctionWithoutPublishingPartialMod` failed with Imported expected 0, actual 1.

These failures were reproduced using real Windows directory junctions on `heaven2`, not mocks.
## Minimal production repair

Added one shared `SafeRecursiveTraversal.Snapshot` primitive in Filesystem. It:
- checks the traversal root for `FileAttributes.ReparsePoint`;
- enumerates one directory level at a time;
- checks every entry before recursion/open;
- rejects directory and file reparses with `IOException`;
- checks cancellation between entries;
- materializes the complete safe tree before callers mutate durable state.

ModScanner and adoption now consume the shared snapshot. Smart Inbox preflights the full source tree before creating its final destination, then uses the same traversal for destination classification. No unrelated feature or architecture work was performed.

## Regression postconditions

Scanner regression proves:
- reparse traversal is rejected;
- external sentinel bytes remain unchanged;
- no CAS blob is created by the failed scan;
- no `mod_files` rows are committed.

Adoption regression proves:
- reparse traversal is rejected;
- external bytes remain unchanged;
- no managed source package is created;
- no adoption success rows are committed.
Smart Inbox regression proves:
- the item is not imported and is counted skipped/failed;
- source stays in Inbox;
- no final Mods destination remains;
- no mod row is published;
- external bytes remain untouched.

## Verification executed

Focused real-Windows post-fix runs:
- Integration: **91/91 PASS**
- Automation: **21/21 PASS**
- both projects build with **0 warnings / 0 errors**.

First-pass exact local verification source after reconciling canonical `main`: `ba8b9a049b9b6e1c68cf24cbeb337b9e91d5dfd5` (superseded by the adversarial follow-up section below).

Repository verifier on Win32NT / Windows 10.0.26200 / .NET SDK 10.0.401:
- **25/25 PASS**
- functions **614 total / 614 verified after promotion**
- explicit call sites **6512 / 0 uncovered**
- trace gaps **0**
- parse errors **0**
- Integration/fault injection **91/91**
- Automation **21/21**
- full automation self-test **11/11**
- strict whole-solution/analyzer build PASS.

Release build:
- Core **79/79**
- Automation **21/21**
- Integration **91/91**
- self-test **11/11**
- win-x64 compile/analyzers PASS
- ReadyToRun self-contained publish PASS, fallback false
- release ZIP SHA-256: `7B46BF7CBC85F4818B49D478613E3FE20F5F83E98E416F60E2D9F79D03E7F686`.

Company-trainer review: no new generic trainer rule was added. The reusable lessons exposed here—physical containment is distinct from lexical containment, destructive/stateful traversal should fail closed, publication should be commit-on-success, and failure tests must assert durable postconditions—are already captured in `_AGENT_TRAINING/SAFETY_AND_DESTRUCTIVE_OPERATIONS.md` and `_AGENT_TRAINING/VERIFICATION_DOCTRINE.md`.

## Remaining risks

**Proven safe by executed tests:** descendant Windows junction rejection for scanner, adoption, and direct-directory Smart Inbox; no durable/copy/catalog side effects in those fixtures; ordinary existing happy paths remain green.

**Partially tested:** the shared primitive also rejects a reparse root and file-reparse leaf by implementation, but dedicated root/leaf fixtures were not added in this narrow pass. A directory-junction cycle is rejected on first encounter by the same primitive, but no separate bounded cycle regression was added.

**Reasoned but not experimentally eliminated:** validation followed by path-based file open remains a TOCTOU window if another process swaps a checked component after snapshot validation. Hardlinks are not reparses and remain outside this boundary.

**Still separate audited work:** CAS filesystem identity/reparse/hardlink policy, CAS digest namespace validation, broader multi-file crash matrices, statement-level SQLite failure injection, migration interruption/idempotency, wider mutation concurrency, and scale/performance workloads.

**Hosted verification — NOT EXECUTED:** this workflow runs on pushes to `main` or by `workflow_dispatch`. The available GitHub connector can inspect and rerun existing Actions runs but cannot dispatch a new workflow, `gh` is not installed on `heaven2`, and the tool safety layer blocked secure credential extraction for a direct REST dispatch. The local `Verify-Release.ps1` and `Build-Release.ps1` gates were actually executed and are green, but they do not substitute for hosted-runner evidence. A future agent/operator must dispatch the Windows Release Gate against the exact branch HEAD, record its run ID/artifacts, and only then mark this boundary fully closed.

No destructive test touched the real game installation, real mod library, Desktop, OneDrive, or personal data; all hostile fixtures were isolated temporary directories.

## Adversarial follow-up — 2026-09-28

The first candidate was independently stress-reviewed before integration. Two additional defects were then reproduced on real Windows against pre-follow-up candidate `1c453f5f9c813a85bca53657c0ee30b47d2d15ab`:

1. **Scanner source-root junction bypass.** A mod package whose `SourcePath` itself was a junction was not rejected because root resolution descended to `SourcePath\nativePC` before the shared traversal checked its root. With the new regression present and production unchanged, Integration ran **94 total / exactly 1 failure**: `Scanner_rejects_reparse_source_root_before_capture` reported that no `IOException` was thrown.
2. **Safe-tree behavior regression.** The LIFO traversal reversed sibling processing relative to the prior `SearchOption.AllDirectories` behavior. On an ordinary non-reparse tree the Smart Inbox classification changed from **Texture** to **Mixed**. With the parity regression present and production unchanged, Automation ran **24 total / exactly 1 failure**.

The minimal repair:
- validates `ModScanner`'s package source root before resolving child scan roots;
- keeps fail-closed root/entry reparse rejection;
- pushes discovered child directories in reverse so stack processing preserves the legacy depth-first sibling order instead of reversing it.

Additional dedicated real-Windows regressions now cover:
- scanner source-root junction rejection with no CAS/database publication;
- scanner descendant junction rejection;
- bounded scanner junction cycle failure;
- adoption live-root and descendant junction rejection with no copied package/adoption rows;
- Smart Inbox top-level and descendant junction rejection with no destination/catalog publication;
- bounded Smart Inbox junction cycle failure;
- safe non-reparse Smart Inbox classification parity.

A real **file-symlink/reparse leaf** fixture remains **blocked by environment privilege**: direct `mklink` on `heaven2` returned `You do not have sufficient privilege to perform this operation.` No mock is being substituted for that Windows-specific case.

Exact follow-up source: `742484ba7a6ff07d12c0cfa1ea1a46a1b1205b4a`.

Executed after repair:
- focused Integration **94/94 PASS**;
- focused Automation **24/24 PASS**;
- both changed test projects build with **0 warnings / 0 errors**;
- `Verify-Release.ps1` **25/25 PASS**;
- functions **615/615** after promotion;
- explicit call sites **6517 / 0 uncovered**;
- trace gaps **0**; parse errors **0**;
- Core **79/79**;
- Automation **24/24**;
- Integration/fault injection **94/94**;
- self-test **11/11**;
- strict whole-solution/analyzer verification PASS;
- `Build-Release.ps1` PASS;
- win-x64 ReadyToRun self-contained publish PASS;
- release ZIP SHA-256 `665836D7BED41CD83925FD956E987E9D64D1DE618BF238157E13291F5B7731B6`.

**Updated risk classification:** source-root junctions and bounded directory-junction cycles are now experimentally covered in addition to the original descendant cases. File-reparse leaves remain implementation-covered but not privilege-backed by a dedicated real symlink fixture. The documented path-check/open TOCTOU window and hardlink policy remain separate unresolved boundaries. Hosted Windows Release Gate evidence is still required before declaring this checkpoint fully closed.
