# Current candidate — PlannerSnapshotRepository extraction REPAIRED after first hosted failure

Initial extraction source: `8e0068bd44cc6735ffa9478067923ad5d9c54506`.
Focused regression-test hardening: `64e666a19ce17c21bc696b46cce9c07bb257a686`.
Repair source: `528401925b1d09b3d65c9652de8e4f2024e3677f`.

Hosted run `36335255922` on exact candidate `161b5fcba88470b7d941a3831624bdbf071ff668` failed and is preserved as superseded evidence. The exact root causes were two LR-001 trace gaps (`RestoreLastGood`, `LaunchSafeMode`) and two missed callers (`NexusMetadataService`, `GameBuildMonitor`) that still referenced the removed ManagerDatabase method. Release/publish was skipped and no verification state was persisted. The repair changes only this boundary.

This is the one production boundary authorized by the MainWindow/storage audit. It is **not yet Windows-verified**.

- Added read-only `PlannerSnapshotRepository` in Storage and moved the former `ManagerDatabase.LoadPlannerSnapshotAsync` query assembly into it.
- `ManagerDatabase.GetModsAsync` remains where it was.
- Connection semantics are intentionally unchanged: mod state is loaded first through `GetModsAsync`; the remaining planner tables are then read through a second `OpenAsync` connection. No new SQLite transaction/snapshot model was introduced.
- Full, filtered `fileModIds`, and empty-filter behavior are preserved.
- MainWindow analysis/restore/safe-mode, EffectiveInspector, GameUpdateImpact, LaunchHealthGate, UnmanagedAdoption, and Health now consume the composed repository.
- Focused integration coverage compares the extracted repository against the exact pre-extraction query assembly for full/filtered/empty snapshots, compares representative planner output, and covers already-canceled cancellation.
- DeploymentExecutor and every documented write-side transaction boundary remain untouched.
- No second storage extraction may begin until a fresh exact Windows Release Gate closes this source boundary.

# Governance checkpoint CLOSED — permanent recursive continuity

A governance-only checkpoint now installs the repository-level continuity constitution requested by the user.

- `AGENTS.md` remains concise and points every agent into the permanent system before edits.
- `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` is the permanent Core-Rule constitution. Recursive propagation itself may be weakened only with explicit user authorization.
- `_AGENT_CONTEXT/LEARNED_RULES.md` is the append-only Active/Superseded ledger for durable incident-driven rules.
- `scripts/Test-AgentHandoff.ps1` validates the invariant by concept rather than exact prose.
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1` deliberately breaks learned-rules linkage, successor propagation, and Core-Rule protection and requires the real validator to fail closed.
- `scripts/Verify-Release.ps1` runs both the positive handoff preflight and negative fixtures.
- No production C# changed. The previously closed product baseline remains exact commit `106a4569b572473394aa075bcfa5d9c03f2fe44d`, hosted run `36331057943`.
- Hosted Windows Release Gate `36333960215` verified exact governance commit `73f1298455ec4c651e211488ececf9803504e60d` on Windows x64 / .NET SDK 10.0.401.
- `scripts/Test-AgentHandoff.ps1` passed; the baseline negative-fixture copy passed; all four recursive-continuity negative fixtures were rejected as intended.
- Repository verification finished **25/25** with **615/615** production fingerprints, **6389** explicit call sites / **0** uncovered, Core **79/79**, Automation **20/20**, Integration/fault injection **66/66**, self-test **11/11**, App win-x64 analyzers PASS, and ReadyToRun publish PASS.
- Release SHA-256: `8A8D78DA53AE81703091683F5BC25D298C7BDEE3FB831098040D91EB2F85AAF4`.
- Workflow evidence/cache persistence commit: `6bc50de3f07015b63c58ac6bfba3b7bfce9a104c`.
- The next product boundary is now **PlannerSnapshotRepository only**.

Every future handoff must explicitly require the successor to preserve and recursively pass this system to the agent after them.

**Do not break the chain.**

---

# Current state — v8.8.0

## MainWindow re-audit / storage transaction design COMPLETE — documentation checkpoint

Canonical audit base: `0129607a0558da6a596e1688a04f3051e5f6ce40`.

No production source changed in this checkpoint. The closed hosted Windows
verification therefore still applies to exact commit
`106a4569b572473394aa075bcfa5d9c03f2fe44d`, with last production source
`fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`.

The remaining `MainWindowViewModel` responsibilities were re-audited by
state ownership and coupling. Decision: **stop page-model splitting now**.
Activity, Coverage, Profiles, and Games are the clean passive/read seams already
closed. Mods/ModsView, Conflicts, IssueSuspects, Overlaps, staging/apply,
Nexus/import, profile mutations, launch/crash diagnosis, health/support and
global busy/status are either cross-feature workflows or mutually coupled state.

Durable audit:
- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md`

