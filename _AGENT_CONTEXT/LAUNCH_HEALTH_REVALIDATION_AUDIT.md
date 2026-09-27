# Launch-health and game-build revalidation audit — 2026-09-27

## Status

**Static support audit. No production behavior changed.**

Canonical `main` inspected for this checkpoint:

`6ada5a5c4cc83afadfba42bc6af6559540920e3d`

This lane was selected only after the first candidate audit became obsolete during execution: PR #10 took ownership of remote-preview network trust and PR #11 took ownership of diagnostics/support-export privacy. Per the parallel-agent protocol, that overlapping work was not opened as another PR. This replacement audit deliberately avoids all currently owned lanes.

## Why this lane is independent

Active parallel work already owns:

- SQLite transaction atomicity;
- verification infrastructure / CI / supply chain;
- MainWindow/WPF architecture;
- broad test-gap/performance analysis;
- async lifetime / cancellation / shutdown;
- Windows filesystem containment and archive-extraction safety;
- multi-source discovery / Smart Pack acquisition;
- MHW semantic coverage / gap fill;
- state backup / portability / disaster recovery;
- remote-preview network trust;
- diagnostics privacy / secret handling;
- legacy migration retry/recovery.

This audit instead owns the current **game-build freshness -> mod revalidation -> launch-health -> Just Play** safety chain.

The specialized files reviewed are:

