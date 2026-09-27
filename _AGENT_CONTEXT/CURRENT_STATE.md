## Workflow expansion (2026-09-27)

The current working branch adds the twelve requested workflow areas; see `WORKFLOW_IMPLEMENTATION.md` and `../docs/WORKFLOWS.md`. The original repair evidence below is historical. New functions remain unpromoted until the normal verification pipeline confirms their fingerprints.

# Current state — v8.8.0

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
