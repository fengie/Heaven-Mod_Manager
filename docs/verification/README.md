# Feature-to-verification inventory (first pass, 2026-11-17)

The machine-readable [feature-test-matrix.json](feature-test-matrix.json) maps 19 high-impact product capabilities to actual production source files, existing unit/integration tests, optional benchmark sources, and **named missing installed-client acceptance evidence**.

The mapping is an inventory and test-coverage *intent*, not proof that each mapped test ran, passed or covers every branch. The `FeatureVerificationMatrixTests` suite fails if any mapped source/test path vanishes or a critical feature loses all mapped tests. Installed acceptance remains **unverified** for every row until tested on the exact packaged executable with safe fixtures and verified evidence. Do not mark all features "green" merely because source CI is green.

## Real code coverage

Microsoft.Testing.Extensions.CodeCoverage v18.12.0 is used through the existing .NET 10, xUnit v3 (MTP v2) test runners. No new runner, daemon, schedule or external telemetry service is required.

Use the opt-in **Actions → Verification Code Coverage (on demand)** workflow after it is merged to the default branch. It runs the exact requested SHA on the existing isolated Windows CI runner; produces per-suite Cobertura XML and a JSON summary. Artifacts are optional if GitHub's artifact quota is exhausted; the job summary still contains measured counts.

Alternatively on a Windows developer or assigned test runner:

```powershell
.\scripts\ci\Assert-PinnedDotNetSdk.ps1
.\scripts\testing\Measure-ProjectCoverage.ps1 -Suite All
```

The harness fails if a test project fails, the collector emits no Cobertura XML, or counters are missing/invalid. Reports remain under test-owned `BuildLogs/Coverage/run-*/`; do not measure on user's game installations.

**Do not sum the three per-suite percentages:** they can instrument the same production methods. Full cross-suite union/merged branch coverage and changed-code coverage thresholds are **still pending**. Establish a measured baseline and instrumented cost first; then add a compatible merging/reporting tool with tested semantics.

## Closure criteria still outstanding

- Exact-head Windows release and feature CI for the coverage-package change, plus at least one observed on-demand measured coverage run.
- Installed WPF UI Automation for the actual packaged app: import/drag/drop; current game selection and switching; mod list after import; enabling/disabling; conflict explanation; apply/undo; cancellation, progress and keyboard focus.
- Representative fault and data corpora: real large RAR4/RAR5/solid/7z, nested mod trees, corrupt/unsupported/encrypted archives, disk full, junction/symlink/NTFS paths, interrupted update and rollback.
- Isolated user-safe production-like workloads at 0/100/1k/10k mods, 1/10/100/500 archives and internal/external storage; p50/p95/p99, allocations, peak RAM, UI input latency and correctness checks on identical hardware.
- Existing opt-in [BenchmarkDotNet workflow](https://github.com/fengie/Heaven-Mod_Manager/actions/workflows/subsystem-performance-benchmarks.yml) is the source of synthetic algorithm baselines. Do not confuse benchmark compilation with executed timings.
- Independent immutable release/source, package hash, installed version, updater replay and rollback receipts. Do not bypass safety admission or mutate live game roots for testing.

Read this file and the repository's existing [TEST-AND-PERFORMANCE-MATRIX](../TEST-AND-PERFORMANCE-MATRIX.md) when expanding scope. Keep tests and benchmarks owned by this project, with shared CI/agent infrastructure remaining in Heaven Toolbox.
