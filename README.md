# v8.8.2 Universal Mod Manager

## v8.8.2 — integrated safety and diagnostics hardening

This integration combines three independently reviewed shipped safeguards: crash bisection now validates a clean control and reproducing full suspect set before it can isolate a culprit; duplicate cleanup compensates ordinary database-delete failures after an archive move without guessing through ambiguous persistence state; and shareable support bundles sanitize recent structured logs at export while preserving full-fidelity local logs.

It also adds the profile-save rollback regression, adversarial continuity-validator fixtures, and durable audits for persisted game-profile path containment and launch-observation atomicity. Duplicate cleanup crash-durable reconciliation, broader diagnostic export sanitization, remaining crash-bisector evidence risks, game-profile ID repair, and launch-observation transaction repair remain explicit follow-ups.

## v8.8.1 — updater publication verification hardening

The automatic-updater release gate now verifies a newly published updater tag through GitHub's authoritative REST git-ref API instead of requiring immediate Git transport propagation. The check fails closed unless the exact expected tag exists, points directly to a commit, and resolves to the exact source SHA being published. This prevents a successfully published immutable release from being reported as failed solely because the Git tag has not propagated to fetch transport yet.


## Repair revision — 2026-09-27

Read `REPAIR-NOTES.md` for the current repairs and validation. This revision fixes
generic scanning, missing-blob recapture, idle watcher logging, verification cache
integrity, and source packaging. The complete solution builds with zero warnings
or errors; all 158 tests and 11 self-test checks pass on the Linux validation host.
Windows UI/locking/release validation remains required via `Test Everything.bat`
and `Build.bat`. The six unchanged previously checked Windows stages are preserved.

## v8.8.0 — incremental function verification and call-error observation

v8.8.0 keeps the v8.7 universal-game architecture and adds a verification layer designed for safe iterative development. Every explicit production executable body (methods, constructors, operators, local functions, explicit accessors, and expression-bodied properties/indexers) receives a stable syntax fingerprint. The verifier keeps a boolean `verified` checklist, preserves unchanged known-good functions as checked, and marks changed/new functions as unchecked. A scan safely persists those exact booleans immediately; only a complete build/test/self-test pass can promote changed/new fingerprints to full-release confirmation.


### Granular checked-state follow-up

After the first real Windows verification run, v8.8 now preserves successful checks granularly instead of discarding them when an unrelated stage fails. `.verification/function-status.json` is rewritten on every scan as a current `verified: true/false` function checklist. `.verification/stage-status.json` records strict-build/test passes by exact project/dependency/toolchain fingerprint; unchanged matches show `PASS-CACHED` on later `Verify-Release.ps1` runs. Production release builds still execute the full release gate.

The same Windows run exposed and this package fixes the FunctionVerifier Roslyn indexer signature bug, DTO `FormatVersion` initializer bug, WPF `Thickness`/`System.IO` issues, stale `GameProfile`-aware constructor call sites, and strict CA1859 discovery-method diagnostics.

Changed/new production functions must enter through `MasterDebugLog.BeginMethod()`. Active method scopes now observe first-chance exceptions raised by nested calls, so the master log records a `PASS-CHECK` when no exception was observed and an `ERROR-CHECK` when a nested call raised an exception (even if later handled); explicit successful scopes with handled nested errors are marked `PASS-WITH-ERROR-CHECK`. This is diagnostic only: exceptions are never swallowed or converted into success. See `docs/FUNCTION-VERIFICATION.md`.


The application now supports arbitrary Windows games through conservative folder-based game profiles, while retaining Monster Hunter: World as the deepest enhanced adapter. Use **Scan games** for Steam/Epic/GOG discovery or **+ Game** to select any Windows game executable manually. Each game has its own mod library, SQLite database, staged state, rollback history, issue history, visuals and deployment manifest.

