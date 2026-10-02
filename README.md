# v8.8.66 — MHW Manual Mod Manager

Current product version: **8.8.66**.

## Recent patches

Keep this section intentionally short. The README shows the **current patch plus the two immediately preceding patches only**; complete history belongs in [`CHANGELOG.md`](CHANGELOG.md).

## v8.8.66 — provider-aware Browse Mods search

- Make the **Search** button query only configured catalog providers that explicitly advertise the `Search` capability, then surface the matching provider data through the existing source-aware cache/FTS path.
- Keep live provider search off the per-keystroke debounce path: typing still filters cached data locally, avoiding request storms and unnecessary provider traffic.
- Do not probe Nexus Mods or GameBanana for unsupported full-catalog text search; their existing browse contracts remain unchanged.
- Preserve per-provider failure isolation so a remote-search failure still leaves cached results usable, with clear coverage/status text in Browse Mods.
- Add a focused regression guard for capability gating, cache-only debounce behavior, and the user-facing cached-versus-provider search contract.
- This is the next #558 tranche; broader provider-aware pagination/browse expansion remains open after this patch.

## v8.8.65 — selected game text follow-up

- Fix the custom dark ComboBox's **closed selected-value presenter**, which could still call `GameProfile.ToString()` even though dropdown rows correctly showed `DisplayName`.
- Make the selected surface reuse the same `ItemTemplate`, template selector, and string-format contract as the dropdown, so the header consistently shows names such as **Monster Hunter: World**.
- Add regression coverage that rejects the broken `SelectionBoxItemTemplate` path and keep issue #556 open until installed Windows/WPF confirmation.
- Installed-client acceptance must use the published v8.8.65 updater package; source-only `main` is not treated as deployed UI proof.

## v8.8.64 — richer Browse Mods + selector repair

- Fix the header game selector so the app-owned dark ComboBox renders the selected game's human-readable name instead of the raw `GameProfile` record.
- Upgrade **Browse Mods** to show safe HTTPS thumbnails, summaries, author/category/download metadata, readable provider names, version, updated date, and cache freshness.
- Add selected-mod artwork plus version/update context while preserving exact-file selection and the existing safe install/provider-page flow.
- Enable recycled row virtualization and raise the initial provider refresh cap from 60 to 100 items plus the cached visible-result cap from 250 to 1000.
- Add focused XAML/source regressions; deeper provider-aware pagination/search and discovery controls remain tracked in #558/#559.

## Current plans & progress

Canonical ledger: [`_AGENT_CONTEXT/PROJECT_PLAN.md`](_AGENT_CONTEXT/PROJECT_PLAN.md)

- [x] **TOOLBOX-CUTOVER / P0** — global training/toolbox ownership, routing, verifier relocation, compatibility-copy deletion, no-reintroduction enforcement, context takeover, and live relay cutover are complete.
- [x] **SECURITY-554 / P0** — v8.8.63 crawler path/redirect containment passed all required exact-head gates and integrated via PR #555.
- [ ] **BROWSE-556 / P1** — v8.8.65 source fix passed all three exact-head gates and merged via PR #562; installed Windows/WPF visual confirmation remains before closing #556.
- [x] **BROWSE-557 / P1** — v8.8.64 rich Browse Mods rows/details passed all required exact-head gates and integrated via PR #561.
- [ ] **CATALOG-SCALE-558 / P1** — v8.8.66 candidate adds capability-gated remote text search without probing unsupported providers; broader provider-aware pagination/browse expansion remains open under #558.
- [ ] **BROWSE-UX-559 / P1** — filters, sorting, provider-health presentation, and broader discovery UX remain open under #559.
- [x] **RECOVERY-002 / P0** — v8.8.62 catalog browser/acquisition, CurseForge, and permitted-crawler recovery is integrated via PR #553 with exact-head gates green; conditional Steam Workshop / optional Vortex remain tracked under issue #281.
- [x] **RECOVERY-004 / P0** — v8.8.60 runtime/updater hardening is integrated via PR #552 with exact-head required gates green.
- [ ] **RECOVERY-007 / P0** — universal installed-game discovery is integrated, but representative Windows/runtime discovery proof remains before DONE.
- [x] **RECOVERY-003 / P1** — v8.8.59 persistent Settings/manual-update preference lane is integrated with exact-head gates green.
- [ ] **RECOVERY-005 / P1** — v8.8.58 dark ComboBox source/tests are integrated; installed Windows/WPF visual/interaction acceptance remains before DONE.

## One-file diagnostics

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

## Full compiler/analyzer sweep

Run `Test Everything.bat` (recommended) or `scripts\release\Verify-Release.ps1`. v8.6.7 first syntax-checks all active PowerShell scripts, then performs a relaxed whole-solution build so analyzer warnings do not block downstream projects, then compiles every project independently with warnings-as-errors and continues through the entire project list. This exposes all *reachable* compiler/analyzer failures in one run instead of stopping at the first failing dependency.

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

