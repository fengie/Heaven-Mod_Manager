# v8.8.0 function verification validation

The v8.8 verifier adds two gates before cache promotion: a Roslyn function-fingerprint scan and runtime-trace coverage for every changed/new production function. The previous `verified=true` cache is immutable on any failing run. Only a complete PASS may promote current fingerprints.

The source package also includes `.verification/trusted-v8.7.0-files.json` and `.verification/trusted-v8.7.0-src.zip` as read-only bootstrap evidence from the user-supplied v8.7.0 baseline. They are not runtime inputs to the mod manager.

# v8.7.0 universal game support validation

Regression expectations:
- generic profile accepts safe targets such as `Mods`, `BepInEx\plugins`, and Unreal `Content\Paks\~mods`; rooted/traversal targets are rejected;
- generic scanner maps bare packages under the configured target and preserves packages that already mirror the target;
- generic same-path texture replacements remain blocking choices instead of inheriting MHW texture heuristics;
- MHW retains enhanced semantics;
- Steam/Epic/GOG discovery only registers installations with a usable executable; manual EXE selection remains available;
- generic root profiles disable live-file adoption to avoid treating an entire game installation as a mod folder;
- per-game database/workspace isolation and transactional deployment remain unchanged.

# v8.6.27 visual regression coverage

- Visual pipeline recognizes Vortex `pictureUrl` metadata.
- Known Nexus IDs have a no-key public main-image fallback.
- UI explicitly reports visual-source counts and no longer claims an API key is required for thumbnails.
- Unsupported remote image media types are rejected rather than mislabeled as JPEG.

## v8.6.26 compile-fix validation

- Regression target: `Rows.cs` no longer contains unqualified `File.Exists` references.
- Regression target: automation tests no longer allocate `JsonSerializerOptions` per serialization.
- XML/XAML/project/JSON parse and static C# lexical/delimiter checks performed before packaging.
- Windows `.NET 10` verifier remains the authoritative runtime/compiler gate.

## v8.6.25 UX / robustness validation

The Windows verifier remains authoritative. This pass adds regression checks for smart views, dry-run/discard actions, the Overlaps page, keyboard bindings, metadata-refresh serialization, atomic remote-image cache writes, and lazy visual rescanning.

Expected Windows flow:

1. `Test Everything.bat`
2. `Build.bat`
3. `RUN BUILT APP.bat`

Manual smoke checks:
- Ctrl+F focuses Mods search.
- Smart views combine correctly with search.
- Preview changes reports an exact plan without modifying `nativePC`.
- Discard staged restores UI state to the applied configuration.
- Overlaps shows normal multi-provider assets without adding blockers.
- Background metadata sync never starts while another UI/transactional operation is active.
- Interrupted thumbnail downloads do not leave final cache files.

## 8.6.24 — Visual library, Nexus/Vortex artwork and automatic update checks

- Mod rows now show cached thumbnails; selecting a mod expands a visual gallery of Nexus artwork, FOMOD/Vortex installer images, and screenshots found inside the source package.
- Nexus v3 `thumbnail_url` / `picture_url` / `image_url` artwork is cached under `State\Next\PreviewCache\Nexus` and refreshed automatically during metadata sync.
- FOMOD `Info.xml`/`ModuleConfig.xml` `<Image>` references are recognized as author-supplied visual metadata.
- Outfit Coverage now shows a preview thumbnail and supplying mod names for each armor/model row.
- Conflict thumbnails use the same safe image decoder. Corrupt image files fail closed instead of crashing the UI.
- Nexus metadata/artwork refreshes automatically while the app is open; update chains are checked daily and logical mods get an `Update available` badge. Updates are detected automatically but never silently installed/deployed.
- Manual **Sync metadata + visuals** forces an immediate refresh.

## 8.6.23 — Mod issue fallback / suspect tracker

- Added persistent per-mod issue suspect records for startup crashes, general game crashes, GPU/graphics crashes, and crash-bisector isolation.
- Automatic startup failures compare the failing launch against the previous successful modded launch and mark likely changed/enabled mods.
- Added one-click **Report game crash** and **Report GPU/graphics crash** actions for failures that happen after the 15-second startup observation window.
- GPU reports weight texture-heavy packages more strongly; startup/game reports weight plugin, executable, game-data, and structural content more strongly. Prior successful launches reduce suspicion while prior failures increase it.
- Suspects appear in **Needs attention** and as warning badges in the Mods list. Marks are advisory and never change files or enabled state.
- Automatic crash-bisector results are persisted as 99% **ISOLATED** marks.
- Added dismiss/clear controls for false positives and master-log `[MOD-ISSUE]` diagnostics.
- Database schema bumped to v5 with `mod_issue_suspects`.


