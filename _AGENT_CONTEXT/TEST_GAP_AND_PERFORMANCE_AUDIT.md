# Test-gap, failure-mode, and performance audit — 2026-09-27

## Scope and repository state

This is a documentation-only audit checkpoint. It does not change production source or the active PlannerSnapshotRepository verification boundary.

The audit started from the prompt's older expected `73f1298455ec4c651e211488ececf9803504e60d`, then re-read canonical `main` as it advanced. The final reconciled branch for this audit was created from canonical `main` at:

`0e561f3c059475ad443a79ac4a27dd68264a7bdb`

That state contains the PlannerSnapshotRepository extraction (`8e0068bd44cc6735ffa9478067923ad5d9c54506`), focused parity/cancellation tests (`64e666a19ce17c21bc696b46cce9c07bb257a686`), the failed first hosted attempt `36335255922`, repair source `528401925b1d09b3d65c9652de8e4f2024e3677f`, and repair documentation `0e561f3...`. Per `CURRENT_REVISION.json`, the repaired production slice is still awaiting a fresh exact hosted Windows Release Gate. This audit does not claim or promote verification for it.

The GitHub connector exposes canonical remote repository state but not a local working tree, so no local `git status` result exists for this audit session. All writes are isolated to `agent/test-gap-performance-audit-20260927-v2`. Recent canonical history/diffs, required continuity documents, tests, benchmark source, storage audit, and relevant production code were inspected before writing this document.

## Executive findings

The test suite is strongest where failure consequences are highest in the existing deployment protocol. It has real fault injection around journal durability, filesystem mutation, durable SQLite commit, interrupted rollback, restart recovery, stale-plan preflight, external edits after a crash, and ownership/undo semantics. Those are meaningful assertions, not raw test-count claims.

The highest-value gaps are narrower and more concrete:

1. **Windows native replacement failure semantics are not fault-tested.** `AtomicFileOps.ReplaceFromAsync` calls `ReplaceFileW` and unconditionally cleans the same-directory temp in `finally`. Microsoft documents failure codes where the replace call can fail after names/streams have already changed. A Windows-only regression must exercise those post-failure states; treating every false return as "no mutation happened" is unsafe.
2. **Deployment target containment is lexical, not reparse-safe.** `DeploymentExecutor.Destination` normalizes the relative key but does not verify that existing path components under the game root are not junctions/symlinks/reparse points. Archive extraction has explicit reparse checks; live deployment does not.
3. **CAS integrity is trusted by filename on restore.** `BlobStore.CaptureWithHashAsync` discards a newly captured correct temp if a same-name CAS file already exists, and `RestoreAsync` checks existence but does not re-hash the CAS object before deployment. A corrupted on-disk blob can therefore remain trusted until later live-file checks notice a mismatch.
4. **The crash matrix is mostly single-file.** Current crash/recovery tests prove phase boundaries, but do not prove a multi-file operation that dies after path N of M, then rolls back in reverse order with a mixture of Add/Replace/Remove/RestoreOriginal.
5. **Atomic SQLite workflows outside DeploymentExecutor have little or no statement-level fault injection.** `ReplaceModFilesAsync`, `ChainManualFamilyAsync`, `ProfileRepository.SaveCurrentAsync`, supersession/revalidation batches, adoption recording, trust/issue batches, and migration reset semantics rely on transaction structure but are not broadly tested by injected mid-batch failure.
6. **Migration remains a major recovery gap.** `LegacyV7Migrator` intentionally uses many autocommitted operations and a reset-on-failure strategy. There is no dedicated migration fault matrix for corrupt state, missing/corrupt blobs, cancellation, crash during import, crash during reset, or retry after partial import.
7. **Concurrency, cancellation, and retry/idempotency are under-covered at the deployment service boundary.** The executor narrows TOCTOU windows and rolls back ordinary exceptions after the journal is durable, but there is no concurrent Apply/Recover test, no mid-operation cancellation matrix, and no same-operation retry/idempotency test.
8. **Planner runtime has a realistic benchmark; family inference and I/O-heavy workflows do not.** The existing 500-mod/150k-file planner benchmark is valuable. `GenericFamilyInference`, scanner/CAS recapture, large archive inspection, deployment preflight, startup reconstruction, and Nexus metadata fan-out need separate measurements rather than speculation.

