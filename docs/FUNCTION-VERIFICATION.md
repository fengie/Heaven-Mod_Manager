# Function verification and call-error checks — v8.8.0

v8.8.0 adds an incremental source-verification layer without weakening the existing full solution build, analyzers, unit tests, integration/fault-injection tests, or automation self-test.

## Goals

1. Inventory every explicit production method, constructor, operator/conversion, local function, explicit accessor body, and expression-bodied property/indexer in `src`.
2. Give each function a stable syntax fingerprint and a boolean `verified` state.
3. Reuse `verified=true` only when the function fingerprint is unchanged.
4. Require changed/new production functions to enter a `MasterDebugLog.BeginMethod()` scope.
5. Observe exceptions raised anywhere under an active scope without swallowing or translating them.
6. Persist the current per-function boolean checklist on every scan without granting trust to changed/new bodies.
7. Promote changed/new fingerprints to `verified=true` only after the complete required verification pipeline passes.
8. Persist independently passing strict-build/test stages by exact input fingerprint so unchanged successful checks do not need to be repeated.

## Per-function cache

The durable checklist is `.verification/function-status.json`. Each current entry contains:

- stable function ID (relative source path + containing type + callable signature);
- syntax fingerprint (SHA-256 over the trivia-free Roslyn token stream);
- `verified: true/false`;
- verification timestamp when checked;
- verification basis.

At scan time the verifier compares the current fingerprint with the last checklist entry and the trusted v8.7 bootstrap. The scan then rewrites the checklist for the current source tree.

- exact same ID + fingerprint + `verified=true` => **known good / skip semantic re-verification**;
- changed fingerprint, new signature, or new function => **needs verification**.

Whitespace/comments do not invalidate a function because the fingerprint removes trivia before hashing. Functional syntax changes do.

## v8.7 bootstrap

v8.8.0 is the first release with exact per-function promotion, so two read-only bootstrap artifacts are included:

- `.verification/trusted-v8.7.0-files.json` — SHA-256 for every v8.7 production `.cs` file;
- `.verification/trusted-v8.7.0-src.zip` — the exact production C# source from the user-supplied v8.7.0 previous-release archive.

If an entire file is unchanged, its functions are immediately known-good. If a file changed, the verifier parses the trusted v8.7 copy and compares functions individually, so unrelated unchanged functions in that file remain known-good. After the first scan, `.verification/function-status.json` already becomes a complete true/false function checklist. A complete successful verification upgrades every exact current function fingerprint to full-release confirmation.

These bootstrap artifacts are verification evidence only. The running mod manager never reads them.

## Runtime call-error observation

`MasterDebugLog.BeginMethod()` creates a nested async-flow-aware scope. v8.8.0 keeps a scope chain with `AsyncLocal` and subscribes to `AppDomain.FirstChanceException` through the existing global exception hook.

When an exception is thrown, every still-active scope in that logical call chain records that an exception was observed. The exception continues normally; the logger never catches it on behalf of product code and never converts failure into success.

Every scope reports its observed exception count. A scope without an explicit outcome writes:

- `PASS-CHECK ... observedExceptions=0` when no nested call threw;
- `ERROR-CHECK ... observedExceptions=N` when one or more exceptions were observed.

A scope completed explicitly with `Success()` writes `PASS` when clean or `PASS-WITH-ERROR-CHECK` when a nested call threw and was subsequently handled. Explicit failures include the same observed-exception metadata.

`ERROR-CHECK` means an error occurred somewhere while the function was active. It does **not** automatically mean the operation failed: a lower layer may intentionally catch and recover from that exception. The normal explicit `PASS`/`FAIL`, unit tests, integration tests and operation journal remain authoritative for operation success.

## Changed/new function gate

The 2026-09-27 repair requires a direct `using var scope = MasterDebugLog.BeginMethod()`
as the first statement, or a single `using (MasterDebugLog.BeginMethod()) { ... }`
enclosing the entire body. Conditional/lambda calls, plain calls without using,
delayed scopes, and short using blocks followed by uncovered work are rejected.
This is a syntax gate; the compiler/tests still provide semantic verification.

Parse errors and duplicate function IDs leave the previous checklist untouched;
ordinary trace gaps still persist as unchecked entries. A manifested trusted ZIP
must exist, and duplicate source entries fail closed.