## 8.6.22 — Shared texture resources + texture safety gate
- Treat shared body/skin textures embedded inside broader armor/outfit packages as one shared resource provider instead of a whole-mod conflict.
- Keep dedicated independent texture/recolor packs blocking unless lineage or an explicit provider rule proves they are related.
- Add pre-launch/Health validation for enabled MHW `.tex` sources: missing/unreadable sources, post-index size changes, truncated files, and invalid TEX signatures are surfaced before launch.
- Invalid/truncated TEX sources are launch blockers and are written to `MHW-DEBUG-ALL.log` under `[TEXTURE-SAFETY]`.
- This gate catches obvious malformed mod textures; it does not claim every MHW ERR12/GPU-device crash is caused by a mod.

## v8.6.22 texture/family regression coverage

Added focused regression coverage for unrelated recolors in a shared resource namespace and for deterministic nonblocking texture resolution inside a proven logical family. Existing randomized texture-graph invariants remain required.

## v8.6.20 manual family chaining
- Manual-chain integration regression: a generic structural collision blocks before chaining, then resolves as an explicit `UserOverlayRule` with the optional child winning after `ChainManualFamilyAsync`.
- Three-layer regression verifies Main + Optional A + Optional B can all carry the same structural path without another self-conflict; the last optional layer wins deterministically.
- Old pair-level incompatibility regression verifies an explicit manual chain replaces the stale relationship between the groups the user just chained.
- Persistence regression verifies both packages receive the same manual family ID and explicit `Main` / `Optional` roles.
- UI exposes separate `Choose only` and `Make main + chain others` actions on every blocking conflict candidate.
- Manual chain keeps source folders untouched and restores existing staged ON/OFF state after logical rows are rebuilt.

## v8.6.19 family-conflict invariant

Regression targets:
- a proven family Base + Optional structural overlap must be non-blocking `ModFamilyOption`;
- a same-family unknown-name smaller subset must compose as `family-subset-component`;
- ambiguous same-family siblings must become blocking `family-internal-choice`, never `HardStructural`, `HardGameData`, or `HardUnknown`;
- explicit `Incompatible` rules must still block even when both mods share a family;
- the v8.6.18 manager-metadata regression test must use escaped `\n` inside C# string literals.

## v8.6.17 family-inference validation (superseded by v8.6.18 generic engine)

- Regression: same Nexus mod ID + all files categorized Main => one logical family.
- Regression: different Nexus mod IDs remain separate even with identical HPN display names.
- Regression: `Free the Nipples` is recognized as an HPN component suffix.
- Existing trace-placement, XAML binding, full verifier, build, publish, and runtime diagnostics remain enabled.

## v8.6.17 publish-analyzer validation

- `WpfMasterTraceListener.TraceEvent(..., string? format, ...)` matches the .NET 10 base signature.
- Null formats are handled explicitly.
- No full-tracing behavior removed.

## v8.6.17 trace-placement validation
- C# trace-placement preflight must report zero initializer violations.
- All previous full-process tracing remains enabled.
- Build/Test/Verify call the trace-placement preflight before invoking dotnet.
- Integration coverage rejects `BeginMethod()` statements directly inside `= new ... { ... }` initializers.

## v8.6.14 full-process trace validation

The debug build must preserve the existing 21-gate verifier and release build while producing one root master trace. Static coverage audit on this package found 328 block-bodied source methods instrumented and no untracked direct `Process.Start` call outside the central process tracer. Runtime validation should confirm method `START`/`END` entries, operation IDs, first-chance exception records, WPF trace records, process start/exit records, database transaction markers, filesystem watcher events, and per-file deployment records. The master logger is best-effort and must never throw into product code.

## v8.6.13 WPF binding regression

- `MainWindow.xaml` inline `Run.Text` bindings are explicitly `Mode=OneWay`.
- Integration test `XamlBindingSafetyTests.InlineRunBindingsAreExplicitlyOneWay` guards against the startup failure class.
- No deployment, planner, database, automation, or path-safety behavior was changed.

## v8.6.12 startup lifecycle validation

- Build, verifier, and runtime diagnostics converge on project-root `MHW-DEBUG-ALL.log` when using `RUN BUILT APP.bat`.
- Startup uses `ShutdownMode.OnExplicitShutdown` until main-window initialization and `Show()` both succeed.
- Main-window initialization is a named startup-diagnostic stage (`ui.main-window.initialize`) rather than an untracked async Loaded handler.
- Dispatcher-unhandled exceptions are logged and surfaced before controlled shutdown.
- Development release-folder launches auto-detect the parent project root for master diagnostics.

## v8.6.11 unified master diagnostics