## Existing strong coverage

### Planner / conflicts / family inference

Strong evidence:
- `tests/MhwModManager.Tests/PlannerTests.cs::Same_input_is_deterministic_except_plan_identity`
- `PlannerTests.cs::Disabled_provider_is_not_considered`
- `PlannerInvariantTests.cs::Randomized_texture_graphs_are_deterministic_and_have_one_winner_per_path`
- `PlannerInvariantTests.cs::Large_shared_namespace_with_sparse_incompatibility_still_blocks`
- `ConflictEngineTests.cs::Incompatible_rule_beats_old_exact_winner`
- `AutoCompatibilityTests.cs::Explicit_incompatible_rule_beats_auto_family_inference`
- `AutoCompatibilityTests.cs::Explicit_resource_provider_pin_beats_automatic_texture_revision`
- the AutoCompatibility suite also covers base/option/hotfix chains, independent structural alternatives, family-internal choice, texture revision/provider selection, and fail-closed ambiguous cases.
- `LogicalModFamiliesTests.cs` covers same-source grouping, different-Nexus separation, local topology evidence, ambiguous labels, revisions, and over-grouping guards.
- `RuleGraphTests.cs` covers cycle detection and DAG acceptance.

The newly extracted planner read model also has focused parity coverage:
- `PlannerSnapshotRepositoryTests.cs::Full_snapshot_matches_pre_extraction_query_assembly`
- `Filtered_snapshot_matches_pre_extraction_case_insensitive_distinct_filter`
- `Empty_filter_skips_only_mod_files_and_preserves_other_planner_inputs`
- `Extracted_snapshot_preserves_representative_planner_output`
- `Already_canceled_token_remains_canceled`

### Deployment / journal / rollback / recovery

This is the best-covered safety boundary.

`HardeningTests.cs::Crash_before_commit_recovers_to_exact_before_image` injects:
- `after-journal`
- `before-first-mutation`
- `after-file-write`
- `after-files-written`
- `before-db-commit`

and asserts recovery returns the live file, manifest, and original ownership to the exact before image.

`HardeningTests.cs::Crash_after_durable_commit_stays_committed` injects:
- `after-db-commit`
- `before-cleanup`

and asserts restart recovery preserves the committed after image.

Additional strong guards:
- `DeploymentTests.cs::First_takeover_restore_and_undo_preserve_ownership_semantics`
- `DeploymentTests.cs::Recovery_refuses_to_destroy_external_change`
- `HardeningTests.cs::Whole_plan_preflight_prevents_partial_write_when_later_path_is_stale`
- `HardeningTests.cs::Interrupted_rollback_enters_recovery_required_then_restart_finishes`
- `DeploymentTests.cs::Empty_directory_pruning_never_deletes_nativepc_or_game_root`
- `HardeningTests.cs::Sqlite_integrity_and_wal_survive_parallel_reads`

The source also performs an immediate per-path `VerifyPreconditionAsync` immediately before each mutation, in addition to whole-plan preflight, which materially narrows the stale-plan window.

### Path and archive safety

Strong existing lexical guards:
- `PathRulesTests.cs::Unsafe_windows_paths_are_rejected` covers parent traversal, reserved device name, trailing dot/space, ADS syntax, and UNC.
- `PathRulesTests.cs::Unsafe_archive_paths_are_rejected` covers traversal, drive-qualified/rooted path, device name, and ADS.
- `HardeningTests.cs::Archive_extraction_rejects_parent_traversal` proves no outside file is created.

`ArchiveInspector` also contains production limits of 200,000 entries and 200 GiB expanded bytes plus explicit destination/parent reparse checks. Those implementations are positive safety features even though their edge cases are not yet fully tested.

### Scanner / adoption / game build

