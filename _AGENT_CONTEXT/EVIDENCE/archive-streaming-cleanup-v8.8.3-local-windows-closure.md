# v8.8.3 archive streaming failure-cleanup ? local Windows closure

Date: 2026-09-28
Repository: `fengie/mhw-mods`
Branch: `agent/archive-streaming-cleanup-lr011-20260928`
Implementation source checkpoint: `5688fe91c03b56b651a3e9d94d7111b974693ab9`
Exact locally release-verified repository checkpoint: `26485dad2c931544728d108de9da66446dedf0a6`
Host: heaven2 / Windows
.NET SDK: 10.0.401

## Closed behavior

- Archive payload-copy failures attempt best-effort deletion of the currently owned output file for every exceptional exit after creation, including ordinary I/O failures.
- A cleanup failure is secondary diagnostics and does not replace the primary cancellation, output-budget, or I/O exception.
- Smart Inbox re-checks requested cancellation before its recoverable I/O/archive-data handler, preventing a canceled run from skipping the failed item and continuing to later entries.
- Deterministic fault-injection tests cover ordinary I/O cleanup, cleanup-failure preservation of cancellation, cleanup-failure preservation of output-budget failure, and Smart Inbox cancellation dominance.
- LR-008 whole-import catalog-invisible staging/publication remains intentionally separate.

## Verification

Before the repository gate, the xUnit v3 executable runner passed Integration 181/181 and Automation 29/29, and a strict whole-solution build passed with 0 warnings / 0 errors. Two earlier direct `dotnet test` invocations returned zero tests / exit 5 under the local runner setup; those were tooling-invocation failures and are not counted as passing evidence.

`Verify-Release.ps1` on exact checkpoint `26485dad2c931544728d108de9da66446dedf0a6`:

- 25/25 stages passed.
- FunctionVerifier: 738/738 promoted verified.
- Explicit call sites: 7,850; uncovered: 0; trace gaps: 0; parse errors: 0.
- Core: 79/79.
- Automation: 29/29.
- Integration/fault injection: 181/181.
- Automation self-test: 11/11.
- Relaxed and strict whole-solution builds/analyzers: PASS, 0 warnings / 0 errors.
- Agent handoff continuity preflight: PASS.
- Eight adversarial continuity fixtures: all rejected as intended.

`Build-Release.ps1` on the same exact checkpoint:

- Solution build/analyzers: PASS.
- Core 79/79, Automation 29/29, Integration 181/181, self-test 11/11.
- App win-x64 compile/analyzers: PASS.
- win-x64 self-contained ReadyToRun publish: PASS.
- Updater-helper win-x64 self-contained publish: PASS.
- Function fingerprint scan: 738/738 known-good, 7,850/0 uncovered.
- Updater build: 320.
- Artifact: `MHW-Manual-Mod-Manager-v8.8.3-win-x64.zip`.
- SHA-256: `60A11007ABC790B8CBB2EA0353F78961F8D40ED1A2290D865E5192D36EF71433`.

## Evidence boundary

This is exact local Windows release evidence for repository checkpoint `26485dad2c931544728d108de9da66446dedf0a6`. The later commit that persists this evidence and promoted verification caches is metadata/evidence-only and must not be relabeled as independently full-gated. Exact-main hosted Windows verification remains pending until integration.

No new company-level trainer rule was added: LR-011 already captures the reusable cleanup-dominance invariant.

The successor must preserve the permanent continuity constitution and active Learned Rules, and must recursively require its successor to pass them to the agent after them. Do not break the chain.