- `src/MhwModManager.Filesystem/GameBuildMonitor.cs`;
- `src/MhwModManager.Automation/LaunchHealthGateService.cs`;
- `src/MhwModManager.Automation/AutomationCoordinator.cs`;
- `src/MhwModManager.Automation/ModTrustService.cs`;
- `src/MhwModManager.Diagnostics/HealthService.cs`;
- `src/MhwModManager.Mhw/GameProcessGuard.cs`;
- `src/MhwModManager.App/App.xaml.cs`;
- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs`;
- `src/MhwModManager.Storage/ManagerDatabase.cs`;
- current Automation and Integration test bodies relevant to build/revalidation.

---

# Internal assignment

Answer these questions from actual source bodies:

1. What event marks a mod as requiring revalidation?
2. What event clears that state?
3. Does launch actually block on an unresolved revalidation requirement?
4. Can the manager's build fingerprint become stale while the app remains open?
5. Does normal Just Play prevent a second game launch the same way deployment and Safe Mode do?
6. Are those invariants regression-tested?

Success means producing a durable implementation-ready audit with exact call chains and focused regression checkpoints, without modifying the active production boundaries owned by other agents.

---

# Executive result

The game-update safety model has a **confirmed semantic hole**:

> a game update can mark plugin/executable/game-data mods as requiring revalidation, but any later successful deployment clears that requirement for every enabled mod even though deployment does not validate runtime compatibility.

The launch gate compounds this by treating `NeedsRevalidation` as a warning rather than a blocker, and the default Just Play path does not surface those warning findings before launch.

There is also a freshness gap: the executable fingerprint is checked during application startup, but the launch-health path does not re-check it. If the game changes while the manager stays open, Just Play can launch without ever marking affected mods.

Finally, the ordinary Just Play path does not consult `GameProcessGuard` when there are no staged changes, while deployment and Safe Mode explicitly do.

These are correctness/safety findings. No crash, corruption, or second-instance failure was reproduced at runtime in this audit.

---

# Existing strengths

Several pieces are already well designed in isolation:

1. `GameBuildMonitor` hashes the game executable with SHA-256 rather than relying only on timestamp/version text.
2. On a changed executable it marks only mods containing `Plugin`, `Executable`, or `GameData` files; texture-only mods are not automatically distrusted.
3. The current Integration hardening test proves that a changed executable marks a plugin mod while leaving a texture mod unmarked.
4. `LaunchHealthGateService` already composes database/manifest health, dependencies, planner blockers, unmanaged files, and revalidation status.
5. Incomplete operations and hard health errors are converted into launch blockers.
6. `Apply` refuses to deploy while the configured game process is running.
7. Safe Mode independently refuses to mutate/launch when the game is already running.
8. Normal deployment remains transactional; this audit does not question that boundary.

The defect is primarily **how these individually useful signals are connected and cleared**.

---

# Finding LH-01 — P1: successful deployment clears runtime revalidation without runtime evidence

## Classification

**Confirmed source behavior. No runtime reproduction required to prove the state transition.**

## Mark path

`GameBuildMonitor.CheckAsync`:

1. hashes the configured game executable;
2. compares it with the stored build fingerprint;
3. when the SHA changes, loads the planner snapshot;
4. selects mods with `Plugin`, `Executable`, or `GameData` files;
5. calls:

`db.MarkRevalidationAsync(risky, "... should be revalidated before being trusted.", true, ct)`.

The UI independently describes this state as:

> “The game executable changed and this logical mod contains plugin/executable/game-data content that should be revalidated.”

That is a runtime/game-compatibility concept.

## Clear path

After **any successful Apply**, `MainWindowViewModel.Apply` does:

`enabledAfter = stage.Where(... enabled ...).Select(...)`

then:

`db.MarkRevalidationAsync(enabledAfter, "Validated by a successful deployment after the current game build was observed.", false, ct)`.

This clears the revalidation requirement for **every enabled mod**.

The deployment result proves that the deployment transaction succeeded under its filesystem/database preconditions. It does not launch the game, exercise a plugin ABI, load an executable component, validate game-data compatibility, or otherwise establish the runtime evidence implied by the revalidation reason.

## Concrete bypasses

### A. unrelated staged change clears risky mods

1. game executable changes;
2. plugin A is marked `NeedsRevalidation=true`;
3. user stages an unrelated texture/priority/mod-state change B;
4. Apply succeeds;
5. `enabledAfter` includes plugin A;
6. plugin A is cleared to `required=false` despite never being runtime-tested.

### B. enabling a stale risky mod clears itself before launch

1. binary/plugin mod was previously marked for revalidation;
2. user enables it;
3. Apply deploys it successfully;
4. post-Apply code clears its revalidation flag;
5. subsequent Just Play sees no revalidation finding.

The exact state intended to warn/block first use after a game update therefore disappears as a side effect of installing files.

## Why P1

This is a false trust-state transition in the hands-free launch workflow. It can convert “game changed; runtime-risky mod should be revalidated” into “validated” using evidence that is unrelated to runtime compatibility.

It does not by itself prove game corruption, so this is not P0.

## Narrow repair direction

Do not clear runtime revalidation in generic deployment success.

Define one explicit revalidation policy, for example:

- revalidation remains required until an explicit user decision; or
- revalidation remains required until a launch under the **current exact build fingerprint** survives the chosen observation criterion; or
- a dedicated compatibility check produces acceptable evidence.

Whichever policy is chosen, encode the evidence type in the state transition. “Deployment succeeded” must not silently stand in for “runtime compatibility validated.”

## Required regression

1. seed plugin A as `required=true`;
2. make unrelated texture B staged;
3. perform/represent successful deployment;
4. assert A remains required;
5. enable a required plugin and apply it;
6. assert it remains required until the designated revalidation event;
7. assert texture-only mods are not accidentally promoted into the runtime-risk set.

---

# Finding LH-02 — P1: unresolved revalidation is warning-only and Just Play proceeds automatically

## Classification

**Confirmed source behavior.**

`LaunchHealthGateService.EvaluateAsync` converts an enabled `NeedsRevalidation` mod into:

`AutomationSeverity.Warning`.

The final readiness result is:

`findings.All(x => x.Severity != AutomationSeverity.Blocker)`.

Therefore `NeedsRevalidation` does **not** make `Ready=false`.

`AutomationCoordinator.LaunchAndObserveAsync`:

1. calls `PrepareForPlayAsync`;
2. checks only `if (!health.Ready)`;
3. otherwise starts the game.

No explicit acknowledgement/override is required.

## Warning visibility problem

The Just Play command receives the final `LaunchObservation` and sets status from that observation. The pre-launch warning list is not surfaced for user confirmation in this flow.

So the revalidation warning is not merely non-blocking; in the default hands-free path it can be effectively silent before the game launches.

## Why this matters

The repository uses strong language in several places:

- GameBuildMonitor: risky mods “should be revalidated before being trusted”;
- UI effective-state text: game executable changed and runtime-risk content “should be revalidated”;
- README: Just Play runs the health/dependency/conflict gate.

A “required revalidation” flag that neither blocks nor asks for an override is not currently functioning as a safety gate.

## Narrow repair direction

Choose and document one policy:

### Preferred safety contract

- enabled runtime-risk mods with `NeedsRevalidation=true` are launch blockers;
- user may resolve through an explicit, auditable revalidation/override action;
- an override is not implemented as a generic Apply side effect.

If product requirements insist on warning-only behavior, Just Play must at least surface the warning and require a deliberate acknowledgement before launch. It should not be silently automatic.

## Required regression

- enabled required plugin => `LaunchHealthReport.Ready == false`, if blocker policy is selected;
- disabled required plugin => does not block;
- enabled texture-only mod after game update => no false blocker;
- explicit approved override/revalidation event => becomes launchable;
- warning-only policy, if deliberately retained => requires an explicit UI acknowledgement path and cannot auto-launch silently.

---

# Finding LH-03 — P1: game-build freshness is startup-scoped, not launch-scoped

## Classification

**Confirmed source call-graph gap. Actual mid-session game update not reproduced.**

Current startup does:

`gameBuild.CheckAsync`

during `startup.intelligence.game-build`.

But:

- `LaunchHealthGateService` does not depend on `GameBuildMonitor`;
- `AutomationCoordinator` does not depend on `GameBuildMonitor`;
- `PrepareForPlayAsync` does not refresh the game build;
- `LaunchAndObserveAsync` does not refresh the game build.

Therefore the stored build fingerprint can be fresh at manager startup and stale at launch time.

## Concrete sequence

1. manager starts; game executable fingerprint = A;
2. manager remains open;
3. Steam/manual update replaces executable with build B;
4. user presses Just Play;
5. launch health evaluates existing revalidation flags but never calls `GameBuildMonitor.CheckAsync`;
6. no B-vs-A comparison occurs in the launch path;
7. runtime-risk mods may remain trusted/unmarked.

## Why P1

This bypasses the entire revalidation mechanism rather than merely downgrading its severity.

The window exists whenever the manager stays open across a game update.

## Narrow repair direction

Make build freshness part of the pre-launch invariant.

Possible minimal design:

1. inject a build-freshness service into the preparation/gate boundary;
2. immediately before launch, compare cheap metadata first if desired;
3. when changed/suspicious, compute the authoritative hash;
4. persist/mark revalidation before final gate evaluation;
5. re-evaluate the launch findings after marking.

Do not make the UI responsible for remembering to invoke this.

## Required regression

With a temporary executable:

1. record build A;
2. construct the launch preparation service;
3. mutate executable to B **after** initial observation;
4. evaluate Just Play;
5. assert B is detected before process start;
6. assert runtime-risk enabled mods enter required revalidation;
7. assert launch cannot bypass the resulting policy.

---

# Finding LH-04 — P2: Just Play can attempt launch while the game is already running when no staged Apply occurs

## Classification

**Confirmed missing guard in source; Windows/game second-instance behavior not reproduced.**

`MainWindowViewModel.Apply` explicitly checks:

`s.ProcessGuard.GetKnownBlockers()`

and stops deployment if the game is running.

`LaunchSafeMode` performs the same style of guard before removing mods and launching.

Normal `LaunchModded`, however:

- only invokes `Apply` when `StagedCount > 0`;
- when there are no staged changes, goes directly to `AutomationCoordinator.LaunchAndObserveAsync`.

Neither `AutomationCoordinator` nor `LaunchHealthGateService` has a `GameProcessGuard` dependency.

It then calls `ProcessDebug.Start` on the game executable.

## Risk

Depending on game behavior this can:

- launch a second instance;
- start a short-lived process that immediately exits because another instance owns the game;
- distort launch success/failure history;
- distort mod trust counters;
- create false crash-diagnosis evidence;
- perform save/adoption/health preparation for a launch that should not have been attempted.

No claim is made that Monster Hunter: World definitely permits two full instances.

## Narrow repair direction

Make “configured game process already running” a normal launch-health blocker, not only a deployment/Safe Mode guard.

The guard belongs in the shared pre-launch path so staged and unstaged launch behavior cannot diverge.

## Required regression

Inject/abstract process-state detection and prove:

- running game => Just Play returns blocked before `Process.Start`;
- stopped game => normal path proceeds;
- staged and unstaged paths share the same guard;
- blocked attempt does not update launch history/trust as a failed launch.

---

# Finding LH-05 — P2 policy gap: externally changed managed files are warning-only in automatic launch

## Classification

**Confirmed policy; severity depends on intended product contract.**

`HealthService`:

- missing managed file => `Error`;
- unreadable managed file => `Error`;
- expected hash mismatch => `DEPLOYED_CHANGED` with severity `Warning`.

`LaunchHealthGateService` maps health `Error` to Blocker and every other severity to Warning.

Therefore an externally modified file that the manifest expected to own does not block Just Play.

This may be intentional to tolerate manual tweaking. If so, the product needs an explicit policy/acknowledgement because the automatic workflow otherwise launches from state that no longer matches the manager's recorded deployed hash.

This audit does not recommend changing that severity in the same checkpoint as LH-01 through LH-04. First make the policy explicit and add a characterization test.

---

# Revalidation state-machine mismatch

Current effective state can be summarized as:

1. **Build changed** -> risky mods `required=true`.
2. **Launch health** -> required state is warning-only.
3. **Any successful Apply** -> every enabled mod `required=false`.
4. **Successful launch** -> trust/last-known-good is recorded, but the launch is not what currently clears revalidation.

The strongest evidence is therefore backwards:

- weak evidence (file deployment) clears runtime distrust;
- stronger runtime evidence (successful startup observation) is stored in trust/history but is not the revalidation transition.

A successor should repair this as a state-machine contract rather than as isolated UI conditionals.

---

# Current test coverage and exact gaps

## Existing coverage found

`tests/MhwModManager.IntegrationTests/HardeningTests.cs` verifies:

- initial game build baseline;
- changed executable detection;
- plugin mod gets `NeedsRevalidation=true`;
- texture mod remains false.

That is useful mark-side coverage.

`tests/MhwModManager.AutomationTests/AutomationServiceTests.cs` includes:

- save snapshot success;
- dependency-doctor plugin/loader detection;
- mod trust accumulation;
- other automation services.

`AutomationLogicTests.cs` covers categorization and crash-bisector logic.

## Missing focused coverage

No direct `LaunchHealthGateService` behavior test was found in the two current Automation test files inspected.

No regression was found proving:

- required revalidation blocks or requires acknowledgement;
- Apply cannot clear unrelated revalidation;
- build is refreshed immediately before launch;
- running game blocks ordinary Just Play;
- launch warnings are surfaced before process start;
- a blocked launch cannot alter trust/history.

Because private GitHub code-search indexing is not treated as exhaustive under LR-002, whole-solution compilation/search should still be used by the implementation agent for caller closure.

---

# Recommended implementation checkpoints

Do not combine this into one broad automation rewrite.

## Checkpoint A — pin the intended contract in tests

Tests first, without production redesign:

1. required risky mod remains required after unrelated successful deployment;
2. required risky mod remains required after its own deployment;
3. launch policy for required revalidation is explicit;
4. mid-session build change is detected before launch;
5. running process blocks ordinary launch;
6. blocked launch does not mutate launch history/trust.

This is the recommended next independent checkpoint.

## Checkpoint B — remove deployment-as-revalidation

Delete/replace the global post-Apply `MarkRevalidationAsync(enabledAfter, ..., false)` behavior with the chosen evidence-based state transition.

Keep this production boundary narrow.

## Checkpoint C — launch-time build/process freshness

Move authoritative build freshness and process-running checks into the shared pre-launch boundary.

Do not duplicate them in individual buttons.

## Checkpoint D — warning presentation policy

After hard safety invariants are correct, decide which remaining warnings require acknowledgement versus informational display.

---

# Things deliberately not changed

- no production source;
- no tests;
- no database schema;
- no verification cache;
- no MainWindow decomposition;
- no transaction behavior;
- no filesystem/reparse behavior;
- no state-backup design;
- no provider/download architecture;
- no shared continuity files, because many active PRs are concurrently editing those files and a new isolated audit avoids avoidable merge conflicts;
- no Learned Rule number reserved here.

The audit is intentionally a one-file support checkpoint.

---

# Verification actually performed

- canonical `main` inspected and rechecked before task selection;
- recent canonical history inspected;
- active PR scopes inspected, including the two PRs that superseded the first candidate lane;
- required continuity/read-order documents were already read for this autonomous support run;
- actual source bodies inspected for build monitoring, revalidation persistence, launch gate, health scan, automation launch, game-process guard, Apply, and startup wiring;
- task-relevant Git history inspected for `LaunchHealthGateService` and `GameBuildMonitor`;
- both current Automation test source files inspected;
- relevant Integration hardening test body inspected;
- README Just Play contract inspected.

## Not verified

- no local working tree was available through this chat session after Work-mode handoff was declined, so no local `git status` is claimed;
- no `dotnet test`;
- no PowerShell handoff validator;
- no hosted Windows Release Gate;
- no real Steam/game update while manager stayed open;
- no second Monster Hunter: World instance attempt;
- no runtime assertion that a particular mod actually breaks after a game update;
- no verification cache was promoted.

All P1 findings above are source-level invariant/call-graph defects, not claims of reproduced game corruption.

---

# Parallel-work integration notes

- PR #7 remains authority for filesystem containment/CAS/reparse/ReplaceFileW behavior.
- PR #9 remains authority for manager-state backup/portability/disaster recovery. This audit only references the pre-launch save snapshot to map call order.
- PR #10 remains authority for remote-preview network security.
- PR #11 remains authority for diagnostic/support share-boundary privacy.
- PR #12 remains authority for legacy migration convergence/recovery.
- PR #13 is Smart Pack/provider implementation and does not own launch revalidation semantics.
- PR #6 owns async/cancellation/shutdown races. Do not fold cancellation behavior into this audit's implementation unless required by a focused launch test.
- PR #4 is the broad test/performance map; this audit is the specialized authority for the build-revalidation-launch state machine.

If another branch changes the launch gate before this audit integrates, reconcile behavior against these exact invariants instead of blindly preserving line-level recommendations.

---

# Successor handoff

The next agent should implement **tests first** for the build-revalidation-launch contract:

> Prove that game-build revalidation cannot be cleared by deployment alone, prove a mid-session executable change is detected before Just Play, and prove ordinary Just Play blocks when the configured game process is already running. Then make the smallest production changes needed to satisfy those tests, run compile-backed caller closure under LR-002, re-audit changed-body tracing under LR-001, and run the exact hosted Windows Release Gate before declaring the boundary closed.

Before editing, re-read canonical `main`, open PRs, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and this audit. Preserve the permanent recursive continuity system and explicitly require the successor after you to do the same.

**Do not break the chain.**
