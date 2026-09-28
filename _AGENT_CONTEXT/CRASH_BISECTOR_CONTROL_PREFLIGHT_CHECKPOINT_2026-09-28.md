# Crash-bisector control-preflight checkpoint — 2026-09-28

## Status

**Implemented and locally verified on an isolated support branch. Not integrated into canonical main. No hosted Windows Release Gate is claimed.**

- canonical `main` inspected before branch creation: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`
- support branch: `agent/support-crash-bisector-control-preflight-20260928`
- exact production/test code commit verified locally: `de43c5fbcd278da4884a02b9cb9ff2286e1f2aa9`
- parent audit: `_AGENT_CONTEXT/CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md`
- active durable rule already covering this failure class: **LR-009 — automated diagnosis must validate its control before persisting blame**

This checkpoint deliberately closes only the first high-severity experiment-validity defect from the parent audit: **CB-01**.

## Why this was selected

At task start, canonical open PR ownership was:

- PR #58 — updater publication window;
- PR #59 — Agent Control v2;
- PR #55 — frontend/UI/UX;
- PR #61 — archive-streaming verification provenance.

Recent support branches also already owned updater, archive, reparse, migration-hardlink, support-bundle, multi-instance, save-snapshot, remote-preview, and control-plane audits.

Crash-bisector evidence integrity had a previously documented P1 source defect but no active implementation PR. The defect sits in `MhwModManager.Automation`, allowing a narrow repair without touching the frontend-owned presentation lane or updater/control-plane boundaries.

## Exact assignment

Scope:

1. inspect current `CrashBisectorEngine.RunAsync` and the caller persistence gate;
2. prove whether an invalid control can still reach a durable confirmed culprit;
3. add regression-first protection for the two experimental preconditions:
   - empty/current control must **not** reproduce;
   - full current suspect set **must** reproduce;
4. preserve the existing public callback/result API;
5. preserve cancellation semantics;
6. leave CB-02 through CB-08 outside this checkpoint;
7. verify on Windows/heaven2 with focused and affected tests plus strict solution compilation;
8. preserve a durable handoff for a fresh successor.

## Confirmed pre-change behavior

Before this branch, `CrashBisectorEngine.RunAsync` immediately split the suspect set and treated any reproducing half as causal evidence.

That allowed the documented CB-01 false-confirmation sequence:

- the reconstructed baseline is currently bad for a reason outside the suspect set;
- every probed half therefore appears to reproduce;
- halving reaches one arbitrary suspect;
- the engine returns `Isolated=true`;
- `MainWindowViewModel.AutoDiagnoseCrash` calls `MarkBisectResultAsync` whenever `result.Isolated` is true;
- the issue service persists score-99 / confirmed culprit state.

The caller was inspected on current source and still gates persistence solely on `result.Isolated`, so fixing the engine result semantics is sufficient to block this exact false-confirmation path without changing MainWindow/UI code.

## Implementation

Changed `src/MhwModManager.Automation/CrashBisectorEngine.cs`:

1. retain the existing empty-suspect early return;
2. normalize/deduplicate suspects as before;
3. honor an already-canceled token before each preflight;
4. probe an empty `IReadOnlySet<string>` control:
   - if it reproduces, return `Isolated=false`;
   - do not enter narrowing;
5. probe the complete normalized suspect set:
   - if it does not reproduce, return `Isolated=false`;
   - do not enter narrowing;
6. only after both preconditions are valid, execute the previous deterministic half-split algorithm;
7. preserve the existing public method signature and `CrashBisectResult` shape.

This intentionally avoids a new enum/result contract in this first checkpoint. A richer `ReproducedFailure / SurvivedObservation / Inconclusive` probe model remains a separate CB-04/CB-05 boundary.

## Regression coverage

Changed `tests/MhwModManager.AutomationTests/AutomationLogicTests.cs`.

Existing tests continue to cover:

- deterministic single culprit isolation;
- interaction result when neither half independently reproduces.

New tests:

### `CrashBisectorRejectsBadControlBeforeNarrowing`

The probe returns true for every state.

Assertions prove:

- `Isolated == false`;
- all four suspects remain unaccused;
- exactly one probe ran;
- the one probe was the empty control;
- narrowing never started.

### `CrashBisectorRejectsCleanFullSetBeforeNarrowing`

The probe returns false for every state.

Assertions prove:

- `Isolated == false`;
- all four suspects remain unaccused;
- exactly two probes ran;
- probe 1 was the empty control;
- probe 2 was the complete suspect set;
- narrowing never started.

## Verification actually performed

Environment:

- machine: **heaven2**
- OS: Windows
- .NET SDK selected from repository `global.json`: **10.0.401**
- isolated worktree:
  `C:\Users\fengc\Documents\Codex\2026-09-28\support-crash-bisector-control-preflight\work`
- exact code/test commit for the final verification below:
  `de43c5fbcd278da4884a02b9cb9ff2286e1f2aa9`

### Focused test

From repository root:

`dotnet test .\tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj --filter CrashBisector --no-restore`

Result:

- **4/4 PASS**
- 0 failed
- 0 skipped

### Affected project

`dotnet test .\tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj -c Release --no-restore`

Result:

- **26/26 PASS**
- 0 failed
- 0 skipped

### Strict solution compile

`dotnet build .\MhwModManager.sln -c Release -warnaserror --no-restore`

Result:

- **Build succeeded**
- **0 warnings**
- **0 errors**

### Failed setup attempt preserved honestly

The first fresh-worktree test command used `--no-restore` before NuGet assets existed and failed with NETSDK1004. A second command run outside the repository root also selected the legacy VSTest path instead of the repository's Microsoft.Testing.Platform configuration. Running from the repository root (so `global.json` is authoritative) resolved that invocation issue. Neither setup failure is a product/test failure.

## What this checkpoint does not claim

Not run / not claimed:

- repository-wide `Verify-Release.ps1`;
- FunctionVerifier promotion or verification-cache update;
- Core or Integration test suites;
- ReadyToRun publish;
- `Build-Release.ps1`;
- hosted Windows Release Gate;
- real Monster Hunter: World crash reproduction;
- immutable release/publication evidence.

No verification cache was manually promoted.

## Remaining diagnosis-evidence risks

This checkpoint closes **CB-01 only**.

Still open from the specialized parent audit:

- **CB-02 (P1):** stored last-known-good game-build provenance is not validated before diagnosis;
- **CB-03 (P2):** last-known-good state lacks exact mod-payload identity;
- **CB-04 (P2):** any process exit inside the observation window is treated as reproduced crash;
- **CB-05 (P2):** one noisy observation can still steer narrowing once preconditions pass;
- **CB-06 (P2):** automatic candidate selection can omit priority-changed suspects when newly enabled suspects exist;
- **CB-07 (P3/overlap):** compensation uses the operation token;
- **CB-08:** interaction sets are not minimized.

Do not bundle these into one follow-up patch.

## Existing strengths preserved

- suspect normalization remains deterministic and case-insensitive;
- interaction results remain fail-closed (`Isolated=false`);
- the public engine API remains unchanged;
- MainWindow/UI contracts are untouched;
- issue persistence is untouched;
- deployment/planner/recovery behavior is untouched;
- updater, control-plane, archive, filesystem, database, migration, networking, backup, and UI work are untouched;
- LR-001 instrumentation remains present in the changed production method;
- LR-009 is enforced more concretely and does not need a duplicate Learned Rule.

## Parallel-agent integration notes

At branch creation, canonical main was `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.