The function verifier uses Roslyn from the pinned .NET SDK itself; it adds no NuGet dependency. Any changed/new explicit production executable body covered by the inventory must have `MasterDebugLog.BeginMethod()` at entry. The tracing implementation inside `MasterDebugLog` is the only intentional recursion exemption.

Legacy v8.7 functions that are byte/syntax-identical and already trusted are not rewritten merely to add instrumentation; this honors the incremental rule that unchanged known-good code does not need to be disturbed or rechecked. As soon as one of those functions is edited, it becomes unverified and the entry-trace requirement applies.

Explicit accessor bodies are inventoried independently. Auto-properties/generated accessors have no source body to verify. Lambdas and compiler-generated bodies are covered by the fingerprint of their containing explicit executable body rather than receiving unstable anonymous IDs.

## Verification pipeline

`Verify-Release.ps1` now performs, in order:

1. PowerShell syntax and report-harness preflight;
2. solution restore;
3. function fingerprint scan;
4. relaxed whole-solution build;
5. strict project-by-project builds/analyzers;
6. strict whole-solution build;
7. core unit tests;
8. automation unit tests;
9. Windows integration/fault-injection tests;
10. full automation self-test;
11. **only if every earlier required stage passed:** promote all exact current function fingerprints.

The function scan itself always preserves safe per-function checks: unchanged trusted/exact-cache functions remain `verified=true`, while changed/new bodies remain `verified=false` on a failed run. Independently passing strict project builds and tests are stored in `.verification/stage-status.json`; `Verify-Release.ps1` prints `PASS-CACHED` and skips them on later runs only when the full project/dependency/toolchain fingerprint is identical. Whole-solution compile and production release gates are never skipped from this cache.

`Build-Release.ps1` applies the same scan-before-build rule and delays promotion until the complete Windows compile/test/self-test and publish path has succeeded.

## Reports

Every verifier run writes a JSON function report in `BuildLogs` containing each function's:

- ID and source line;
- current fingerprint;
- `knownGood` boolean;
- verification basis (`exact-function-cache`, trusted v8.7 file/function, or changed/new);
- whether runtime trace is required/present;
- invocation/object-creation count for audit context.

This report is intended to be handed to future coding agents together with the source archive.

## Re-audit hardening

The scanner excludes `bin` and `obj` source paths so generated SDK/WPF C# cannot be mistaken for authored production functions on repeat runs.

The trusted v8.7 source zip is validated against `trusted-v8.7.0-files.json` on every scan. A missing manifested file, an unmanifested trusted source file, or a SHA-256 mismatch fails the verifier before trust can be reused.

Function IDs include explicit-interface qualification for source members that support it, avoiding aliasing between members such as `IFoo.Run()` and `IBar.Run()`.

Reports also contain `totalExplicitCallSiteCount`, `coveredExplicitCallSiteCount`, and `uncoveredExplicitCallSiteCount`. Explicit invocation/object-creation/implicit-object-creation/constructor-initializer sites are considered covered when their containing source body is exact-known-good, has the required `BeginMethod()` entry scope, or is the narrow tracer recursion exemption. Any uncovered explicit call sites fail the scan.

### First-chance detail mode

Scope-level exception observation remains always on after global hooks are installed. Full `FIRST-CHANCE` stack records are opt-in because handled exceptions also produce first-chance notifications. Set `MHW_FIRST_CHANCE_DETAIL=1` (or `true`/`yes`/`on`) for deep per-throw logging. Process exit writes the aggregate first-chance count.

## Granular checked-stage cache

The repair also fingerprints common build targets/editor/NuGet configuration when
present. Integration stages include source/UI files, verification tooling, scripts,
and the handoff documents their tests read, even without project-reference edges.
`scripts/Test-VerificationCache.ps1` verifies these invalidations and runs in both
release entrypoints. Generated `bin`/`obj` files remain excluded.

The user explicitly requested that checks which already passed remain checked. `stage-status.json` implements this without weakening release correctness. Each entry is keyed by stage ID and a SHA-256 fingerprint covering the project tree, transitive `ProjectReference` inputs, common build props/package props/global.json, exact dotnet SDK, OS, and process architecture. A cache hit is valid only for an exact fingerprint match.

The first Windows v8.8 run proved Core, Storage, MHW, UnitTests, Benchmarks, and 79/79 Core unit tests on exact inputs that remained unchanged after the follow-up fixes, so those checks are pre-seeded as `verified=true`. Diagnostics and Automation were **not** carried forward because their transitive Filesystem dependency changed during the fixes.