## Conflict semantics remain conservative

Enabled mod state and winning file provider are separate concepts. Several enabled mods may coexist while one wins a particular overlapping path.

The manager can automatically resolve byte-identical files, shared textures/resources, remembered overlays, high-confidence component/patch relationships, dedicated texture providers, and newer related texture revisions. Unknown structural/model/material/physics/plugin/game-data collisions fail closed until there is an explicit rule.

Different bytes are never described as merged unless an actual file-format merger exists.

## Build and verify on Windows

Use the current .NET 10 SDK (the repository pins SDK `10.0.401` in `global.json`). Then run:

```powershell
.\scripts\release\Verify-Release.ps1 -RunBenchmarks
```

If that passes, build the self-contained Windows x64 release:

```powershell
.\scripts\build\Build-Release.ps1
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
.\scripts\diagnostics\Capture-Diagnostics.ps1 -ProcessId <PID>
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

## Automation workflow

The default workflow is now intended to be almost hands-free. Drop archives/folders into `Inbox\` or keep using `Mods\`; startup maintenance imports safe inbox content, learns lineage, assigns categories, and archives safe disabled duplicates/superseded revisions. The main action is **JUST PLAY**: it applies staged changes, adopts trackable loose `nativePC` files, snapshots the MHW save and manager state, runs the health/dependency/conflict gate, launches the game, and records a Last Known Good setup when startup survives.

If startup begins failing after newly enabling mods, **Auto-diagnose startup crash** performs a binary-search style bisect against Last Known Good and restores the original setup after diagnosis. Genuine unresolved structural alternatives or missing dependencies still stop rather than being guessed.

For complete verification, double-click `Test Everything.bat` (or `Verify.bat`). The verifier continues through all reachable compile/analyzer/test groups even when one fails and writes `BuildLogs\verification-report-*.md` plus a machine-readable `.json` report. See `docs\AUTOMATION-AND-TESTING.md` for details.

### Build diagnostics
`Build.bat` now writes stage-specific logs under `BuildLogs`. The release builder performs a RID-specific ReadyToRun restore before publish. If only the SDK ReadyToRun optimization phase fails, it records the failure and retries a self-contained JIT publish; application correctness/tests are unchanged.

### Publish analyzer preflight

Release builds now compile the exact `win-x64` App target with warnings-as-errors before publish. Startup database initialization also forwards the diagnostic cancellation token explicitly, resolving CA2016 in RID-specific publish builds.

### Build harness exit-code preservation

The release builder now consumes `Tee-Object` output with `Out-Host` and returns only the native `dotnet` exit code from each stage. This prevents successful stages from being misread as failures when their console text is captured alongside exit code `0`. A harness contract check fails immediately if any stage ever returns a non-scalar/non-integer result.

## Generic mod-family inference

Family detection is evidence-based and is not tied to HPN or any other author/series. The engine prefers explicit/manual family IDs, imported manager metadata such as `logicalFileName`, and shared Nexus source identity. When those are unavailable it combines semantic name lineage with concrete file evidence: subset/overlap ratios, shared content roots, resource namespaces, and MHW armor/model identity.

Names alone do not collapse packages. Two similarly named mods with disjoint layouts stay separate, and two unrelated replacements of the same armor model stay separate unless there is additional lineage evidence. Nexus Main/Optional/Misc labels are treated as descriptive metadata rather than structural truth.

Accepted heuristic pairings are written to `MHW-DEBUG-ALL.log` as `[FAMILY] GENERIC ... score=... evidence=...` so every automatic grouping can be audited.

## Git-first agent continuity

Global programming-agent bootstrap, shared Git policy, personal plugins, Heaven Bridge, Agent Control, and reusable operator/developer tools are owned by `fengie/heaven-toolbox@main`. For MHW work, refresh and read current Toolbox first, then refresh `fengie/mhw-mods@main` and load this repository's product-specific `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `_AGENT_CONTEXT/`, source, tests, release state, and evidence.

Do not create new reusable plugin/control-plane/tooling implementations under MHW-local `plugins/`, `heaven-bridge/`, `tools/`, or `_AGENT_TRAINING/`; those ownership roots intentionally no longer exist here. Reusable infrastructure changes belong in Heaven Toolbox. MHW keeps only mod-manager product code, tests, product-specific scripts/workflows, and continuity/evidence needed to operate the product.

Every shipped application change must bump the app version in `VERSION.txt` and `Directory.Build.props`, keep duplicated release/update metadata aligned, update this README, and add the matching `CHANGELOG.md` entry before the work is considered complete. Documentation/agent-policy/evidence-only changes that do not change the shipped application do not require an app-version bump. Run the current MHW product verification gates before declaring work complete.