This branch intentionally does **not** edit the high-contention global handoff files currently being touched by the updater/archive/control-plane support lanes. Integrators should preserve newer canonical continuity snapshots and merge this specialized checkpoint + source/tests without replaying stale global state.

If `CrashBisectorEngine`, `AutomationLogicTests`, or the caller persistence contract changes before integration, re-run this invariant against the new bodies rather than mechanically resolving text conflicts.

## Recommended next independent checkpoint

After this branch is integrated and exact-main verification is available, take **one** diagnosis-evidence boundary next.

The strongest next candidate is CB-04/CB-05 together only if treated as one probe-evidence contract: replace the boolean process probe with explicit reproduced/survived/inconclusive evidence and prove noisy or ambiguous observations cannot become confirmed blame.

Keep CB-02 game-build freshness coordinated with the canonical launch-health policy rather than creating a competing build monitor.

## Successor handoff

A fresh successor must:

1. re-read actual canonical `main`, open PRs, and current branches before editing;
2. read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, this checkpoint, and the parent crash-bisector audit;
3. preserve the validated-control invariant introduced here;
4. report verification only for exact inputs actually executed;
5. preserve the permanent recursive continuity constitution and active Learned Rules;
6. explicitly require their successor to inherit, preserve, and recursively propagate the same system to the agent after them.

**Do not break the chain.**
