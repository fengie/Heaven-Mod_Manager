# Product tests and performance measurement matrix

## What runs automatically

The existing `scripts/release/Verify-Release.ps1 -FeatureCandidate` and Workflow Feature PR Gate run the core xUnit v3, automation, and Windows integration/fault-injection suites; they also strictly compile the BenchmarkDotNet project. The main Windows Release Gate runs the canonical release checks. Benchmark timing is deliberately **not** a CI pass/fail threshold because shared-runner timing varies.

The new `SubsystemWorkloadRegressionTests` assert real outcomes, not timing: nested archive boundary count at 1,024 paths; archive inspection, safe extraction and authoritative hash equality; 48-archive multi-game import, original preservation and replay deduplication; and pre-cancelled imports that cannot publish files.

## Coverage matrix

| Product subsystem | Functional/unit or integration evidence | Benchmark or measurement |
| --- | --- | --- |
| Conflict planning, path and compatibility rules | `PlannerTests`, `PlannerInvariantTests`, `ConflictEngineTests`, `PathRulesTests` | `PlannerBenchmarks` (200/500 mods, 50k/150k files) |
| Family inference and package grouping | `LogicalModFamiliesTests`, `ModBoundaryDiscoveryTests`, `SubsystemWorkloadRegressionTests` | `FamilyInferenceBenchmarks`, `ModBoundaryDiscoveryBenchmarks` (100/1k/10k paths) |
| Game identity and bulk routing | `GameModIntakeTests`, `CollectionBudgetIntakeTests`, `SubsystemWorkloadRegressionTests` | `GameClassificationBenchmarks`, `BulkArchiveIntakeBenchmarks` (8/64 ZIPs at 4 KiB or 1 MiB per payload) |
| ZIP/RAR/7z decoding, extraction and safety | `RealArchiveFormatAcceptanceTests`, `LargeZipManifestIntakeTests`, `HardeningTests` | `ArchivePipelineBenchmarks` (32/512-entry synthetic ZIP inspect/extract) |
| Hashing, blob capture and filesystem writes | `HashingServiceSafetyTests`, `BlobIntegrityTests`, `DeploymentTests` | `FileHashingBenchmarks` (1/8 MiB fast vs authoritative) |
| SQLite, migrations, profiles and persistence | `ManagerDatabaseMigrationTests`, `PlannerSnapshotRepositoryTests`, `ProfileRepositoryAtomicityTests` | `ModDatabaseBenchmarks` (100/1000 warm mod-row reads) |
| Startup and first usable window | `StartupPathResolutionTests`, `StartupPerformanceTests` | `Measure-StartupPerformance.ps1`, `startup-performance-gate.yml` |
| UI bindings, game switch, browse and catalog | `XamlBindingSafetyTests`, `SingleInstanceAndGameSwitchTests`, `CatalogScaleRegressionTests` | No full installed-WPF p95 interaction benchmark yet |
| Auto Populate, Apply, Undo and recovery | `AutoPopulateServiceTests`, `DeploymentTests`, `HardeningTests` | Planner benchmark only; installed Apply/Undo I/O budgets still needed |
| Updater, releases, retention and rollback | `UpdaterCoreTests`, `UpdaterStorageMaintenanceTests`, `UpdaterInstalledClientE2ETests` | Full isolated installed update/rollback acceptance remains open under #793 |

Functional tests are deterministic and may use tiny synthetic packages. These are not claims of representative large RAR performance or physically installed GUI responsiveness.

## Run on a Windows development environment

```powershell
# Existing complete release-quality checks (includes unit + integration suites)
.\scripts\release\Verify-Release.ps1 -FeatureCandidate

# Quick BenchmarkDotNet harness validation, no reliable timing claims
.\scripts\benchmarks\Run-Benchmarks.ps1 -Filter '*BulkArchiveIntakeBenchmarks*' -Job Dry

# Repeatable measurements; pass a filter to avoid unnecessarily large suites
.\scripts\benchmarks\Run-Benchmarks.ps1 -Filter '*ArchivePipelineBenchmarks*' -Job Short
.\scripts\benchmarks\Run-Benchmarks.ps1 -Filter '*FileHashingBenchmarks*' -Job Short
.\scripts\benchmarks\Run-Benchmarks.ps1 -Filter '*' -Job Short
```