Generic profiles deliberately avoid guessing game-specific semantics: exact-path collisions remain explicit choices unless metadata/manual rules prove a relationship. Known layouts such as BepInEx, Unreal Paks, `Data`, and `Mods` are used only to choose a sensible deployment target. Configure the profile if a game uses a different mod directory, save file or Nexus game domain.

# v8.6.27 visual source fallback

Basic mod artwork no longer depends on a Nexus API key. The manager uses local screenshots/FOMOD images first, Vortex-style `pictureUrl` metadata next, then a throttled public Nexus main-image fallback when a Nexus mod ID is known. Authenticated Nexus remains optional and is used for richer metadata/update checks.

## 8.6.26 — App compile cleanup

- Fixes WPF App compilation by fully qualifying `System.IO.File.Exists` in visual-row thumbnail/gallery code.
- Reuses one `JsonSerializerOptions` instance in automation tests to eliminate CA1869.
- Preserves all v8.6.25 UX, overlap explorer, dry-run, thumbnail, update, issue-tracking and family behavior unchanged.

## 8.6.25 — UX, overlap explorer, dry-run planning and background hardening

- Added smart mod-library views: **All**, **Enabled**, **Staged**, **Updates**, **Issues**, **Revalidate**, and **Superseded**. Search composes with the active view.
- Added **Preview changes**: a true planner dry run that captures/indexes newly-enabled sources, builds the same deployment plan as Apply, reports add/replace/remove/restore counts, and redirects to **Needs attention** if a blocking choice remains. It never writes `nativePC`.
- Added **Discard staged** to return all staged state to the last applied state without touching deployed files.
- Added an **Overlaps** page: an informational, MO2-style view of assets supplied by multiple enabled mods. Resolved shared textures/family overlays are shown calmly; unresolved choices remain in **Needs attention**.
- Added keyboard shortcuts: **Ctrl+F** focus Mods search, **Ctrl+Enter** Apply, **Ctrl+Z** Undo, **F5** refresh analysis.
- Periodic Nexus metadata/artwork refresh is serialized and skipped while foreground/transactional work is active. Manual sync/import/adoption share the same gate.
- Remote thumbnails now download to bounded temporary files and are atomically renamed only after a complete successful transfer.
- Clicking through visual-heavy libraries reuses persisted gallery metadata before recursively rescanning large source folders.
- See `RESEARCH-UX-ROBUSTNESS.md` for the Vortex/MO2/Fluffy UX patterns used in this pass.

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

## Texture-family resolver correction (v8.6.22)

Unrelated texture replacements remain real choices even when they touch a shared `mod_*` namespace. A resource namespace alone, or generic words such as `recolor`, `armor`, or a color name, cannot establish family lineage. Conversely, providers already proven to share one logical family no longer conflict with themselves on texture paths: explicit saved overlay rules win first, then family priority provides the deterministic internal fallback.

## Manual family chaining (v8.6.20)
When automatic family inference misses a relationship, select the conflict in **Needs attention** and click **Make main + chain others** on the package that should be the main mod. The manager persists the relationship as a manual family, marks the selected root as Main and the other packages as Optional, and writes an explicit ordered precedence chain (Main → Optional 1 → Optional 2 → …). Optional layers are ordered by your current priorities so shared base files still resolve deterministically. **Choose only** remains available for true mutually-exclusive alternatives. Source folders are not moved or merged.

## Family-safe conflict resolution (v8.6.19)

A proven logical family is now a hard boundary in conflict resolution. A base mod and its own optional/patch/component package cannot accidentally fall through to an ordinary structural conflict. High-confidence overlays and mostly-contained component packages auto-compose. Ambiguous sibling alternatives are shown as an internal family choice instead of an unrelated-mod conflict. Explicit user incompatibility/exact-file/resource-provider rules still win. `MHW-DEBUG-ALL.log` records these decisions under `[FAMILY-CONFLICT]`.

## Generic family inference update (v8.6.19)