The fallback ManagerDatabase transaction-boundary audit is also complete.
Critical existing transaction owners are documented, especially
DeploymentExecutor prepared-journal, final commit, rollback, mod-file replacement,
manual family chaining, profile save, adoption, trust and issue batches.

Durable audit:
- `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`

Exactly one first storage extraction is recommended: a **read-only
PlannerSnapshotRepository** containing the existing
`LoadPlannerSnapshotAsync` query assembly. Do not combine it with another
repository move, transaction redesign, Generic Host/DI work, or MainWindow
decomposition. Preserve the current query and connection semantics on the first
move, add focused parity tests, then require a fresh full Windows Release Gate
before any second production change.


## Games list-presentation slice CLOSED — hosted Windows

The Games extraction is fully green.

- exact verified commit: `106a4569b572473394aa075bcfa5d9c03f2fe44d`
- last production-source change: `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`
- Windows Release Gate: `36331057943`
- repository verification: **25/25 PASS**
- production fingerprints: **615/615 promoted**
- explicit call sites: **6389**, uncovered **0**
- trace gaps / parse errors: **0 / 0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **66/66**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `4872ABDA6D548CB9F97668AF1A3019AC44B146A9A66876F92B065D7009455189`
- evidence/cache persistence commit: `750a3232ad9ac82bd1587ddd903709b886c9b8bb`

The first Games run `36330808544` is superseded. It reached 24/25 and exposed
the one missing entry trace in the newly moved `ScanInstalledGames` method.
The final source adds that trace and a regression assertion.

The verified architecture keeps only passive list state in
`GamesPageViewModel`. Selection, registry mutation, discovery, active-game
changes, restart/shutdown, busy/status coordination, AppPaths discovery, and
startup service reconstruction remain shell/application responsibilities.

## Profiles read/list page-view-model slice CLOSED — hosted Windows

The Profiles read/list extraction is fully green.

- exact verified commit: `04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8`
- last production-source change: `19a1ad4a4e665e4ce7f38586dea5f08f1c3acdf0`
- Windows Release Gate: `36328183152`
- repository verification: **25/25 PASS**
- production fingerprints: **613/613 promoted**
- explicit call sites: **6385**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **65/65**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `A150FFA7B832C56535A9CA19DCFEDD7640C3BE1F8E9ACB4D40881CB8C69F93B8`
- evidence/cache persistence commit: `122bcdb525bb432e73dff6f5887a63e065246964`

This verifies `ProfilesPageViewModel` owning profile list reads/state while
preserving the legacy `Profiles` / `RefreshProfilesCommand` binding surface.
Profile mutations, selected-profile behavior, mod staging/application, and
shell-global busy/status ownership remain in `MainWindowViewModel`.


## Coverage page-view-model slice CLOSED — hosted Windows

The Coverage extraction is fully green.

- exact verified commit: `e3ed3be730000d1829b02e5d2d29b3f23ca52d94`
- last production-source change: `855f6e5eb4998aa442538636b76f5c644146eb6a`
- Windows Release Gate: `36327634813`
- repository verification: **25/25 PASS**
- production fingerprints: **611/611 promoted**
- explicit call sites: **6379**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **64/64**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `40B47B6E3C9CF21A0945095FE28E118540B415FBD9177190BDB99FE80C9657A8`
- evidence/cache persistence commit: `e62e7ad5d93cdb6c6ae3d6e8562d6fae667e9c02`

This verifies the real `CoveragePageViewModel` extraction while preserving the
legacy `OutfitRows` / `RefreshOutfitsCommand` binding surface and keeping
shell-global busy/status ownership in `MainWindowViewModel`.


## Activity page-view-model slice CLOSED — hosted Windows

The first post-closure architecture slice is fully green.