Use **Actions → Subsystem Performance Benchmarks → Run workflow** for an explicitly requested Windows-runner measurement. This workflow is manual-only, read-only with respect to GitHub, and uses isolated synthetic files under the process temporary directory. It does not inspect or change user Mods, game installations or updater storage. Measurements are emitted to BenchmarkDotNet JSON and optionally uploaded for three days; artifact quotas can prevent retention without changing the benchmark result.

## Baseline and regression methodology

Record exact source SHA, runner name, CPU/RAM/disk, SDK, scenario parameters, date, sample count, distribution, and output correctness. Compare baseline and candidate on **the same physical runner** with the same scenario and warm/cold conditions. Compare mean/median and tails using the JSON distribution; do not claim a throughput improvement from a single run. Separate pure CPU algorithms, filesystem throughput, and UI feedback latency.

Never optimize by skipping checksum/integrity verification, path/reparse containment, safe extraction limits, durable transactions, cancellation or fallback review. Correctness regressions fail the existing xUnit/CI checks independently of performance measurements. Keep large real-world RAR/7z, mixed 100s-mod user corpus, installed drag-and-drop responsiveness, remote metadata/network latency, catalog pagination and positive isolated updater E2E as independently unverified work under #791, #735 and #793 until instrumented on an installed client.


## November 16 fault-boundary and topology expansion

The existing 26-case synthetic profile and previously landed four cross-subsystem tests do not cover all operational correctness properties. The following additions deliberately focus on gaps not already specified by the large hardening/deployment suites:

| Blind spot | New deterministic oracle | Performance visibility |
| --- | --- | --- |
| Post-progress cancel versus pre-cancel only | Cancel synchronously after the first completed archive; assert later archives untouched and no misleading completed batch report | End-user cancel latency still requires installed UI instrumentation |
| Input fan-out and malformed persisted state | Reject 2,049 explicit selections before staging; malformed SHA-256 receipt index fails closed | Inventory/replay benchmark contrasts 8/64/256 archive warm runs |
| Bad ZIP/RAR/7z next to valid packages | Keep valid archive routing despite corrupt RAR and 7z, suspicious ZIP entry and unchanged original source files | No claim of actual real RAR or solid 7z throughput |
| Archive extraction collision | Reject case-insensitive duplicate output and file/ancestor conflicts before writing | Existing 32/512-entry extract-and-cleanup benchmark still relevant |
| Interrupted extraction | Fault-inject cooperative cancellation before payload write; no abandoned owned partial file | No synthetic test substitutes for power-loss durability proof |
| Same archive from two names | Deduplicate identical byte content by registered game and SHA, preserve both sources | Warm replay benchmark includes repeated hashing/copy/receipt lookups |
| Mod family input order | Seeded input permutations must preserve grouping keys and complete-link negative evidence | Topology benchmark compares independent roots with shared root/index pressure |
| False grouping due to shared folder prefix | Unrelated filenames in a shared root do not become one mod | Shared-root candidate pressure must not be equated with justified auto-grouping |

The new replay benchmark seeds isolated manager state once in `GlobalSetup`, then times actual second-import behavior against that state; **fresh import and replay must not be mixed in one average**. It checks `AlreadyPresent == ArchiveCount` on each invocation to catch accidental fast no-op paths. The new family topology benchmark runs the same mod/file count with separate versus shared four-segment roots and fails if unrelated mods are grouped. BenchmarkDotNet `Allocated` measures cumulative managed allocations, not peak RSS or total bytes written. See <https://github.com/dotnet/BenchmarkDotNet/blob/master/docs/articles/configs/diagnosers.md> and Microsoft .NET unit-test guidance at <https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices>.

### Missing proof, not passed checks

- Real large RAR4/solid RAR5/7z imports, encrypted/corrupt variants and mounted remote/external disks: existing real-format acceptance uses small pinned fixtures. **Representative real throughput remains unmeasured**.
- Warm duplicate replay currently copies and hashes source archives; receipt persistence rewrites sorted JSON on every new acceptance. This is a candidate large-batch complexity problem; establish a measured scaling curve before altering durable receipt writes.
- Installed WPF dragging and live per-phase feedback, preview/UI thread blocking, user-perceived import p50/p95, Auto Populate end-to-end state changes, Nexus metadata refresh and device-specific disk throughput are not covered by these isolated microbenchmarks.
- Cross-process/concurrent import writers and crash consistency of receipt/index state require an independent serial-ownership and recovery fixture. Do not simulate concurrent writers against an installed user library or weaken existing single-writer policy.
- A green CI unit/benchmark compile does not prove the opt-in BenchmarkDotNet suite executed. Run a filtered, explicitly requested manual measurement and compare the same machine/workload/candidate; do not set flaky absolute wall-time thresholds in the normal PR gate.
- All temporary fixture roots are created under unique test-owned temporary directories and cleaned up; never point these tests at a user's mod library, game install, updater state, or real downloads.