Family inference is now brand-neutral. HPN is only a regression test; production grouping uses explicit/manager metadata, shared Nexus/source identity, semantic naming, file overlap/subset evidence, content roots, resource namespaces, and MHW asset/model identity. Similar names or shared armor slots alone are not enough to merge unrelated mods.

## Full-process debug build (v8.6.19)

`MHW-DEBUG-ALL.log` remains the single exhaustive handoff log. v8.6.18 keeps the full tracing architecture from v8.6.14, but fixes the trace-instrumentation generator so method scopes are never inserted into C# object/collection initializers. A dedicated C# trace-placement preflight now runs before verification/build, and an integration regression test enforces the same rule.

This is intentionally very verbose. Filenames, mod IDs, paths, operation names, timing, and process IDs may appear. Secret values such as API-key contents are not intentionally emitted. If anything breaks, upload the top-level `MHW-DEBUG-ALL.log`.

## WPF binding safety (v8.6.13)

v8.6.13 fixes a startup crash where WPF attempted to write back through inline `Run.Text` bindings targeting computed/read-only ViewModel properties. Inline display bindings now explicitly use `Mode=OneWay`, and an integration test prevents regressions.

## Startup testing (v8.6.12)

After `Build.bat`, launch the built application with **`RUN BUILT APP.bat`** from this top-level folder.

That launcher intentionally keeps the development/test data root here and forces runtime diagnostics into the same top-level `MHW-DEBUG-ALL.log`. If the loading screen disappears or startup fails, upload that one file.

v8.6.12 also keeps WPF in explicit-shutdown mode throughout bootstrap and performs main-window data initialization inside the traced startup transaction, so a startup exception cannot disappear behind the splash-window lifecycle.

# MHW Manual Mod Manager

## One-file diagnostics (v8.6.11)

The top-level `MHW-DEBUG-ALL.log` is the primary debugging handoff file. **If anything fails, upload this one file.** It accumulates chronological diagnostics from PowerShell syntax checks, `Build.bat`, `Test Everything.bat`, compiler/analyzer/test output, publish, application startup, startup maintenance, normal runtime operation telemetry, watcher warnings, and unhandled exceptions. Detailed per-stage logs remain in `BuildLogs`, `StartupLogs`, and `State\Next\Logs` for deeper inspection.

Use `OPEN MASTER DEBUG LOG.bat` to open it immediately. The logger is best-effort and never intentionally makes a product operation fail.

## Compatibility intelligence + logical mods

v8.6.7 treats the folder library as immutable source material and builds a higher-level compatibility graph above it. Main packages, optional components, patches, updates, texture revisions, Nexus lineage, superseded versions, atomic model/material/physics bundles, unmanaged live files, and game-build changes all feed one resolver. The normal UI shows logical/effective mods rather than raw file-conflict noise.

The intended result is hands-off: high-confidence `MAIN → OPTIONAL → UPDATE/FIX` relationships compose automatically; identical/shared resources are deduplicated; known newer texture providers win only their overlapping paths; old revisions are archived; multipart packages remain one toggle with an internal configuration drawer. A human choice is reserved for independent logical mods that directly replace the same non-mergeable asset and cannot be safely ordered.

### Nexus lineage (optional)

Offline metadata (`mhw-manager.meta.json`, common `meta.ini` fields, stored source URLs/folder hints) is always used when available. For stronger live Nexus lineage, put your Nexus API key in `State\Next\nexus-api-key.txt` or set `NEXUS_API_KEY`, then click **Sync lineage**. Live enrichment is rate-limited during normal startup and can be forced from the UI. The manager never requires Nexus connectivity to deploy local mods.

### Manual-install adoption

At startup the manager counts untracked files already present under the live `nativePC`. **Adopt manual files** copies those files into a new immutable source package under `Mods` without changing the live game tree. The adopted path + SHA-256 is remembered; unchanged adopted files stop being offered repeatedly, while later external edits become visible again.

### Texture previews

