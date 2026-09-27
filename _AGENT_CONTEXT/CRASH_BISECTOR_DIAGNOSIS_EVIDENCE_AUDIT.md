# Crash-bisector and diagnosis evidence-integrity audit — 2026-09-27

## Status

**Static support audit. No production behavior changed.**

Canonical audit base when the branch was created:

`5619604e88a27176726ada8518f53d385abc7b0f`

Support branch:

`agent/support-crash-bisect-diagnosis-audit-20260927`

The available repository surface was the connected GitHub repository. A local working tree was not available after Work-mode handoff was declined, so no local `git status`, `dotnet test`, or Windows runtime reproduction is claimed.

---

## Why this lane is independent

Current parallel work already owns:

- Windows filesystem containment / native replacement;
- async lifetime / shutdown;
- migration convergence;
- diagnostics privacy;
- remote-preview network trust;
- backup / portability;
- multi-source acquisition and Smart Pack planning;
- MHW semantic coverage;
- launch-health / general game-build revalidation.

The broad `TEST_GAP_AND_PERFORMANCE_AUDIT.md` already notes that crash diagnosis lacks a noisy/flaky probe policy. This audit does not duplicate that broad map. It specializes the **evidence chain that turns temporary crash probes into a persistent 99-score confirmed mod accusation**.

PR #14 remains the specialized authority for the general build-revalidation / Just Play state machine. This audit only records the narrower fact that automatic crash bisection loads a stored last-known-good build fingerprint but does not validate it before using that state as an experimental baseline.

---

## Internal assignment

Inspect the crash diagnosis path end to end and answer:

1. What conditions are proven before narrowing suspects?
2. What exactly counts as “the crash reproduced”?
3. Can a stale or currently-bad baseline cause an innocent mod to be confirmed?
4. Does the candidate set match the evidence produced by issue fallback?
5. What provenance from the last-known-good state is actually enforced?
6. What tests exist for nondeterminism, baseline validity, and persistent confirmation?
7. What is the smallest independent checkpoint that can make confirmed bisection evidence trustworthy?

Primary source bodies inspected:

- `src/MhwModManager.Automation/CrashBisectorEngine.cs`
- `src/MhwModManager.Automation/LastKnownGoodService.cs`
- `src/MhwModManager.Automation/ModIssueFallbackService.cs`
- `src/MhwModManager.Automation/AutomationCoordinator.cs`
- `src/MhwModManager.Automation/AutomationModels.cs`
- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs`
- `tests/MhwModManager.AutomationTests/AutomationLogicTests.cs`
- `tests/MhwModManager.AutomationTests/AutomationServiceTests.cs`

Related durable audits checked for overlap:

- `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md`
- `_AGENT_CONTEXT/ASYNC_LIFETIME_CANCELLATION_AUDIT.md`
- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md`
- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`
- active PR #14 `LAUNCH_HEALTH_REVALIDATION_AUDIT.md`

---

# Existing strengths

Several parts of this path are already conservative:

1. `CrashBisectorEngine` deduplicates suspects case-insensitively and imposes deterministic ordering.
2. Probe deployment goes back through the real planner and `DeploymentExecutor`; a blocking structural conflict aborts instead of guessing.
3. The automatic bisect UI operation is currently launched through `RunBusy(..., cancellable: false, ...)`, so ordinary user cancellation is not allowed in the middle of the temporary deployment experiment.
4. `AutoDiagnoseCrash` uses a `finally` block to attempt restoration of the user's pre-diagnosis enabled/priority state.
5. A probe that survives 12 seconds is terminated before the next probe, preventing intentional surviving diagnostic instances from accumulating.
6. A confirmed bisect result is persisted distinctly as `ModIssueKind.BisectIsolated`, score 99, `confirmed=true`.
7. Normal successful launch cleanup deliberately leaves confirmed bisect marks active; the current test `BisectIssueMarkRemainsConfirmedAcrossNormalStartupSuccess` pins that policy.
8. Existing unit tests cover a simple single culprit and a two-mod interaction where neither half reproduces independently.

Those strengths make the remaining evidence-validity gaps more important: once the system says “isolated,” that result is intentionally durable.

---

# Finding CB-01 — P1: narrowing begins without validating the experiment

## Classification

**Confirmed source logic gap. Runtime false-positive not reproduced in this audit.**

`CrashBisectorEngine.RunAsync` immediately starts halving the supplied suspect list. Neither the engine nor `AutoDiagnoseCrash` first proves both required experimental preconditions:

1. the current baseline **does not** reproduce the failure; and
2. the full current suspect set **does** reproduce the failure.

The algorithm therefore assumes the last-known-good state is still good and assumes the historical/current failure is still reproducible.

## Concrete false-confirmation sequence

A particularly dangerous sequence is possible from the current logic:

1. stored last-known-good state exists;
2. something outside the candidate mod set now makes that baseline exit during the 12-second probe window;
3. automatic bisect starts with suspects `[a,b,c,d]`;
4. left half `[a,b]` “reproduces” because the baseline itself is currently bad;
5. left quarter `[a]` “reproduces” for the same reason;
6. engine returns `Isolated=true`, suspect `a`;
7. `ModIssueFallbackService.MarkBisectResultAsync` persists `a` as score **99**, `confirmed=true`, severity **Blocker**.

The mod can therefore become a durable confirmed suspect without causal evidence that enabling it changed the result.

## Why P1

The result is not merely advisory. Confirmed bisection is intentionally sticky across later successful launches and is surfaced as “ISOLATED” in the Mods/Issues UI. That is a high-confidence diagnostic state created from an experiment whose controls were never validated.

## Required regression

Before any narrowing probe:

- run a baseline/control probe with zero candidate suspects enabled;
- if it reproduces, stop as **baseline-invalid**, persist no confirmed culprit;
- run the full-candidate probe;
- if it does not reproduce, stop as **not-currently-reproducible**, persist no confirmed culprit;
- only then enter narrowing.

Tests must assert that neither invalid precondition can reach `MarkBisectResultAsync`.

---

# Finding CB-02 — P1: stored last-known-good game-build provenance is ignored by the bisector

## Classification

**Confirmed source data-flow gap; overlaps PR #14 only at the general game-build policy boundary.**

`LastKnownGoodService.RecordAsync` deliberately stores:

`LastKnownGoodState.GameBuildSha256`

alongside the mod state.

`AutoDiagnoseCrash` loads that `LastKnownGoodState`, but the crash-bisect path never compares `GameBuildSha256` with the current game build before using the state as the experimental control.

A game update can therefore invalidate the meaning of “known good” while the bisector still treats it as a trustworthy control.

## Concrete sequence

1. setup survives and is recorded as last known good under game build A;
2. game becomes build B;
3. same mod state now crashes because of build/API incompatibility;
4. automatic bisect replays the A-era mod state against build B;
5. the baseline may fail;
6. CB-01 then permits arbitrary narrowing/confirmation.

## Ownership boundary

PR #14 owns the broader question of when build freshness is checked and when risky mods require revalidation before normal launch.

This audit's narrower requirement is:

> a diagnostic experiment may not call a stored state “known good” unless its recorded build provenance is still valid for the experiment, or the user explicitly chooses an unsupported cross-build diagnosis mode.

Do not implement a second independent game-build monitor solely inside the bisector. Reuse the canonical freshness mechanism once that boundary is defined.

---

# Finding CB-03 — P2 structural: “last known good” preserves state identity, not exact mod payload identity

## Classification

**Structural provenance gap. No in-place mod-payload mutation was runtime-reproduced here.**

`LastKnownGoodState` records:

- timestamp;
- snapshot root;
- game-build SHA;
- per-mod enabled/priority state.

Automatic crash probing reconstructs the baseline by taking the **current** planner snapshot/current mod catalog and applying the old enabled/priority map.

There is no exact per-mod payload/version/content fingerprint in the last-known-good record that the bisector validates before declaring the reconstructed state equivalent to the historical working state.

If a package is updated/replaced while retaining the same logical/member ID, the experiment can label the state “known good” even though the bytes being deployed are not the bytes that previously survived.

## Why this matters

The failure mode is the same class as CB-02 but at mod-payload provenance rather than game-build provenance: a bad control contaminates every subsequent half-split.

## Recommended boundary

Do not turn this into a backup/CAS redesign. A narrow future design can record or derive an immutable diagnostic fingerprint for enabled members at the last-known-good point, then refuse high-confidence bisection when the current payload set no longer matches.

PR #9 remains authority for full backup/portability; this audit only needs enough provenance to validate the diagnostic control.

---

# Finding CB-04 — P2: any process exit inside 12 seconds is treated as “crash reproduced”

## Classification

**Confirmed probe policy. Misclassification not reproduced against a real game process here.**

`ProbeCrashSubsetAsync` does:

- launch configured executable;
- wait for either 12-second delay or process exit;
- if process exits first, return `true`;
- otherwise kill the surviving process and return `false`.

The returned boolean is named/consumed as “reproduces crash,” but no distinction exists between:

- crash/abnormal termination;
- clean exit;
- launcher/stub handoff exit;
- user-initiated early close;
- environment refusal to start correctly after process creation.

This policy can feed an ordinary early exit directly into CB-01/CB-05 and eventually into a persistent confirmed culprit.

## Recommendation

Replace the boolean probe contract with an explicit outcome, for example:

- `ReproducedFailure`
- `SurvivedObservation`
- `Inconclusive`

Do not assume one universal Windows exit code reliably identifies every game crash. Define evidence appropriate to the supported launch model, and treat ambiguous early exit as inconclusive rather than confirmed reproduction.

---

# Finding CB-05 — P2: one observation per subset can become permanent 99-score blame

## Classification

**Confirmed design; broad gap already noted by `TEST_GAP_AND_PERFORMANCE_AUDIT.md`. This audit specializes the persistence consequence.**

The bisector performs one probe per tested half. There is no retry, quorum, confidence estimate, or nondeterministic/inconclusive state.

A single transient failure in one half steers all later narrowing. If that path reaches one item, the caller persists it as score 99 / confirmed.

The broad test/performance audit already requested a noisy/flaky reproducer policy test. The additional specialized requirement here is that **noisy evidence must be unable to promote persistent confirmed blame**.

## Regression matrix

At minimum, test:

- deterministic culprit;
- baseline always fails;
- full set never fails;
- alternating/flaky outcome;
- first probe fails spuriously, repeats survive;
- delayed/intermittent failure;
- cancellation/launch error returns inconclusive rather than culprit.

The persistence test should verify that only a diagnosis meeting the selected confidence policy reaches `confirmed=true`.

---

# Finding CB-06 — P2: automatic candidate selection can omit priority-changed suspects that issue fallback explicitly identified

## Classification

**Confirmed source selection mismatch.**

`ModIssueFallbackService.AnalyzeAndPersistAsync` treats both of these as changed candidates:

- newly enabled mods;
- enabled mods whose priority changed since the prior successful launch.

But `AutoDiagnoseCrash` initially selects only:

`current.Where(m => m.Enabled && (!known.Mods.TryGetValue(m.Id, out var old) || !old.Enabled))`

Only when **zero** newly-enabled suspects exist does it fall back to persisted issue suspects.

Therefore a session with:

- one innocent newly enabled mod; and
- one actually-problematic priority-changed mod

will bisect only the newly enabled set. The priority suspect produced by the issue-analysis path is ignored simply because at least one newly enabled candidate exists.

## Narrow repair direction

Use one canonical “changed since last good / active diagnosis candidates” calculation that includes at least enabled-state and priority changes, then apply explicit ranking/caps without silently excluding a whole change class.

`LastKnownGoodService.ChangedSinceLastGoodAsync` already recognizes enabled and priority differences and is a natural input to inspect before inventing another divergent selector.

---

# Finding CB-07 — P3 / overlap: restoration compensation uses the same operation token

## Classification

**Confirmed code shape; ordinary user cancellation is currently mitigated because automatic bisect is non-cancellable.**

The `finally` block restores `currentState` using the same `ct` passed through the operation.

Today, `AutoDiagnoseCrash` runs with `cancellable: false`, so the normal UI does not expose cancellation and MainWindow treats it as critical. That makes this materially less severe than CB-01 through CB-06.

However, if this workflow later becomes cancellable, or shutdown/host lifetime supplies a canceled token, compensation can be entered with a token already canceled and fail before restoration converges.

The async-lifetime audit is the specialized authority for cancellation/shutdown semantics. Any future crash-diagnosis cancellation work must use its compensation rules rather than “fixing” this in isolation.

---

# Finding CB-08 — capability gap: pair/interactions are detected but not minimized

## Classification

**Expected current algorithm limitation, not a false-confirmation bug.**

When neither half independently reproduces, `CrashBisectorEngine` returns the entire remaining set with:

> interaction between mods rather than one isolated culprit

For two suspects, the existing unit test correctly characterizes this.

For a larger candidate set, an interaction between one mod from each half causes the engine to return the whole remaining set rather than minimizing to the interacting pair/subset.

Because `Isolated=false`, the caller does **not** persist those mods as confirmed score-99 suspects. That fail-closed behavior is good.

A later capability improvement could use a delta-debugging/ddmin-style interaction reducer, but it should come only after CB-01 through CB-05 make the probe evidence trustworthy.

---

# Existing test coverage and exact gaps

## Present

`AutomationLogicTests.cs`:

- `CrashBisectorIsolatesSingleCulprit`
- `CrashBisectorReportsInteractionWhenHalvesAreClean`

`AutomationServiceTests.cs`:

- fallback ranking for a newly-enabled texture mod;
- fallback ranking for a newly-enabled plugin after startup failure;
- successful launch clears unconfirmed issue state;
- confirmed bisect state survives normal startup success.

## Missing

No inspected test proves:

- baseline control must currently survive;
- full suspect set must currently reproduce;
- stored game build must still match;
- stored mod payload identity still matches;
- clean/ambiguous early process exit is not automatically a crash;
- flaky/noisy probes cannot create confirmed blame;
- priority-changed candidates remain eligible when newly-enabled candidates also exist;
- a failed/inconclusive bisect leaves zero new confirmed issue records;
- large interaction sets are minimized;
- restoration converges after an exceptional probe path.

---

# Recommended independent implementation checkpoint

Keep this to one diagnosis-evidence boundary.

## Phase 1 — pure engine / policy tests first

Introduce a richer probe outcome/policy at the automation boundary and tests for:

1. baseline bad -> abort, no isolation;
2. full set clean -> abort as not reproducible;
3. deterministic culprit -> isolate;
4. flaky probe -> inconclusive unless selected retry/quorum policy is satisfied;
5. interaction -> no confirmed single culprit.

Do not launch real processes in these first tests.

## Phase 2 — caller provenance tests

Prove the caller refuses high-confidence bisection when:

- last-known-good game build is stale;
- required baseline payload identity is stale/unknown under the chosen design;
- priority-only changes are relevant alongside newly-enabled changes.

Coordinate the general build-freshness implementation with PR #14 rather than duplicating it.

## Phase 3 — persistence gate

Add an integration test that asserts:

> `ModIssueFallbackService.MarkBisectResultAsync` is reached only after the diagnosis result carries validated-control + reproducibility evidence.

A baseline failure, non-reproduction, ambiguous exit, or noisy/inconclusive probe must not write a score-99 confirmed suspect.

## Phase 4 — exact verification

Any production-source change must then:

- re-audit moved/changed body tracing under LR-001;
- use compile-backed caller closure under LR-002 if probe/result APIs change;
- preserve deployment transaction and recovery invariants;
- run the exact hosted Windows Release Gate;
- persist exact source/run evidence only after green.

---

# Things deliberately not changed

This support checkpoint does **not**:

- change `CrashBisectorEngine`;
- change MainWindow behavior;
- change launch-health or game-build policy;
- change `ModIssueFallbackService` persistence;
- change database schema;
- change deployment/recovery code;
- introduce a second planner/deployment engine;
- alter verification caches;
- claim a real Monster Hunter: World crash was reproduced.

The current production state remains exactly as canonical main defines it.

---

# Verification actually performed

Performed from the connected GitHub repository:

- inspected canonical history and current open PR/branch scopes;
- read the permanent continuity/handoff state and active LR-001 through LR-006;
- inspected current source bodies listed in the scope;
- inspected the current crash-bisector and issue-fallback test bodies;
- inspected the broad test/performance audit for existing crash-diagnosis ownership;
- inspected the async-lifetime audit for cancellation overlap;
- inspected the MainWindow responsibility audit for workflow ownership;
- inspected the SQLite audit for issue-suspect transaction ownership;
- inspected active PR #14's launch-health audit to avoid stealing its general build-revalidation scope.

Not performed:

- no local checkout / `git status`;
- no `dotnet test`;
- no PowerShell handoff validator;
- no hosted Windows Release Gate;
- no real game process/crash reproduction;
- no verification-cache promotion.

---

# Parallel-agent integration notes

- PR #4 remains the broad test/failure/performance map. This audit is the specialized authority for **crash-bisector evidence validity and confirmed-suspect promotion**.
- PR #6 remains authority for general async lifetime/cancellation/shutdown. CB-07 is intentionally subordinate to it.
- PR #14 remains authority for general game-build freshness/revalidation/Just Play policy. CB-02 only requires the diagnostic control to respect the canonical build-freshness invariant.
- The SQLite deep audit remains authority for issue-suspect transaction atomicity. This audit does not redesign those transactions.
- Smart Pack/provider work is unrelated and should not be coupled to this checkpoint.
- Windows filesystem/native replacement work is unrelated and should not be coupled to this checkpoint.

If any of those branches change the relevant source before implementation, re-evaluate these invariants against the new bodies rather than preserving line-level recommendations.

---

# Successor handoff

The next implementation agent for this lane should start with **tests that make false confirmation impossible**:

> Prove a current clean baseline, prove the full candidate set currently reproduces, represent ambiguous/noisy probe outcomes explicitly, and only then allow narrowing to produce a persistent confirmed suspect. Also include priority-changed candidates and reject stale last-known-good provenance under the canonical build/payload policy.

Do not begin by tuning the 12-second constant or by adding more heuristics to the score. The first job is to make the experiment itself valid.

The successor must re-read canonical `main`, open PRs, `CONTINUITY_PROTOCOL.md`, active Learned Rules, and this audit before editing. Preserve the permanent recursive continuity system and explicitly require the successor after them to do the same.

**Do not break the chain.**
