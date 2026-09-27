# Source map

## `src/MhwModManager.Core`
Domain/policy and game-agnostic planning logic. Important files: `Domain.cs`, `GameProfiles.cs`, `ConflictEngine.cs`, `DeploymentPlanner.cs`, `RuleGraph.cs`, `PathRules.cs`, family/provenance intelligence, texture safety, and `MasterDebugLog.cs`.

## `src/MhwModManager.Storage`
SQLite persistence and migration. Important files: `ManagerDatabase.cs`, `Schema.cs`, `ProfileRepository.cs`, `LegacyV7Migrator.cs`.

## `src/MhwModManager.Filesystem`
Physical IO and integrations: `DeploymentExecutor.cs`, `AtomicFileOps.cs`, `BlobStore.cs`, `HashingService.cs`, `ModScanner.cs`, `GameProfileRegistry.cs`, `NexusMetadataService.cs`, visuals/previews, adoption, restart-manager inspection, game-build monitoring.

## `src/MhwModManager.Mhw`
MHW-specific locator/process/catalog behavior: `GameLocator.cs`, `GameProcessGuard.cs`, `ArmorCatalogLoader.cs`.

## `src/MhwModManager.Diagnostics`
Structured/master logging, health, startup diagnostics, support bundles and telemetry. `AppLogging.cs`, `UnifiedDebugLog.cs`, `HealthService.cs`, `SupportBundleService.cs`.

## `src/MhwModManager.Automation`
Higher-level workflows: coordinator, crash bisector, last-known-good, update impact/diff, save backup, dependency doctor, duplicate cleanup, trust, issue fallback, category/preset/inbox helpers.

## `src/MhwModManager.App`
WPF composition and presentation. `Assets/MHWModManager.ico` is the canonical application/executable/window icon; `MhwModManager.App.csproj` embeds it and the global `Window` style in `App.xaml` applies it to WPF windows. `App.xaml.cs` is the composition/bootstrap root. `ViewModels/MainWindowViewModel.cs` is still a large concentration of UI orchestration and should be treated as architectural debt rather than a template to expand indefinitely. `Rows.cs`, `MainWindow.xaml`, profile editor/startup windows and WPF tracing live here.

## `tools/MhwModManager.FunctionVerifier`
v8.8 source verifier. Uses the Roslyn assemblies shipped with the pinned SDK instead of adding a NuGet package. `Program.cs` owns scanning, fingerprints, trusted-v8.7 comparison, reports, and atomic cache promotion.

## Tests

- `tests/MhwModManager.Tests`: Core policy/planner/conflict/profile invariants.
- `tests/MhwModManager.AutomationTests`: automation logic/services.
- `tests/MhwModManager.IntegrationTests`: deployment, hardening, multi-game, UI/static binding, visuals and v8.8 trace/cache invariants.
- `tools/MhwModManager.SelfTest`: end-to-end automation self-test.
- `benchmarks/MhwModManager.Benchmarks`: performance coverage.

## Verification/build entrypoints

- `Test Everything.bat` / `scripts/Verify-Release.ps1`
- `Build.bat` / `scripts/Build-Release.ps1`
- `scripts/Test-CSharpTracePlacement.ps1`
- `.verification/*`

## Continuity tooling

- `AGENTS.md`: root instructions for repository-aware coding agents.
- `_AGENT_CONTEXT/CURRENT_REVISION.json`: machine-readable current status, lineage, verification summary, and source commit to which evidence applies.
- `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`: mandatory knowledge-preservation rule that must propagate to future agents.
- `_AGENT_CONTEXT/handoff-manifest.json`: machine-readable required handoff payload.
- `scripts/Test-AgentHandoff.ps1`: fail-closed continuity preflight.
- GitHub `fengie/mhw-mods` `main`: canonical development state and authoritative diff/history.
- `scripts/Build-Source-Handoff.ps1` / `Build Source Handoff.bat`: optional export tooling that packages a clean source handoff, excludes build outputs, writes `_AGENT_CONTEXT/SOURCE_HANDOFF_MANIFEST.json` inside the generated archive, and emits a SHA-256 sidecar.

## Repair regression entrypoints

`FunctionVerifierBehaviorTests` executes the real verifier against isolated fixtures. `MultiGameTests` covers generic nativePC mappings and missing-blob recapture. `scripts/Test-VerificationCache.ps1` tests actual fingerprint functions and runs in both release entrypoints.