- exact verified commit: `5eab48f0a2139e3aee96a7c71e4466d2e1168877`
- last production-source change: `e2396c7c91c5d8d88fe229603689539b5cdfb2da`
- Windows Release Gate: `36325994246`
- repository verification: **25/25 PASS**
- production fingerprints: **609/609 promoted**
- explicit call sites: **6375**, uncovered **0**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **63/63**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- release ZIP SHA-256: `7ED67747DADE8BD3E56D30139BF39886F5E0A293F9CAE9B2887D06D720F589AF`
- evidence/cache persistence commit: `f0221e545ab4bf75b989d985dc3e5a90e2faa6fc`

This verifies the real `ActivityPageViewModel` extraction while preserving the
legacy `ActivityRows` / `RefreshActivityCommand` binding surface. The malformed
intermediate run `36325764389` remains superseded historical evidence only.

## Activity candidate serialization correction

The first Activity extraction source commit `e2396c7c91c5d8d88fe229603689539b5cdfb2da` accidentally
contained literal `\\n` text in two generated replacement strings inside
`MainWindowViewModel.cs`. This was detected by source inspection before relying
on CI. Run `36325764389` is superseded.

Corrected production source: `e2396c7c91c5d8d88fe229603689539b5cdfb2da`.

The corrected source contains normal C# declarations and constructor statements.
No intended Activity architecture or runtime behavior changed; only the connector
serialization defect was removed. A fresh complete Windows Release Gate is
required for this corrected source.

## Activity page-view-model candidate — awaiting hosted Windows verification

Production source commit `e2396c7c91c5d8d88fe229603689539b5cdfb2da` begins the first post-closure
architecture slice.

- Added `ActivityPageViewModel` to own recent Activity read projection and row
  collection state.
- `MainWindowViewModel` composes the page model but continues exposing the
  same `ActivityRows` collection reference.
- `RefreshActivityCommand` and `RunBusy` remain in `MainWindowViewModel`,
  preserving cross-page/global operation coordination.
- The existing XAML binding surface is unchanged.
- A source-level integration guard asserts the page seam and the preserved
  bindings.
- No database/deployment/conflict/filesystem semantics changed.

This candidate does **not** inherit the closed green state from
`9717a22d3338f77e63cd409a80d2ec5fc3c924f2`. A fresh Windows gate is required.

## Architecture / Explain Why milestone CLOSED — hosted Windows

The integrated architecture/Explain Why checkpoint is now fully closed.

- exact verified commit: `9717a22d3338f77e63cd409a80d2ec5fc3c924f2`
- last production-source change: `098d617bcb3dcdd044e3fdb8319ba506c97082af`
- GitHub Actions Windows Release Gate: `36325133722`
- repository verification: **25/25 PASS**
- production function fingerprints: **607/607 promoted**
- Core tests: **79/79**
- Automation tests: **20/20**
- Integration/fault injection: **62/62**
- automation self-test: **11/11**
- win-x64 compile/analyzers: PASS
- self-contained ReadyToRun publish: PASS
- release ZIP SHA-256: `DF87A48716596ABFFF545DD6C73BAAE02954167424908850D943BFFA3833A2D6`
- promoted-cache/evidence persistence commit: `702c9055bff19caa80fdd60e29e891181932217a`

The previous CA1826 failure was repaired by direct `IReadOnlyList` Count/indexer
access in overlap primary-path selection; no resolver/deployment semantics changed.
This checkpoint is the required stable base for the next incremental architecture
slice. Any new production-source edit invalidates the applicable source evidence
until a fresh verifier/gate run confirms the changed fingerprints.
## Architecture candidate release-gate follow-up — 2026-09-27

GitHub Actions run `36324750213` on integrated main checkpoint
`35abe5c7425456675086cdc38455a8447d3560c5` passed the exact repository
verification gate, including the continuity preflight, strict builds/tests,
Automation **20/20**, Integration/fault injection **62/62**, and all **11**
self-tests. The release stage then failed at the dedicated win-x64
compile/analyzer gate on a single diagnostic:

- `MainWindowViewModel.Overlaps.cs:22` — CA1826, LINQ
  `FirstOrDefault()` used on indexable `IReadOnlyList<string>`.

Production source commit `098d617bcb3dcdd044e3fdb8319ba506c97082af` changes only that expression to
`Count` + indexer access while preserving the same empty-list fallback to the
asset key. No deployment, conflict-resolution, database, or filesystem safety
semantics changed. A fresh complete Windows Release Gate is required before the
architecture/Explain Why milestone can be marked closed.

