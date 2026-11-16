# Product tests and performance measurement matrix

## What runs automatically

The existing `scripts/release/Verify-Release.ps1 -FeatureCandidate` and Workflow Feature PR Gate run the core xUnit v3, automation, and Windows integration/fault-injection suites; they also strictly compile the BenchmarkDotNet project. The main Windows Release Gate runs the canonical release checks. Benchmark timing is deliberately **not** a CI pass/fail threshold because shared-runner timing varies.

The new `SubsystemWorkloadRegressionTests` assert real outcomes, not timing: nested archive boundary count at 1,024 paths; archive inspection, safe extraction and authoritative hash equality; 48-archive multi-game import, original preservation and replay deduplication; and pre-cancelled imports that cannot publish files.

## Coverage matrix

| Product subsystem | Functional/unit or integration evidence | Benchmark or measurement |
| --- | --- | --- |
| Conflict planning, path and compatibility rules | `PlannerTests`, `PlannerInvariantTests`, `ConflictEngineTests`, `PathRulesTests` | `PlannerBenchmarks` (200/500 mods, 50k/150k files) |
| Family inference and package grouping | `LogicalModFamiliesTests`, `ModBoundaryDiscoveryTests`, `SubsystemWorkloadRegressionTests` | `FamilyInferenceBenchmarks`, `ModBoundaryDiscoveryBenchmarks` (100/1k/10k paths) |
| Game identity and bulk routing | `GameModIntakeTests`, `CollectionBudgetIntakeTests`, `SubsystemWorkloadRegressionTests` | `GameClassificationBenchmarks`, `BulkArchiveIntakeBenchmarks` (8/64 ZIPs) |
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
