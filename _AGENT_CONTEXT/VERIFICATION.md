## Workflow expansion validation (2026-09-27)

Strict Linux-hosted .NET 10.0.401 Release cross-build: zero warnings/errors. Core 79, Automation 39, Integration 61 all pass (179 total); self-test 11/11. Function scan: 683 total, 562 known-good, 121 pending, zero trace/parse/call-site coverage gaps. No functions were manually promoted. Evidence is in `EVIDENCE/workflow-expansion-validation.log` and `EVIDENCE/workflow-expansion-function-scan.json`. Native WPF acceptance remains required; Windows CI is configured separately.

# Verification performed for this source handoff

## Latest executed checks — 2026-09-27 repair revision

The latest evidence is `EVIDENCE/v8.8.0-repair-validation.log` and
`EVIDENCE/v8.8.0-repair-function-scan.json`. With SDK 10.0.401 / runtime 10.0.12
on Linux x64, the complete strict Release solution build passed (0 warnings,
0 errors); Core 79/79, Automation 18/18, Integration 61/61 passed; all 11
automation self-test checks passed. PowerShell 7.5.3 ran the syntax, continuity,
cache invalidation, and source packaging checks.

Use `dotnet restore/build ... -p:EnableWindowsTargeting=true -m:1` on this host.
Its sandbox blocks named pipes used by `dotnet test` and multi-node MSBuild.
Tests ran via `dotnet <built-test-dll> -noLogo -noColor -maxThreads 2` instead.
This exercised the real xUnit tests without changing their assertions or targets.
Negative verifier fixture diagnostics in the integration log are expected; the
test summary has zero failures. Do not copy this host workaround into Windows
release policy. No Windows UI/locking/publish claim is made.

The scanner reports 602 functions, 579 known-good, 23 changed/new, zero parse/
trace/uncovered-call-site gaps. The six unaffected historical Windows stage
fingerprints were independently recomputed and match. Cache promotion was not run.

The remaining sections below are historical evidence from earlier revisions.

## Completed in the artifact environment

- Compared the working tree against the exact user-supplied v8.7.0 archive.
- Confirmed the trusted bootstrap contains 73 production C# files and every SHA-256 in `trusted-v8.7.0-files.json` matches the corresponding entry in `trusted-v8.7.0-src.zip`.
- Parsed all project/props XML files successfully.
- Parsed all `.verification/*.json` files successfully.
- Confirmed all solution `.csproj` paths exist.
- Ran a C# lexical/delimiter audit over the source/test/tool tree; no unclosed strings/comments or mismatched `{}`, `[]`, `()` were found.
- Audited the v8.8 source delta to keep product behavior changes limited to tracing/version metadata plus the new verification infrastructure.
- Verified the build scripts only promote the cache after their required gates; the release build promotion is after the Windows compile/test/self-test/publish path.


## Post-re-audit hardening (current packaged revision)

Additional checks performed after the continuity/research pass:

- Re-ran the trusted-v8.7 baseline integrity check: all 73 manifested production C# files are present and every SHA-256 matches.
- Parsed every JSON document in the source handoff successfully, including the handoff and function-verification manifests.
- Parsed every project/props/targets/XAML XML document successfully.
- Confirmed every project referenced by the solution resolves to an existing `.csproj`.
- Re-ran a lexical/delimiter audit over 95 C# files under `src`, `tests`, `tools`, and `benchmarks` while excluding generated `bin`/`obj`; no structural mismatch was found.
- Confirmed the handoff manifest has no missing required files, its version matches `VERSION.txt`, and the propagation text is present in both the start-here and continuity-protocol documents.
- Audited the source-handoff packager so generated `bin`/`obj`, logs, release output, IDE state, and repository metadata are excluded while source/context/verification state is retained.
- Hardened the function verifier to ignore generated C# under `bin`/`obj`, validate the trusted source archive against its SHA-256 manifest every scan, reject duplicate stable function IDs, disambiguate explicit-interface members, and report explicit call-site coverage.
- Full first-chance stack logging is now opt-in (`MHW_FIRST_CHANCE_DETAIL=1`); active function scopes still observe every managed throw and aggregate counts remain available.

The PowerShell continuity gate itself could not be executed here because PowerShell is unavailable in the artifact environment. Its manifest/path logic was statically audited, and the authoritative Windows run remains required.

## Not possible in this environment

The authoritative .NET compile/tests were not run. No `dotnet` executable was installed and the environment could not resolve Microsoft's SDK download host. Do not represent this archive as compiler-verified until the Windows verifier is run.

## Required success criteria on Windows

The release should not be considered confirmed until all of these pass:

1. PowerShell syntax/preflight.
2. Solution restore.
3. Function fingerprint scan with zero parse errors and zero required trace gaps.
4. Relaxed whole-solution compile.
5. Strict analyzer compile for every project, including FunctionVerifier and App.
6. Strict whole-solution compile.
7. Core unit tests.
8. Automation unit tests.
9. Integration/fault-injection tests.
10. Full automation self-test.
11. Function-cache promotion.
12. For release build: win-x64 compile and successful self-contained publish (ReadyToRun or the existing safe JIT fallback) before promotion.

## First authoritative Windows run supplied by the user

Evidence file: `_AGENT_CONTEXT/EVIDENCE/v8.8.0-first-windows-verification.log`.

The run used SDK 10.0.401 on Windows. It reached the collect-all verifier and produced **13 passed / 12 failed**. Most failures were compile-contract regressions introduced during the v8.7/v8.8 transition rather than runtime product failures. The successful checks listed in `CURRENT_STATE.md` are preserved granularly in `.verification/stage-status.json` when their exact fingerprints remain unchanged.

The current package fixes every compiler/analyzer diagnostic visible in that log. A new Windows verifier run is still required to discover any next-order diagnostics that were previously masked by these compilation failures. Do not mark the overall release confirmed until that subsequent run passes all required stages.

## Second authoritative Windows run

Evidence file: `_AGENT_CONTEXT/EVIDENCE/v8.8.0-second-windows-verification.log`.

This run produced **24 PASS / 1 FAIL**. Important confirmed results on the pre-closure source state:

- relaxed whole-solution compile: PASS, 0 warnings / 0 errors;
- strict whole-solution compile: PASS, 0 warnings / 0 errors;
- strict builds for Filesystem, Diagnostics, Automation, AutomationTests, IntegrationTests, SelfTest, FunctionVerifier, and App: PASS;
- cached strict checks for Core, Storage, Mhw, UnitTests, Benchmarks: PASS-CACHED;
- Core unit tests: previously cached 79/79 PASS;
- Automation unit tests: 18/18 PASS;
- Integration + fault injection: 43/43 PASS;
- full automation self-test: PASS for all listed checks.

The only failure was the function fingerprint scan. It found 602 functions, 582 known-good and 20 changed/new bodies, with exactly four required entry-trace gaps and 69 uncovered explicit call sites. This packaged revision adds entry traces to precisely those four functions. The next Windows run is expected to rerun stages invalidated by those two edited production files and should be considered authoritative for final function-cache promotion.