Validation target: `MHW-DEBUG-ALL.log` exists at the package root and receives PowerShell syntax, build, verifier, dotnet stage output, startup diagnostic entries, runtime Serilog events, telemetry signals, and unhandled exception records. Existing detailed logs remain intact. `OPEN MASTER DEBUG LOG.bat` provides one-click access. Logging paths are best-effort and must not throw into product code.

## v8.6.7 startup diagnostic trace

Startup now emits a persistent verifier-style text log and JSON report before path discovery begins. The trace is written incrementally, so failures during early path/database/service setup still leave evidence. Startup automation logs Inbox, categories, duplicate archive/analysis, dependency scan, update diffs, and timeline recording as separate stages and safely attempts the remaining independent stages before surfacing an aggregate failure.

The startup failure dialog includes both generated log paths. Root helper `Open Startup Logs.bat` opens the trace directory.

## v8.6.6 App compile/analyzer cleanup

The v8.6.4 Windows verification run again reached **18 passed / 3 failed**. Core, Storage, Filesystem, MHW, Diagnostics, Automation, UnitTests, AutomationTests, IntegrationTests, Benchmarks, and SelfTest all compiled successfully, and all four runtime test stages passed. The remaining failures were limited to the App/whole-solution compile path.

The reported diagnostics were:

- `CS1061` in `MainWindowViewModel.cs`: a conditional tuple lost its `identity` element name on the fallback branch.
- `CA1822` in `Rows.cs`: `ConflictRow.Blocking` was an always-true instance property. The displayed `Conflicts` collection already contains blocker rows only, so `BlockerCount` now reads the collection count directly and the redundant property is removed.
- `CA1826` in `MainWindowViewModel.cs`: logical members are an indexable `IReadOnlyList`; enabled/fallback selection now uses indexed access rather than LINQ `FirstOrDefault`.
- `CA1822` in `NexusMetadataService.cs`: `TryFetchNexusAsync` uses only static helpers/state and is now static.

No planner, deployment, persistence, migration, conflict-resolution, recovery, or test semantics were weakened.

Run `Test Everything.bat` on Windows to execute the authoritative .NET 10 verification suite.


### v8.6.8 build diagnostics
`Build.bat` now writes stage-specific logs under `BuildLogs`. The release builder performs a RID-specific ReadyToRun restore before publish. If only the SDK ReadyToRun optimization phase fails, it records the failure and retries a self-contained JIT publish; application correctness/tests are unchanged.

### v8.6.9 RID analyzer gate

`Build-Release.ps1` must pass `App win-x64 compile/analyzers` before the ReadyToRun restore/publish stages. This catches runtime-identifier-specific compiler/analyzer diagnostics before artifact production.

### v8.6.10 build harness contract

`Invoke-DotNetStage` must emit exactly one success-pipeline value: the integer native process exit code. Console output is tee'd to its stage log and consumed by `Out-Host`. `Assert-ScalarExitCode` guards the contract before any stage result is compared.

## v8.6.18 generic family inference

Added regression coverage for brand-neutral family behavior:
- multiple `Main` files with the same Nexus/source mod identity group as one logical family;
- local base/component packages can group from semantic name lineage plus strong file subset/model evidence;
- similar names with disjoint file layouts remain separate;
- unrelated mods touching the same armor/model identity remain separate without additional evidence.

Static packaging validation also checks that `GenericFamilyInference.cs` contains no HPN/Hepsy brand tokens.


## v8.8.0 post-re-audit continuity and verifier hardening

The source-handoff revision adds a fail-closed continuity preflight and source packager. `scripts/Test-AgentHandoff.ps1` validates the handoff manifest, version alignment, required context/verification/tooling files, and the recursive "Do not break the chain" instruction. `scripts/Build-Source-Handoff.ps1` creates a source-focused archive and a per-file SHA-256 manifest while excluding generated/output folders.

Function verification now excludes generated `bin`/`obj` C# files, validates the embedded trusted v8.7 source archive against all 73 expected SHA-256 hashes before using it, rejects duplicate stable function IDs, distinguishes explicit-interface implementations, and records explicit call-site coverage. Nested local-function bodies are counted by their own function identities rather than double-counted in the containing function.

First-chance exception observation remains active for every live method scope, but full per-throw exception/stack disk logging is opt-in through `MHW_FIRST_CHANCE_DETAIL=1`. Aggregate first-chance counts are retained in process-end diagnostics.

Artifact-environment static validation for this revision: JSON/XML parse clean, all solution project paths resolve, all 73 trusted baseline hashes match, and a lexical/delimiter audit over 95 non-generated C# files found no structural mismatches. The authoritative .NET 10.0.401 Windows compile/analyzer/test/self-test/publish pipeline was **not** executable in this environment and must still be run with `Test Everything.bat` / the release builder before this revision is promoted as compiler-verified.