Test commands on a Windows development host:

```powershell
dotnet test .\tests\MhwModManager.Tests\MhwModManager.Tests.csproj -c Release --filter 'FullyQualifiedName~FamilyInferenceOrderInvariantsTests'
dotnet test .\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~BulkImportFaultBoundaryTests'
.\scripts\release\Verify-Release.ps1 -FeatureCandidate
# Opt-in only; never infer these have run from a compiled PR:
.\scripts\benchmarks\Run-Benchmarks.ps1 -Filter '*BulkArchiveReplayBenchmarks*' -Job Short
.\scripts\benchmarks\Run-Benchmarks.ps1 -Filter '*FamilyTopologyBenchmarks*' -Job Short
```


## Concurrent receipt publication and cancellable byte-level progress

Two independent manager instances can both read an older receipt JSON snapshot, then
publish unrelated packages. Without a shared final-publication boundary, the later
sorted-index rewrite can erase another process's receipt even though both packages
reached their game-scoped Inboxes. The deterministic regression
`Two_independent_importers_preserve_both_receipts_after_stale_read` pauses one
importer **after** its receipt snapshot and runs a second importer to completion
before resuming. `Competing_importers_of_identical_archive_bytes_publish_only_one_package`
exercises the same race for identical bytes under different source filenames.

The intake service now holds a Windows `FileShare.None` lease on a dedicated
`mod-intake-receipts.lock` file only while rereading the durable receipts,
checking destination integrity, publishing an already-copied package and writing
the merged receipt JSON. The lease file is **retained** across process lifetimes:
deleting and recreating it would permit multiple writers to lock different file
objects. Receipt reads share delete access with the existing same-folder
atomic-replace operation. Contention waits are bounded, cancelable and fail
closed. A receipt can mean a mod was previously **processed**, so an empty Inbox
alone never authorizes repeat publication. The original files remain immutable.

Selected archive files and independent top-level downloaded archives now report
actual copied MiB (throttled to approximately 4 MiB after the initial chunk)
through the existing `GameModIntakeProgress` callback. Cancelling from that
callback prevents publication and cleans the owned scratch file; a deterministic
test pins the behavior. Nested archive extraction and unpacked-folder copies
still require distinct phase-progress evidence, rather than claiming percent
completion for all possible input layouts.

These behavior changes remain subject to exact-head Windows integration tests
and a same-runner before/after warm-replay benchmark; a build or synthetic test
does **not** establish installed WPF responsiveness, hard-power-loss fsync
durability, large real RAR/7z speed, or safe cross-host shared-network-filesystem
locking. Do not relax the existing 8 GiB bound, verification hash, ownership,
or atomic index-write guarantees for speed.

Primary OS semantics: Microsoft's `FileShare.None` declines sharing of an
opened file until it is closed (<https://learn.microsoft.com/dotnet/api/system.io.fileshare>);
`Task.Delay` observes cancellation
(<https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.delay>).


## Explicit activation and library review (November 25 candidate)

- `ExplicitModSelectionTests` verifies marks and mixed staged changes remain independent of deployment, and `LibraryPreferenceTests` covers saved views, category/favorite persistence and 100/500/1,000-row bulk marking without deployment.
- `DeploymentReviewTests` checks exact plan projection, resolved overlaps and global blockers. `DeploymentReviewBenchmarks` characterizes 10k/50k/100k planned replacements across 100/500/1,000 mods; `TextureRelationshipBenchmarks` checks 100/500-mod texture relationships.
- Run the existing manual benchmark workflow with `*DeploymentReviewBenchmarks*` and `*TextureRelationshipBenchmarks*`, separately, and retain measured results. A compiled benchmark or deterministic scale test is not a measured responsiveness result.
- Installed WPF, actual game launch, and full feature/method verification remain release gates; no physical machine results are implied by this candidate.