- `HardeningTests.cs::Scanner_detects_same_size_same_timestamp_source_edit` proves metadata-only caching is not authoritative.
- `MultiGameTests.cs::Rescan_recaptures_a_missing_cached_blob` proves missing CAS content is recaptured from source.
- `HardeningTests.cs::Adopted_manual_files_are_not_offered_again_until_the_live_bytes_change`
- `HardeningTests.cs::Game_build_change_marks_binary_mods_but_not_texture_only_mods_for_revalidation`

### Explain Why / diagnostics / crash diagnosis

- `AutomationServiceTests.cs::EffectiveInspectorShowsWinnerAndShadowedProvider`
- `AutomationServiceTests.cs::ExplainWhyUsesPlannerDecisionAndHumanRuleEvidence`
- `AutomationLogicTests.cs::CrashBisectorIsolatesSingleCulprit`
- `AutomationLogicTests.cs::CrashBisectorReportsInteractionWhenHalvesAreClean`
- issue-fallback tests cover new texture/plugin suspects, successful-launch clearing, and confirmed bisect persistence.

### Verification cache / source handoff / WPF seam guards

Strong fail-closed tests exist for verification machinery:
- `FunctionVerifierBehaviorTests.cs::Invalid_inventory_preserves_previous_checklist`
- `Missing_trusted_snapshot_cannot_grant_whole_file_trust`
- `Duplicate_trusted_archive_entries_are_rejected`
- `Scan_keeps_exact_trusted_functions_checked_and_edits_unchecked`
- `DebugTraceCoverageTests.cs::FunctionVerificationPipelineIsFailClosed`
- `DebugTraceCoverageTests.cs::FunctionVerifierRejectsGeneratedSourceAndValidatesTrustedSnapshot`
- `DebugTraceCoverageTests.cs::AgentHandoffContinuityIsFailClosedAndPropagating`
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1` independently proves four continuity corruptions are rejected.

WPF extraction seam guards exist in `XamlBindingSafetyTests.cs` for Activity, Coverage, Profiles, Games, inline `Run` one-way bindings, conflict-family controls, and Explain Why bindings. These are useful contract guards but are static source assertions, not runtime WPF behavior tests.

## Failure-mode matrix

Legend: **Y** direct assertion-backed coverage; **P** partial/adjacent coverage; **G** confirmed gap; **N/A** not meaningful for that operation.

| Operation | success | fail before mutation | fail mid-op | files before DB commit | after DB commit | crash/restart | external change | retry/idempotency | concurrent R/W | cancellation | malformed/corrupt | rollback | recovery | large input |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Planner/conflict build | Y | N/A | N/A | N/A | N/A | N/A | P (stale plan enforced later) | Y deterministic | G concurrent snapshot mutation semantics | G aside from repository pre-cancel | P bad rules/cycles | N/A | N/A | P benchmark + 160-provider test |
| PlannerSnapshotRepository | Y | N/A | N/A | N/A | N/A | N/A | N/A | Y repeatable parity | G write-during-read consistency beyond preserved semantics | Y pre-cancel | G corrupt enum/date/row cases | N/A | N/A | G DB-scale read benchmark |
| CAS capture/restore | Y indirectly | P missing source/missing blob | G interrupted/corrupt existing CAS | N/A | N/A | G capture crash leftovers/race | P source-change detection | P same-hash writer race coded, weakly tested | G concurrent capture/restore | G | **G corrupt object under valid filename** | N/A | G | G throughput/large-file benchmark |
| Archive inspect/extract | Y directory/SmartInbox | Y traversal | G cancellation/IO failure cleanup | N/A | N/A | G staging leftovers after crash | G reparse race | P unique destination naming | G | G mid-extract | P traversal only; G malformed/collision/bomb limits | N/A | G cleanup/retry | G near entry/size limits |
| Deployment Apply | Y | Y stale preflight / crash phase | **P single-file fault only** | Y | Y | Y | Y post-crash + preflight stale | G same plan/op retry | **G** | **G mid-phase** | P bad live state | Y | Y | G executor-scale |
| Deployment rollback/recovery | Y | N/A | P interrupted rollback single-file | N/A | N/A | Y | Y fail-closed external edit | P repeated recovery path, no explicit idempotency test | G Apply vs Recover race | G recovery cancellation | G corrupt journal/state JSON | Y | Y | G |
| ReplaceFileW/AtomicFileOps | Y indirectly on Windows gates | N/A | **G documented native partial-failure states** | N/A | N/A | G | G lock/reparse race | G | G | G during copy/replace | G metadata/stream side effects | P outer deployment rollback only | P | G large file/cross-volume source staging |
| SQLite deployment commit | Y | Y phase crash around boundary | G statement-level DB fault | Y | Y | Y | N/A | G | P WAL reads only | G | G DB corruption/constraint/disk full | Y outer protocol | Y | G |
| Other atomic SQLite batches | Y selective | G | **G** | N/A | N/A | G | N/A | G | G | G | G | implicit transaction rollback, rarely asserted | G | G |
| Legacy migration | G dedicated suite absent | G | **G** | N/A | N/A | **G** | N/A | **G retry after partial import** | G | G | **G malformed state/blob cases** | reset strategy only | G reset interruption | G |
| Adoption | Y basic | G | G file-copy vs DB record failure | N/A | N/A | G | Y bytes-changed rediscovery | G | G | G | G | P source folder cleanup by code | G | G |
| Profiles | P list/binding seam | G | G SaveCurrent transaction fault | N/A | N/A | G | N/A | G duplicate-name/re-save semantics | G | G | G | transaction exists, not injected | G | G |
| Nexus/provenance | P local logical family | G | G per-mod/network/DB failure | N/A | N/A | G | N/A | P throttling timestamps | G | P loop checks token | P malformed local/API JSON is partly caught | G batch replacement faults | G | **G fan-out benchmark/fake HTTP tests** |
| Launch health/update impact | P underlying components | N/A | G aggregate dependency failures | N/A | N/A | N/A | N/A | Y deterministic by inputs expected but not dedicated | G | propagated only | G malformed persisted state | N/A | N/A | G |
| Crash bisector/issue diagnosis | Y | N/A | P two structural cases | N/A | N/A | N/A | N/A | G flaky/non-deterministic probe policy | G | G mid-run | N/A | N/A | N/A | G many suspects |
| Verification/source handoff | Y | Y fail-closed | Y negative fixtures | N/A | N/A | N/A | N/A | Y repeat scan semantics | G simultaneous cache writers | G | Y several corrupt/trusted cases | N/A | Y prior checklist preserved | P repository-size cost unmeasured |
| WPF binding seams | P static | N/A | N/A | N/A | N/A | N/A | N/A | N/A | G dispatcher/runtime races | G | P source string guards | N/A | N/A | G large collection churn |

## Confirmed gaps and recommended regression checkpoints

### P0 / highest safety value

#### P0.1 — Windows ReplaceFileW failure postconditions

**Source:** `src/MhwModManager.Filesystem/AtomicFileOps.cs::ReplaceFromAsync`

The implementation:
1. copies the source to a same-directory temp;
2. calls `ReplaceFileW(destination,temp,null,...)` when the destination exists;
3. throws on false;
4. always tries to delete `temp` in `finally`.

Microsoft's `ReplaceFileW` documentation explicitly lists failure codes where the operation can fail after names/streams have already changed, including `ERROR_UNABLE_TO_MOVE_REPLACEMENT` and `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2`. It also documents that replaced/replacement/backup files must reside on the same volume. The implementation intentionally satisfies the latter for the native replace operands by staging `temp` in the destination directory, even if the original CAS/source resides on another volume.

**Missing guard:** a Windows-only test seam capable of forcing/observing each documented failure postcondition and then asserting `DeploymentExecutor` either restores the exact before image or enters `RecoveryRequired` without destroying recoverable bytes.

**Do not** unit-test only that `ReplaceFileW` returned false. Assert actual destination/temp/backup bytes and journal state.

Primary reference:
https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew

#### P0.2 — live deployment must not traverse reparse points

**Sources:**
- `DeploymentExecutor.Destination`
- `DeploymentExecutor.VerifyPreconditionAsync`
- `BlobStore.RestoreAsync`
- contrast with `ArchiveInspector.EnsureNoReparsePoint`

`PathRules.Normalize` is lexical. It prevents `..`, rooted paths, ADS, and device names, but an already-present directory below `gameRoot`/`nativePC` can still be a reparse point. Windows reparse points can redirect ordinary file operations.

**Missing guard:** Windows integration tests create a junction/symlink inside the managed tree pointing outside the game root, then assert Add/Replace/Remove/rollback all fail closed without touching the external target. Include a race-oriented test if a controllable filesystem abstraction is introduced later.

Primary references:
https://learn.microsoft.com/windows/win32/fileio/reparse-points
https://learn.microsoft.com/windows/win32/fileio/symbolic-link-effects-on-file-systems-functions

#### P0.3 — corrupted CAS object must never be deployed as a trusted hash

**Source:** `src/MhwModManager.Filesystem/BlobStore.cs`

`CaptureWithHashAsync` currently treats an existing hash-named CAS file as authoritative and discards the newly captured temp. `RestoreAsync` checks only that the object exists before restoring it.

**Recommended test:** capture known bytes, corrupt the CAS file without renaming it, then:
- recapture the original source;
- restore/deploy the hash;
- assert the system detects and repairs or rejects the corrupt CAS object before live mutation.

This test should establish the desired fail-closed contract before production behavior is changed.

### P1 / high correctness value

#### P1.1 — multi-file deployment crash matrix

Extend the existing phase matrix to a mixed 4–8 path plan:
- unmanaged takeover/Add;
- managed Replace;
- Remove;
- RestoreOriginal.

Crash after sequence 1, middle sequence, final sequence, after `FilesWritten`, and during rollback at multiple sequences. Assert every live byte, manifest row, original ownership row, mod state, operation state, and journal status after restart.

The current single-file phase tests are strong but do not prove partial-prefix rollback ordering.

#### P1.2 — database statement-level fault injection

Add a narrow SQLite command/transaction fault seam in tests rather than changing transaction ownership.

Priority operations:
1. DeploymentExecutor prepared journal transaction.
2. DeploymentExecutor post-files commit transaction.
3. DeploymentExecutor rollback DB transaction.
4. `ManagerDatabase.ReplaceModFilesAsync`.
5. `ManagerDatabase.ChainManualFamilyAsync`.
6. `ProfileRepository.SaveCurrentAsync`.
7. `ReplaceSupersessionAsync`, `MarkRevalidationAsync`, `SetEnabledAndPriorityAsync`.
8. adoption/trust/issue batch transactions.

For each, fail after at least one mutation inside the transaction and assert no partial DB state is visible.

#### P1.3 — deployment cancellation / concurrency / retry

Add independent checkpoints for:
- cancellation before journal: no operation/journal/live mutation;
- cancellation after journal but before first file: rollback reaches exact before image;
- cancellation after first of multiple files: rollback exact before image;
- cancellation requested during rollback: rollback intentionally uses `CancellationToken.None` from Apply failure handling and must still complete;
- concurrent `ApplyAsync` calls against overlapping paths;
- `ApplyAsync` racing `RecoverIncompleteAsync`;
- retry after a failed/rolled-back operation, including same vs new operation ID contract.

Do not assume UI busy-state is a substitute for service-level serialization unless that invariant is explicitly enforced and tested.

#### P1.4 — migration fault/retry matrix

**Source:** `LegacyV7Migrator.cs`

Add a dedicated `LegacyV7MigratorTests` suite covering:
- unsupported/malformed JSON schema;
- missing referenced legacy blob;
- corrupt blob whose bytes do not match its SHA-256 name;
- invalid legacy path;
- failure after some mods/files/rules/profiles are imported;
- cancellation at several loop points;
- restart after partial import before completion marker;
- failure/crash during `ResetIncompleteImportAsync`;
- second run after failed first run;
- preservation of legacy source state and backup/report evidence.

Because migration is reset-and-retry rather than one giant transaction, test the workflow contract directly rather than pretending it is a single atomic transaction.

#### P1.5 — archive/reparse/collision limits

Add tests for:
- destination root already a reparse point;
- parent component changed into a reparse point during extraction;
- case-colliding entries (`A/file` vs `a/file`) on Windows;
- Unicode-equivalent/confusable filenames where the filesystem normalizes/aliases;
- duplicate archive entries targeting one destination;
- reserved device names with extensions and mixed casing;
- long path behavior under the actual Windows manifest/policy;
- malformed/truncated archives;
- entry count at limit and limit+1;
- expanded bytes at limit and limit+1 using sparse/synthetic test support;
- cleanup/retry after cancellation or extraction failure.

A compression-ratio limit is not currently present; the implementation bounds expanded bytes and count. Decide explicitly whether those two bounds are the intended bomb policy before adding a ratio policy.

#### P1.6 — CAS concurrency and crash leftovers

Test:
- two concurrent captures of identical bytes;
- concurrent capture of different sources while CAS destination appears;
- cancellation during copy;
- source mutation during copy, including same-size/same-timestamp adversarial mutation if reproducible;
- stale `.capture-*.tmp` after process death and startup cleanup policy;
- missing/corrupt CAS during rollback/recovery.

### P2 / important semantic coverage

#### Planner/conflict cases

Add focused regressions for:
- exact tie: same priority and otherwise equal candidates must resolve deterministically or fail closed by explicit contract;
- stale exact-winner rule referencing a disabled/missing provider;
- stale overlay rule referencing removed mods;
- empty planner snapshot and empty enabled set;
- changed game profile/build inputs where rich MHW inference must be disabled or revalidation must surface;
- 3+ provider graphs where only a subset is explicitly ordered;
- explicit rule duplicates with identical timestamps (current index uses newer/equal replacement; input-order behavior should be pinned);
- semantic overlapping assets where same family evidence changes between rescans;
- very large sparse provider graph and very dense provider graph assertions independent of BenchmarkDotNet.

#### Explain Why

Existing explicit-human-rule coverage is good. Add one case each for:
- inferred family winner;
- fail-closed blocking ambiguity;
- resource-provider pin;
- stale applied manifest that no longer matches planned winner;
- missing provider metadata / deleted mod.

#### Profiles / game switching / service reconstruction

`XamlBindingSafetyTests` proves static binding surfaces, not runtime state reconstruction.

Add:
- `ProfileRepository.SaveCurrentAsync` success + overwrite existing profile + injected rollback;
- profile load into deployment target state and undo behavior;
- switch active game and reconstruct services with different roots, then prove no state/data from the old game leaks into planner/deployment;
- interrupted or failed game switch leaves one coherent active registry/service state.

#### Game update impact / launch health

There are underlying component tests but no dedicated aggregate tests observed for:
- `GameUpdateImpactService.BuildAsync` classification of texture-only vs structural vs plugin/executable/game-data;
- `LaunchHealthGateService.EvaluateAsync` precedence/aggregation of health blocker, unmanaged warning, missing dependency blocker, planner conflict, and needs-revalidation warning;
- cancellation/failure in one dependency.

#### Nexus/provenance

Current local metadata family inference has a useful regression (`ManagerLogicalFileNameGroupsPackagesWithoutBrandSpecificRules`), but live-sync behavior is largely untested.

Add fake-`HttpMessageHandler` based tests before more Nexus features:
- no API key / throttled sync;
- rate/HTTP failure and malformed JSON;
- ambiguous file-version matches fail closed;
- previous-version resolution;
- update timestamp comparison;
- preview size/content-type limits and temp cleanup;
- supersession replacement remains atomic if one insert fails.

### P3 / robustness and UI runtime

- Run WPF binding smoke tests on an STA dispatcher for key pages rather than relying only on source-string assertions.
- Stress `ObservableCollection`/view refreshes with thousands of mods/conflicts/activity rows and verify refresh is batched enough to avoid UI stalls.
- Add cancellation to crash bisector and a noisy/flaky reproducer policy test before treating it as deterministic diagnosis.
- Add concurrent verification-cache writer protection tests if CI/local verifier processes can overlap.

## Performance audit

### Existing planner benchmark is meaningful

`benchmarks/MhwModManager.Benchmarks/Program.cs` currently benchmarks:
- 200 mods / 50,000 files
- 500 mods / 150,000 files

Each scenario deliberately makes 100 paths shared by every mod.

That directly stresses `DeploymentPlanner`'s pair-overlap loop:

`sum over shared paths of C(providerCount, 2)`

Concrete work just for the 100 fully shared paths:
- 200 mods: 100 × C(200,2) = **1,990,000** pair increments.
- 500 mods: 100 × C(500,2) = **12,475,000** pair increments.

This is a real density-dependent O(sum k_path²) cost, not merely "there is a nested loop." The benchmark is therefore valuable. What is missing is stored baseline data/threshold policy and companion workloads for sparse real-world overlap.

### Generic family inference is the clearest unbenchmarked algorithmic risk

`GenericFamilyInference.InferKeys` compares every unassigned/non-Nexus candidate pair. `ScorePair` computes path overlap by scanning one path set against another. For 500 eligible mods, there are **124,750 mod pairs** before cluster-merge work. With ~300 paths/mod, the initial overlap checks alone can reach roughly **37 million hash-set membership probes**, plus name/asset/root work.

The accepted-pair union phase then repeatedly enumerates candidate cluster members and performs complete-link `HardBlock` checks across clusters. Dense evidence graphs can therefore cost materially more than the initial pair scan.

**Benchmark checkpoint:** 100/250/500/1000 local mods at 50/300/1000 files per mod, with separate sparse, family-clustered, and adversarial near-match datasets. Measure wall time and allocations for both `GenericFamilyInference.InferKeys` and `LogicalModFamilies.Build`.

### Scanner/CAS always pays at least a fast content pass for cached files

`ModScanner.CaptureAsync` intentionally does not trust size/timestamp alone. Cached files with a fast hash still get an XXH3 content pass; changed/un-cached files pay SHA-256 + CAS copy. Parallelism is bounded 2–6.

That is correctness-positive, but startup/full-library rescans can be I/O bound.

**Benchmark checkpoint:** warm unchanged cache vs 1%/10%/100% changed files, HDD-like throttled I/O vs SSD where practical, 10k/50k/150k files. Report bytes read, hashes computed, CAS writes, elapsed time, and peak allocations.

### Deployment preflight hashes managed targets twice in the no-change case

`ApplyAsync` does:
1. whole-plan `VerifyAllPreconditionsAsync` with max parallelism 4;
2. another `VerifyPreconditionAsync` immediately before each mutation.

This is deliberate TOCTOU hardening, not accidental duplication. At very large plan sizes it doubles authoritative reads for expected-live files.

**Benchmark checkpoint:** 1k/10k/50k deployment changes with realistic file-size distributions; measure preflight vs mutation time separately. Do not remove immediate revalidation merely to win a benchmark.

### Nexus metadata refresh has bounded public scraping but unbounded API-per-mod fan-out

`NexusMetadataService.RefreshAsync`:
- scans every mod locally;
- public no-key visuals are budgeted to 8 background / 60 forced;
- when live API sync is allowed, it iterates every Nexus-linked mod serially;
- a mod can require mod UUID lookup, file list, up to four version-list requests, previous-version lookup, visuals, and update check.

This is latency/fan-out risk proportional to Nexus-linked mod count and candidate ambiguity. It is also hard to regression-test because the current service owns a static `HttpClient`.

**Checkpoint:** inject HTTP transport/time and test 10/100/500 Nexus-linked mods using deterministic fake latency. Record request count per mod, total wall time, cancellation latency, and DB round trips. Optimize only after measuring; likely wins include request memoization/batching/concurrency with strict rate limits, not uncontrolled parallelism.

### Database round trips / startup reconstruction

The PlannerSnapshotRepository intentionally preserved the historical two-connection read semantics for behavior parity. Do not "optimize" that while its current verification boundary is open.

After closure, add a read benchmark for 200/500/1000 mods and 50k/150k/500k `mod_files`, including full and filtered snapshots. Track rows, commands, elapsed time, and allocations. A later snapshot/isolation redesign must be a separate correctness checkpoint, not bundled into performance work.

## Recommended independent checkpoint order

1. **Close the current PlannerSnapshotRepository Windows gate first.** Do not mix this audit with that source boundary.
2. **Windows filesystem safety checkpoint:** ReplaceFileW documented failure postconditions + deployment reparse containment tests. If tests expose a production defect, fix only that boundary and run the full Windows gate.
3. **CAS integrity checkpoint:** corrupt-existing-object and recovery tests; define fail-closed behavior before code changes.
4. **Multi-file deployment fault checkpoint:** broaden phase/rollback/cancellation matrix without production refactor.
5. **SQLite transaction fault-injection checkpoint:** add a reusable test seam/harness, then cover one atomic domain boundary at a time.
6. **Migration recovery checkpoint:** dedicated corrupt/failure/retry suite.
7. **Archive hardening checkpoint:** reparse/collision/malformed/limit cases.
8. **Planner semantic edge checkpoint:** stale rules, equal ties, empty/large graphs, Explain Why variants.
9. **Performance checkpoint:** family inference + scanner/CAS + executor preflight benchmarks. Record baselines before optimizing.
10. **Nexus/launch/update checkpoint:** injectable fake HTTP plus aggregate launch/update tests.
11. **WPF runtime stress checkpoint:** STA smoke/binding and large collection refresh behavior.

Each production fix remains its own source -> focused regression -> continuity -> hosted Windows evidence -> closure cycle. Do not bundle unrelated safety fixes because this audit found them together.

## Parallel integration note

After this audit branch was based, canonical `main` gained a separate deep SQLite atomicity audit:
- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`
- follow-up linkage in `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`