Package screenshots/adjacent PNG/JPG/BMP files can be shown on rare texture-choice cards. Raw `.tex` conversion is best-effort and optional: configure `MHW_TEX_CONVERTER` and `TEXCONV_EXE` if you want generated PNG previews. Missing converters never block deployment.

See `docs/AUTO-COMPOSITION.md` for the safety model.

## v8.6.7 Automation + Compatibility Intelligence

### v8.6.7 verifier reliability

`Test Everything.bat` now treats the verifier as another testable subsystem. It parser-checks the active PowerShell scripts, round-trips a sample report through Windows PowerShell JSON serialization, then runs every reachable compile/analyzer/test/self-test stage. A failed native `dotnet` command is logged but does not terminate later stages. The final report uses plain PowerShell arrays for Windows PowerShell 5.1 compatibility.

## Full compiler/analyzer sweep

Run `Test Everything.bat` (recommended) or `scripts\Verify-Release.ps1`. v8.6.7 first syntax-checks all active PowerShell scripts, then performs a relaxed whole-solution build so analyzer warnings do not block downstream projects, then compiles every project independently with warnings-as-errors and continues through the entire project list. This exposes all *reachable* compiler/analyzer failures in one run instead of stopping at the first failing dependency.

Artifacts are written to `BuildLogs\`: the human-readable verification transcript, `compile-summary-*.txt`, a relaxed whole-solution `.binlog`, one strict `.binlog` per project, and the final strict solution `.binlog` when the sweep is clean. A genuine C# compiler error in an upstream project can still make a downstream assembly impossible to compile until that upstream error is corrected; the verifier reports this rather than pretending otherwise.


This is the C#/.NET successor to the v7 PowerShell/WPF manager, with the production bug-fix/stability requirements implemented directly into the planner, storage, filesystem transaction engine, diagnostics, tests, and WPF shell.

It is built for the real topology this project was designed around: 160+ source folders, 70+ simultaneously enabled mods, HPN base/component packages, shared `mod_hepsy` resources, body/texture providers, root/plugin files, and genuine structural incompatibilities.

## Do this first

**Keep your existing `Mods` folder and your entire existing `State` folder.**

v8.6.7 writes new state under:

```text
State\Next\manager.db
State\Next\Blobs\
```

Legacy v7 remains under `State\V2` and the fallback v7 source is included under `legacy-v7`. Migration is non-destructive and does not touch `nativePC` merely to import state.

## v8.2 visual redesign

The WPF shell has been rebuilt around a restrained charcoal/gold desktop design: persistent left navigation, a tighter global command bar, metric/dashboard cards, cleaner mod-library rows, stronger staged-state presentation, and consistent page toolbars/cards. The redesign intentionally leaves deployment, conflict, migration, profile, and filesystem semantics unchanged.

## What changed in the hardening pass

The main goal is not more features; it is proving that the features already present survive load, concurrency, external edits, and crashes.

- whole-plan preflight before the first filesystem write;
- per-file TOCTOU revalidation immediately before mutation;
- durable operation journal with explicit recovery states;
- one atomic SQLite metadata commit for manifest + ownership + mod state + `Committed` marker;
- recovery that refuses to overwrite unknown post-crash edits;
- safer `ReplaceFileW`-based existing-file replacement;
- connection-per-operation SQLite/WAL instead of shared-cache coupling;
- one-pass/indexed conflict analysis and sparse incompatibility lookup;
- no hidden O(paths²) decision lookup in the planner;
- XXH3 verification of metadata cache hits so same-size/same-timestamp source edits are detected;
- indexed armor-component coverage instead of wildcard path scans;
- batched/versioned armor DB startup import;
- batched WPF collection replacement and preserved DataGrid virtualization;
- planner/archive/hash/migration/health work kept away from the Dispatcher;
- structured correlation IDs, classified errors, ThreadPool/GC metrics and Dispatcher stall detection;
- Restart Manager lock-owner diagnostics;
- secure archive path/device/ADS/reparse checks;
- expanded crash-phase, stale-plan, cache, archive, large-conflict and deterministic-planner tests.

See `docs/BUG-AUDIT.md` for the concrete failures/root causes and `docs/DIAGNOSTICS.md` for hang capture.

## Conflict semantics remain conservative

Enabled mod state and winning file provider are separate concepts. Several enabled mods may coexist while one wins a particular overlapping path.

The manager can automatically resolve byte-identical files, shared textures/resources, remembered overlays, high-confidence component/patch relationships, dedicated texture providers, and newer related texture revisions. Unknown structural/model/material/physics/plugin/game-data collisions fail closed until there is an explicit rule.

Different bytes are never described as merged unless an actual file-format merger exists.

## Build and verify on Windows

Use the current .NET 10 SDK (the repository pins SDK `10.0.401` in `global.json`). Then run:

```powershell
.\scripts\Verify-Release.ps1 -RunBenchmarks
```

If that passes, build the self-contained Windows x64 release:

```powershell
.\scripts\Build-Release.ps1
```

The final binary ZIP is produced under `artifacts` by the build script.

This source package was assembled in a Linux execution container without a local .NET SDK or Windows WPF runtime, so **a compiled executable is intentionally not claimed in this package**. `VALIDATION.md` describes the exact boundary between static validation here and runtime validation required on Windows.


## Startup diagnostics

Every application launch writes a verifier-style startup trace under `StartupLogs` beside the manager (or under `%LOCALAPPDATA%\MhwModManager\StartupLogs` if the manager directory is not writable). Each launch produces:

- `startup-YYYYMMDD-HHMMSS-fff.log` — human-readable ordered stages with START/PASSED/FAILED, elapsed time, context, and full exceptions.
- `startup-YYYYMMDD-HHMMSS-fff.json` — machine-readable stage results for debugging/comparison.

The trace covers path discovery, database initialization, service composition, migration, catalog refresh, armor import, Nexus/game-build intelligence, recovery, each startup-automation substage, main-window creation, and watchdog startup. Startup maintenance attempts independent safe substages even after one fails so the report captures more than the first error. The failure dialog prints both log paths. `Open Startup Logs.bat` opens the folder directly.

## Diagnosing a freeze

The app records structured timings automatically. For deeper evidence, click **Activity -> Capture diagnostics**, or run:

```powershell
.\scripts\Capture-Diagnostics.ps1 -ProcessId <PID>
```

When installed, the script uses `dotnet-stack`, `dotnet-counters`, `dotnet-trace`, and `dotnet-gcdump`. The support bundle stays bounded and does not automatically copy mod assets/CAS blobs.

## Safe upgrade procedure

1. Close MHW and v7.
2. Back up the manager folder if you want an additional external copy.
3. **Do not delete `Mods` or `State`.**
4. Compile/verify v8.5.0 with the scripts above, or use a Windows build produced by them.
5. Put the published v8.5.0 files in the manager root next to your existing `Mods` and `State`.
6. Launch `MHW Mod Manager.exe`.
7. Read the migration report before first Apply.
8. Run Health.
9. Test a small staged deployment before a mass profile switch.
10. Keep `legacy-v7` until migration, one Apply, Health, restart, and Undo have all been verified.

## Source layout

- `src/MhwModManager.App` — WPF/MVVM shell
- `src/MhwModManager.Core` — domain, rules, conflict engine, planner
- `src/MhwModManager.Storage` — SQLite/WAL, schema, profiles, v7 migration
- `src/MhwModManager.Filesystem` — CAS/hashing/scanning/archive/deployment/recovery/Restart Manager
- `src/MhwModManager.Mhw` — game discovery and armor catalog
- `src/MhwModManager.Diagnostics` — telemetry, health, exception policy, support bundle
- `tests/` — deterministic/unit + Windows filesystem/failure-injection integration tests
- `benchmarks/` — realistic planner stress fixtures
- `docs/` — architecture, failure modes, migration, schema, diagnostics and bug audit
## v8.6 automation workflow

The default workflow is now intended to be almost hands-free. Drop archives/folders into `Inbox\` or keep using `Mods\`; startup maintenance imports safe inbox content, learns lineage, assigns categories, and archives safe disabled duplicates/superseded revisions. The main action is **JUST PLAY**: it applies staged changes, adopts trackable loose `nativePC` files, snapshots the MHW save and manager state, runs the health/dependency/conflict gate, launches the game, and records a Last Known Good setup when startup survives.

If startup begins failing after newly enabling mods, **Auto-diagnose startup crash** performs a binary-search style bisect against Last Known Good and restores the original setup after diagnosis. Genuine unresolved structural alternatives or missing dependencies still stop rather than being guessed.

For complete verification, double-click `Test Everything.bat` (or `Verify.bat`). The verifier continues through all reachable compile/analyzer/test groups even when one fails and writes `BuildLogs\verification-report-*.md` plus a machine-readable `.json` report. See `docs\AUTOMATION-AND-TESTING.md` for details.


### v8.6.8 build diagnostics
`Build.bat` now writes stage-specific logs under `BuildLogs`. The release builder performs a RID-specific ReadyToRun restore before publish. If only the SDK ReadyToRun optimization phase fails, it records the failure and retries a self-contained JIT publish; application correctness/tests are unchanged.

### v8.6.9 publish analyzer preflight

Release builds now compile the exact `win-x64` App target with warnings-as-errors before publish. Startup database initialization also forwards the diagnostic cancellation token explicitly, resolving CA2016 in RID-specific publish builds.

### v8.6.10 build harness exit-code fix

The release builder now consumes `Tee-Object` output with `Out-Host` and returns only the native `dotnet` exit code from each stage. This prevents successful stages from being misread as failures when their console text is captured alongside exit code `0`. A harness contract check fails immediately if any stage ever returns a non-scalar/non-integer result.

## Generic mod-family inference (v8.6.18)

Family detection is evidence-based and is not tied to HPN or any other author/series. The engine prefers explicit/manual family IDs, imported manager metadata such as `logicalFileName`, and shared Nexus source identity. When those are unavailable it combines semantic name lineage with concrete file evidence: subset/overlap ratios, shared content roots, resource namespaces, and MHW armor/model identity.

Names alone do not collapse packages. Two similarly named mods with disjoint layouts stay separate, and two unrelated replacements of the same armor model stay separate unless there is additional lineage evidence. Nexus Main/Optional/Misc labels are treated as descriptive metadata rather than structural truth.

Accepted heuristic pairings are written to `MHW-DEBUG-ALL.log` as `[FAMILY] GENERIC ... score=... evidence=...` so every automatic grouping can be audited.

## Git-first agent continuity

GitHub `fengie/mhw-mods` on `main` is the canonical development state. Repository-aware coding agents should read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, and `_AGENT_CONTEXT/CURRENT_REVISION.json` before changing code, then follow the full continuity protocol. Update `_AGENT_CONTEXT/` and commit the handoff state with the code it describes. Every shipped application change must also bump the app version in `VERSION.txt` and `Directory.Build.props`, keep duplicated release/update metadata aligned, update this README, and add the matching `CHANGELOG.md` entry before the work is considered complete. Documentation/agent-policy/evidence-only changes that do not change the shipped application do not require an app-version bump. Run `scripts/Test-AgentHandoff.ps1` before declaring work complete. `Build Source Handoff.bat` remains available when a reproducible source ZIP export is useful.

## Verification closure status

The second Windows/.NET 10.0.401 verification run reached **24 PASS / 1 FAIL**. Every compile/analyzer/test/self-test stage passed; the sole failure was four missing entry traces detected by the function scanner. This source handoff adds exactly those four `MasterDebugLog.BeginMethod()` scopes. Run `Test Everything.bat` once more on Windows to confirm zero function trace/call-site coverage gaps and allow exact-current function fingerprints to be promoted to `verified=true`.