## Architecture / Explain Why candidate — 2026-09-27

Working branch: `agent/architecture-explain-why`. Candidate production source:
`5c1937e557aa9996cef493709e94a6aa611d4e1c` (later branch commits update handoff docs only).
This candidate is **not yet Windows-verified** and must not inherit the closed v8.8
fingerprints simply because its parent release was green.

Implemented in this candidate:

- `PresentationReadRepository` owns Activity and Outfit/Coverage read projections that
  were previously handwritten SQL inside `MainWindowViewModel`.
- `ArchiveImportService` owns archive validation, quarantine extraction, single-wrapper
  normalization, destination naming, publication into the mod library, and catalog refresh.
- `EffectiveInspectorService.ExplainWhyAsync` replays the configured `DeploymentPlanner`
  and exposes the resulting `ConflictDecision` plus applied-manifest state, provider
  priority, logical family role, Nexus lineage, provenance, confidence, score and evidence.
- The Overlaps tab now supports `Explain selected` with progressive detail instead of
  requiring users to infer resolver behavior from overwrite rows.
- Activity, Coverage, Import, and Overlap/Explain methods moved into partial feature
  files. The central `MainWindowViewModel.cs` fell from about 72.6 KB to 65.7 KB while
  retaining the same WPF binding type and commands.
- Regression coverage was added for planner-backed Explain Why, presentation reads,
  and the new XAML binding surface.

No deployment executor, CAS, journal, rollback/recovery, TOCTOU, `ReplaceFileW`, or
live-tree safety semantics were redesigned. FOMOD and the enhanced-game adapter redesign
remain explicitly out of scope.


## Latest state: 2026-09-27 repair audit

The exact parent is the supplied v8.8.0 FunctionVerification ZIP (four trace-scope
fixes already included). See `AUDIT-2026-09-27.md` for the new source changes.
SDK 10.0.401 strict Release solution build passed with zero warnings/errors;
Core 79/79, Automation 18/18, Integration 61/61, self-test 11/11 passed on Linux.
Windows validation is still required. The current scan persists all 602 function
entries: 579 exact known-good true and 23 changed/new false, with zero gaps.
The original seven stage-cache records are retained byte-for-byte; six still match
their Windows fingerprints, while FunctionVerifier changed and must rerun.
No local Linux results were imported as Windows release checks or function promotion.

Sections below document the preceding revisions, not the latest verification status.

## What changed from v8.7.0

Product behavior and the universal-game architecture were intentionally left largely intact. This is a hardening/versioning release centered on verification and diagnostics.

- Version bumped to `8.8.0` / `8.8.0-function-verification`.
- Added `MhwModManager.FunctionVerifier` to the solution.
- Added persistent per-function/body boolean verification state.
- Added a read-only v8.7 source/hash bootstrap so unchanged functions can stay known-good even when another function in the same file changes.
- Changed/new production executable bodies must have an entry method trace or an explicit recursion exemption.
- `MasterDebugLog` now maintains an async-flow scope chain and records first-chance exceptions against all active scopes.
- Scope completion records `PASS-CHECK`, `ERROR-CHECK`, or `PASS-WITH-ERROR-CHECK` as appropriate; exceptions are never swallowed by this mechanism.
- `Verify-Release.ps1` scans before compilation and only promotes the cache after all required verification stages pass.
- `Build-Release.ps1` delays cache promotion until the complete Windows build/test/self-test/publish path succeeds.
- Added integration regressions for the new fail-closed verification behavior.

## Source delta

Relative to the supplied v8.7 archive, the deliberately modified product C# files are primarily:

- `src/MhwModManager.Core/MasterDebugLog.cs`
- `src/MhwModManager.App/App.xaml.cs` (version/trace message)
- `src/MhwModManager.Diagnostics/AppLogging.cs` (version)
- `src/MhwModManager.Filesystem/NexusMetadataService.cs` (User-Agent version)

Plus verification tests, scripts, solution/project metadata, docs, and the new verifier tool.

## Verification state at handoff

This environment did **not** contain the pinned .NET SDK and network access could not resolve Microsoft's SDK host, so the authoritative .NET 10.0.401 compile/test run could not be executed here. Static checks were performed; see `VERIFICATION.md`.

Therefore `.verification/function-status.json` is intentionally still a bootstrap cache. Do not manually mark the new v8.8 bodies verified. Run the Windows verifier first.