Those parallel findings are complementary and more specialized for SQLite statement/transaction details. When integrating this branch, preserve the deep SQLite audit and use it as the more specific authority where its transaction findings overlap the high-level database recommendations here. Do not overwrite or flatten either audit.

## Database transaction audit cross-check

The existing `STORAGE_TRANSACTION_BOUNDARY_AUDIT.md` correctly identifies transaction owners that must not be fragmented:
- `ReplaceModFilesAsync`
- `ChainManualFamilyAsync`
- `ProfileRepository.SaveCurrentAsync`
- DeploymentExecutor prepared/final/rollback transactions
- adoption record
- trust and issue batches
- supersession replacement
- revalidation and enabled/priority batches

This audit adds a test requirement, not a transaction redesign: each boundary needs at least one injected mid-transaction failure proving rollback leaves no partial state.

The migration exception remains deliberate: `LegacyV7Migrator` is a reset-and-retry cross-filesystem workflow, not one SQLite transaction. Test that contract instead of forcing it into an artificial UnitOfWork.

## Verification honesty

Executed in this audit session:
- inspected canonical GitHub `main` history/diffs and observed it advance during the audit;
- reconciled the audit against final inspected main `0e561f3c059475ad443a79ac4a27dd68264a7bdb`, including the PlannerSnapshotRepository first-gate failure and repair;
- created independent branch `agent/test-gap-performance-audit-20260927-v2`;
- read continuity/storage/verification documents;
- inspected relevant Core, Filesystem, Storage, Automation, App seam-test, and benchmark source;
- inspected assertion bodies for the tests cited above;
- checked Microsoft primary documentation for `ReplaceFileW` and reparse-point semantics.

Not executed:
- no local Windows runtime tests;
- no `dotnet test`;
- no BenchmarkDotNet run;
- no hosted Windows Release Gate;
- no verification-cache promotion;
- no production source modification.

Therefore all Windows behavior findings that depend on native runtime reproduction are recommendations/risks grounded in source + Microsoft documentation, not claims that a failing runtime test was observed.

## Handoff

The next agent must:
1. re-read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and this audit;
2. treat canonical `main` and newer continuity state as authoritative if they advanced;
3. close the current PlannerSnapshotRepository verification boundary before starting one of this audit's source checkpoints;
4. preserve exact transaction ownership and fail-closed recovery semantics;
5. keep each recommended test/fix as an independent checkpoint with exact verification evidence;
6. update durable continuity state and explicitly require their successor to preserve and recursively propagate the permanent continuity system to the agent after them.

Do not break the chain.
