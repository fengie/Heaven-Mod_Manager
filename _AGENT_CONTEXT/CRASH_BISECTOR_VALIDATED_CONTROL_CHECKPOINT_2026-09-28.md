# Crash bisector validated-control checkpoint — 2026-09-28

## Status

Implementation candidate on `agent/crash-bisector-evidence-integrity-20260928`.

Exact implementation commit locally verified: `a7b33431ea274b9be5c27abdacbef632521d4d1d`.

Exact reconciled branch commit locally reverified after merging current main: `2085e059401e935eb4827149560d1044dd7d9840`.

Canonical `main` inspected before work: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.

Final concurrent-main reconciliation before completion: `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`; its intervening changes are archive-audit/provenance/continuity/evidence files and do not modify `CrashBisectorEngine.cs` or `AutomationLogicTests.cs`.

This checkpoint closes only CB-01 from `CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md`: automatic bisection must validate its experimental control and positive condition before narrowing can produce a persistent confirmed culprit.

## Why this was selected

Active parallel lanes own Agent Control v2 (PR #59), updater publication hardening (PR #58), and frontend UX (PR #55). A separate heaven2 worktree also owns profile-save atomicity. None owns crash-diagnosis evidence integrity.

The existing specialized audit identifies a P1 source defect: a currently bad baseline could make every subset appear to reproduce, allowing an arbitrary mod to become a durable score-99 confirmed suspect.

## Scope and exclusions

Changed only:
- `src/MhwModManager.Automation/CrashBisectorEngine.cs`
- `tests/MhwModManager.AutomationTests/AutomationLogicTests.cs`

Deliberately not changed: game-build/payload provenance, process-exit classification, flaky-probe retry policy, priority-change candidate selection, cancellation compensation, interaction minimization, issue-persistence transactions, updater, Agent Control, frontend, or profile persistence.

## Confirmed pre-fix behavior

Regression-first execution used the real xUnit v3 executable:

`dotnet run --project tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj -c Release --no-restore`

Before the production fix, the suite ran 26 tests and failed exactly three new/strengthened crash-bisector assertions:
- failing baseline was still reported as isolated;
- clean full suspect set still returned suspects instead of refusing confirmation;
- the first deterministic-culprit probe was a half-set rather than an empty control.

This runtime characterization confirms CB-01; it is not only a static risk.

## Implementation

`CrashBisectorEngine.RunAsync` now performs, in order:
1. an empty-set baseline/control probe;
2. a full normalized suspect-set probe;
3. existing deterministic half-splitting only if both preconditions are valid.

If the baseline reproduces, the result is non-isolated with no suspects. If the full set does not reproduce, the result is non-isolated with no suspects. Therefore the existing caller cannot reach `MarkBisectResultAsync` from either invalid experiment.

The change also fixes the one-suspect case: one suspect is no longer returned as isolated without any probe.

## Regression coverage

`CrashBisectorRejectsFailingBaselineBeforeNarrowing` proves the first and only probe is the empty control and that no suspect is returned.

`CrashBisectorRejectsCleanFullSetBeforeNarrowing` proves the second probe is the complete suspect set and that non-reproduction yields no suspect.

`CrashBisectorIsolatesSingleCulprit` now additionally proves control then full-set preflight precede narrowing.

## Verification actually performed

On heaven2 / Windows / .NET SDK 10.0.401:
- pre-fix regression characterization: 26 total, 3 expected failures;
- post-fix AutomationTests via xUnit v3 executable: 26/26 PASS;
- `dotnet restore MhwModManager.sln --nologo`: PASS;
- `dotnet build MhwModManager.sln -c Release --no-restore --nologo`: PASS, 0 warnings / 0 errors;
- `scripts/Verify-Release.ps1`: **25/25 PASS**;
- FunctionVerifier: **728/728 promoted**, **7780 explicit call sites / 0 uncovered**, 0 trace gaps, 0 parse errors;
- AutomationTests: **26/26 PASS** inside the verifier;
- Integration + fault injection: **177/177 PASS**;
- automation self-test: **11/11 PASS**;
- strict whole-solution compile/analyzers: PASS, 0 warnings / 0 errors;
- handoff continuity preflight + negative fixtures: PASS;
- `git diff --check`: PASS before verifier-generated evidence files.

An earlier whole-solution `--no-restore` build in the fresh worktree failed only because several projects had no `project.assets.json`; after the required restore, the same build succeeded. `dotnet test` is not the authoritative invocation for these xUnit v3 executable projects and reported zero tests; `dotnet run --project ...` executed the real 26-test suite. Verification-cache promotion above was performed only by the normal repository verifier.

## Residual findings not closed here

CB-02: stored last-known-good game-build provenance is not validated by the bisector.

CB-03: stored state does not prove exact historical mod payload identity.

CB-04: any process exit within 12 seconds is still treated as crash reproduction.

CB-05: one observation per subset can still become permanent high-confidence blame.

CB-06: initial UI candidate selection still omits priority-only changes when newly enabled suspects exist.

CB-07/CB-08 remain cancellation-compensation and interaction-minimization follow-ups.

These are deliberately separate checkpoints. In particular, this change does not claim that the boolean probe itself is a trustworthy crash classifier; it only prevents narrowing when its own control or positive precondition is invalid.

## Learned-rule / trainer review

No new Learned Rule is added. Active LR-009 already states the generalized invariant: automated diagnosis must validate its control before persisting blame. Company verification doctrine already requires meaningful control/positive evidence and regression-first escaped-bug handling.

## Not yet verified

At this checkpoint, no real Monster Hunter: World process/crash was used, no hosted GitHub Actions Windows Release Gate has been run for this branch, and no release artifact is claimed.

Local Windows repository verification is green; hosted integration evidence still belongs to the eventual integration checkpoint.

## Recommended next independent checkpoint

After this candidate is integrated, address CB-04/CB-05 together as one probe-evidence boundary: replace the raw bool/early-exit assumption with an explicit reproduced/survived/inconclusive outcome and prove noisy or ambiguous observations cannot promote confirmed score-99 blame.

Do not couple that work to game-build freshness, updater, Agent Control, frontend, profile persistence, or filesystem safety.

## Parallel-agent integration notes

Re-check canonical `main` and open PRs before integration. If another branch changes CrashBisectorEngine, AutomationLogicTests, or the caller contract first, re-evaluate the invariant rather than replaying this diff blindly.

The active updater, Agent Control, frontend, and profile-save work were not modified or reset.

## Successor handoff

The successor inherits the permanent continuity constitution. Re-read canonical `main`, active PRs, `CONTINUITY_PROTOCOL.md`, active Learned Rules, and this checkpoint before editing. Preserve the exact-evidence rule and recursively require the next agent to inherit, preserve, and propagate the same continuity system to the agent after them.

**Do not break the chain.**