## Re-audit hardening added after the first v8.8 package

- Added mandatory propagating agent continuity protocol, machine-readable handoff manifest, handoff preflight, and source-handoff packager.
- Function verifier excludes generated `src/**/bin/**` and `src/**/obj/**` C# so repeat builds cannot create false new-function failures.
- Trusted v8.7 source zip is now hash-validated against its manifest during every scan; missing/unmanifested/mismatched source fails closed.
- Function IDs now distinguish explicit-interface members to avoid cache collisions.
- Function reports include explicit call-site counts and whether those call sites are covered by a known-good/traced/exempt containing body.
- First-chance exceptions are still observed by every active runtime function scope, but full first-chance stack logging is opt-in with `MHW_FIRST_CHANCE_DETAIL=1`; this avoids mandatory disk I/O for every handled throw.
- `MasterDebugLog` now has a thread-static first-chance recursion guard and writes the total first-chance count at process exit.

These are hardening changes inside the existing 8.8.0 source handoff, not a product-feature redesign.

## Windows verification evidence received after packaging

The user ran `Test Everything.bat` on Windows with .NET SDK 10.0.401 and supplied `_AGENT_CONTEXT/EVIDENCE/v8.8.0-first-windows-verification.log`. That run established real partial evidence instead of only static inspection.

Confirmed PASS on that exact source/input state:
- PowerShell syntax sweep, report serialization preflight, agent-handoff preflight, and restore.
- strict compile/analyzers: Core, Storage, Mhw, Diagnostics, Automation, UnitTests, Benchmarks.
- Core unit tests: **79/79 passed**.

Failures exposed by the run and fixed in the current package:
- FunctionVerifier accepted only `ParameterListSyntax`; indexers use `BracketedParameterListSyntax`. It now accepts `BaseParameterListSyntax`.
- FunctionVerifier nested DTO `FormatVersion` initializers accidentally referenced their own instance property. They now qualify `Program.FormatVersion`.
- `GameProfileEditorWindow` used invalid two-argument WPF `Thickness` constructors and lacked `System.IO` for `Path` / `PathTooLongException`.
- tests/self-test still called the pre-v8.7 `SaveBackupService` constructor and two integration tests passed raw game-root strings into services that now require `GameProfile`.
- strict analyzer CA1859 on private Steam/GOG discovery methods; private discovery methods now return concrete `List<DiscoveredGame>`.

### Checked-state behavior now

`.verification/function-status.json` is a true/false function checklist rewritten on every scan. Safe exact unchanged functions are checked immediately even if later unrelated stages fail. `.verification/stage-status.json` stores independently passing strict build/test stages by exact project/dependency/toolchain fingerprint. The current package pre-checks only evidence whose fingerprint is still identical after the fixes: Core, Storage, Mhw, UnitTests, Benchmarks, and the 79/79 Core test stage. Diagnostics/Automation are intentionally not pre-checked because Filesystem changed.

The source-handoff packager was also corrected to exclude `SOURCE_HANDOFF_MANIFEST.json` from its own file-hash inventory; otherwise it could hash the previous manifest and then overwrite it. The continuity preflight now validates both verification-cache schemas.

## Second authoritative Windows run and verification-closure patch

The user ran the corrected v8.8.0 verifier again on Windows/.NET 10.0.401. Result: **24 passed / 1 failed**. The relaxed whole solution and strict whole solution both compiled with **0 warnings / 0 errors**; Automation tests passed **18/18**; Integration + fault-injection passed **43/43**; the full automation self-test passed every listed check. The sole failure was the function fingerprint scan: 602 functions total, 582 known-good, 20 changed/new, with exactly four trace gaps (`GameProfileEditorWindow.AddField`, `GameProfileRegistry.DiscoverSteam`, `DiscoverEpic`, `DiscoverGog`). Those four gaps accounted for all 69 uncovered explicit call sites.

This packaged revision adds only `MasterDebugLog.BeginMethod()` entry scopes to those four functions. No functional game/deployment behavior was intentionally changed. Since `GameProfileEditorWindow.cs` and `GameProfileRegistry.cs` changed, stage-cache entries whose dependency fingerprints include App/Filesystem are intentionally invalidated on the next run. Exact unaffected green evidence remains cached; `strict:FunctionVerifier` is now also pre-checked from the second run because the verifier project itself is unchanged by this patch.
