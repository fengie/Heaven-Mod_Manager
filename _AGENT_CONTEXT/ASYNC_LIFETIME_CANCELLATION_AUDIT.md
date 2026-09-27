# Async/background lifetime, cancellation, and shutdown audit — Support Agent 5

Audit date: 2026-09-27  
Canonical audit base: `6ada5a5c4cc83afadfba42bc6af6559540920e3d`  
Support branch: `agent/support-5-async-lifetime-audit-20260927`

## Why this support lane exists

Four parallel support lanes were already occupied:

1. SQLite / transaction atomicity;
2. CI / verification / supply-chain;
3. MainWindow / WPF responsibility architecture;
4. broad test-gap / failure-mode / performance coverage.

This audit deliberately does **not** repeat those jobs. It takes the concrete shell-lifetime risk noted by Support Agent 3 and traces it as an independent execution/cancellation problem across WPF shutdown, `RunBusy`, the periodic metadata loop, fire-and-forget tasks, and state reload.

No production C# is modified by this audit.

## Verification honesty

Performed:

- canonical GitHub `main` / recent-history inspection;
- current continuity-rule inspection;
- source-level control-flow and lifetime audit;
- comparison with Support Agent 3 UI audit and Support Agent 4 test-gap audit;
- static inspection of relevant integration tests.

Not performed:

- no local Windows execution;
- no WPF runtime reproduction;
- no new unit/integration tests executed;
- no hosted Windows Release Gate;
- no verification-cache promotion.

Source-derived findings below are labeled as confirmed where the control-flow result follows directly from the checked code. Runtime scheduling observations that depend on WPF `SynchronizationContext` behavior are explicitly labeled as runtime hypotheses requiring a focused test.

---

# Executive findings

## A1 — CONFIRMED: metadata reload can discard unapplied staged mod changes

`MainWindowViewModel.ReloadMods` reconstructs every `ModRowViewModel` from persisted `ModDescriptor.Enabled/Priority` values and replaces the entire `Mods` collection.

`ModRowViewModel` initializes:

- `_appliedMembers` from persisted member state; and
- `_stagedMembers` as a new copy of `_appliedMembers`.

Therefore a call to `ReloadMods` destroys any in-memory staged draft unless the caller explicitly captures and reapplies it.

The code already demonstrates awareness of this invariant in `ChainConflictFamily`:

1. `var staged=CaptureStage();`
2. perform family mutation;
3. `await ReloadMods(ct);`
4. reapply the captured state with `ApplyProfileState(staged)`.

The metadata paths do **not** do this:

- periodic `AutoMetadataLoopAsync` -> `Nexus.RefreshAsync` -> `ReloadMods`;
- manual `SyncMetadata` -> `Nexus.RefreshAsync` -> `ReloadMods`.

The periodic loop checks only busy/critical state. It does not check `StagedCount`.

### User-visible failure

A user can stage enable/disable changes, leave them unapplied, and then have a periodic metadata tick replace `Mods` from persisted state. The staged draft disappears even though the user never pressed Discard.

Manual “Sync metadata + visuals” can do the same thing while staged changes exist.

This is not merely an architectural concern; it follows directly from the row constructor/reload behavior.

### Required regression

A focused test must prove:

> With one or more staged changes present, a metadata refresh that rebuilds library rows does not change the staged member state.

For the periodic path, an even safer contract may be:

> Periodic metadata refresh does not rebuild the user draft while `StagedCount > 0`; it either skips the tick or refreshes metadata without replacing the staged rows.

Do not globally “preserve stage across every ReloadMods call.” Some callers intentionally reflect a newly committed authoritative state (Apply/Undo/Restore). Preservation must be scoped by workflow semantics.

---

## A2 — CONFIRMED: periodic metadata exclusion is advisory, not mutual exclusion

Current loop:

1. wait for periodic tick;
2. check `BusyVisibility` / `CriticalOperation`;
3. try `metadataGate.WaitAsync(0, ct)`;
4. execute Nexus refresh;
5. reload Mods;
6. refresh planner analysis.

`RunBusy` independently:

1. checks `BusyVisibility`;
2. creates `busyCts`;
3. sets busy/critical UI state;
4. starts the foreground action.

There is no common lock/operation lease spanning these two paths.

A foreground operation can begin **after** the periodic loop passes its busy check and acquires `metadataGate`.

The metadata gate only serializes metadata refreshes with other code that explicitly takes `metadataGate`. Apply, Undo, profile workflows, cleanup, health, deployment analysis, and many other foreground operations do not take it.

### Why the overlap matters

`NexusMetadataService.RefreshAsync` is not read-only. It can write:

- `mod_provenance`;
- preview/visual/update settings;
- `mods.family_id` through family hints;
- `mod_supersession` replacement;
- sync timestamps.

Family IDs and supersession state feed library grouping / analysis semantics.

After those writes, the periodic loop also calls `ReloadMods` and `RefreshAnalysis`.

Thus the possible overlap is not “background network traffic plus foreground work”; both sides can mutate or rebuild state consumed by planner/UI workflows.

### Contract

`BusyVisibility` and `CriticalOperation` are UI state, not a concurrency primitive.

Background state mutation needs an explicit operation-level exclusion/lease shared with foreground workflows whose assumptions require a stable library/planner state.

A future fix should not solve this by making every operation take `metadataGate`; that semaphore describes one service concern, not the whole application mutation domain.

---

## A3 — CONFIRMED: ordinary window close cancels cancellable workflows but does not await them

`MainWindow.OnClosing` blocks close only when:

`vm.CriticalOperation == true`.

`RunBusy` sets:

- `CriticalOperation = !cancellable`.

Therefore any `RunBusy(..., cancellable: true, ...)` operation does **not** block window close.

On close:

1. `MainWindow.OnClosed` synchronously calls `MainWindowViewModel.Dispose()`;
2. the ViewModel cancels `busyCts`, cancels `backgroundCts`, and disposes them;
3. no foreground task or background loop is retained/awaited;
4. because `ShutdownMode=OnMainWindowClose`, application shutdown proceeds;
5. `App.OnExit` disposes the watchdog, file-change service, and logger.

Cancellation is therefore requested, but graceful cancellation/compensation is not awaited before application lifetime ends.

### Why this is materially dangerous

Several “cancellable” operations perform filesystem/DB mutations and depend on the process staying alive long enough to reach their cancellation handling:

- archive import;
- metadata sync;
- manual adoption;
- profile save;
- Smart Inbox;
- safe duplicate cleanup;
- manual family chaining;
- game launch observation;
- reindex/capture.

Many individual DB transactions remain safe, but cross-filesystem/DB workflows can have compensation or convergence steps after an await.

Most notably, this UI close path makes the previously documented duplicate-cleanup defect reachable through an ordinary action:

1. Smart Cleanup is `cancellable: true`;
2. `DuplicateCleanupService.ArchiveSafeAsync` can `Directory.Move(source,dest)`;
3. it then awaits a cancellable DB delete;
4. user closes the window in this interval;
5. ViewModel cancels `busyCts`;
6. application does not wait for workflow convergence;
7. archive directory may exist while the DB row still points at the old missing source.

The deep SQLite audit already classified the move-before-delete condition as D1. This audit establishes an ordinary shell-lifetime trigger for it.

The same shutdown pattern weakens confidence in normal-exception compensation for adoption/import: compensation code can exist in the async method, but application shutdown currently does not wait for that async method to reach the compensation path.

### Desired shutdown contract

Normal user close should distinguish:

- **critical non-cancellable operation:** block close until the existing commit/rollback boundary completes;
- **cancellable side-effect operation:** request cancellation, keep the process/dispatcher alive, await bounded graceful completion/compensation, then close;
- **pure read/UI operation:** cancellation may be immediate.

“Cancellable” must not mean “safe to terminate the process immediately.”

---

## A4 — CONFIRMED: periodic metadata loop is unowned fire-and-forget work

`InitializeAsync` starts:

`_ = AutoMetadataLoopAsync(backgroundCts.Token);`

The task is not retained.

`Dispose` cancels/disposes the token source but cannot await loop termination.

The loop catches:

- outer `OperationCanceledException`;
- inner `HttpRequestException`, `IOException`, and `UnauthorizedAccessException`.

An unexpected exception from:

- database materialization;
- parsing/data conversion;
- row reload/projection;
- planner analysis;
- Dispatcher invocation;
- another future dependency

escapes the loop task.

Because the task is discarded, the periodic service silently dies except for the process-wide `TaskScheduler.UnobservedTaskException` hook if/when GC later observes it.

### Required behavior

A lifetime-owned background loop should:

- be stored;
- have one owner;
- report unexpected termination immediately;
- have explicit restart/fail policy;
- be canceled and awaited during shutdown.

Do not rely on `UnobservedTaskException` as the supervision mechanism.

---

## A5 — CONFIRMED lower-severity fire-and-forget paths

### Selected-mod gallery load

`OnSelectedModChanged` launches:

`_ = LoadModVisualsAsync(value);`

The method handles only IO/UnauthorizedAccess exceptions.

This is lower risk because it updates the specific row it was passed and does not mutate authoritative deployment state. Still:

- unexpected exceptions can become unobserved;
- work can finish after the row was replaced/removed;
- it has no lifetime cancellation token.

Recommended treatment: supervised “latest selection” task or cancellation generation only if this becomes a measured issue. Do not overengineer it ahead of the metadata/shutdown problems.

### Search debounce

`_ = DebounceSearchAsync(cts.Token)` is better bounded:

- previous token is canceled;
- `OperationCanceledException` is handled;
- the only post-delay action is Dispatcher refresh.

This is an acceptable lightweight pattern, though shutdown behavior should be included in a WPF lifecycle test.

### File-change telemetry

`FileChangeHintService` event handling launches `DiagnosticTelemetry.RecordSignalAsync` without awaiting it.

`RecordSignalAsync` internally catches diagnostic persistence failures, so correctness risk is low. Under prolonged DB stalls, however, repeated 750-ms watcher flushes can create overlapping telemetry tasks. Treat this as observability backpressure, not an authoritative-state defect.

### DispatcherWatchdog

The watchdog retains its loop Task but `Dispose` only cancels the CTS; it does not wait for the task.

App disposal then disposes the logger immediately.

This is a small shutdown ordering gap. It is not comparable in severity to foreground workflow cancellation, but a future lifetime coordinator should close it naturally.

---

# Runtime scheduling hypothesis: “background” metadata may still resume on the WPF Dispatcher

This is an inference requiring a focused runtime test, not a source-only verified failure.

`AutoMetadataLoopAsync` is invoked from `MainWindowViewModel.InitializeAsync`, which is awaited during WPF startup. The loop is not started with `Task.Run`, and the code contains no explicit synchronization-context suppression.

Under standard WPF async behavior, continuations after `await` are expected to resume through the captured Dispatcher synchronization context.

If confirmed, local synchronous work inside `NexusMetadataService.RefreshAsync` can still consume Dispatcher time even though network/file APIs are asynchronous. The service contains synchronous local metadata parsing, directory/image discovery, grouping, and other work between awaits.

Do not blindly add `ConfigureAwait(false)` everywhere. First add a runtime thread/Dispatcher assertion around the loop and measure actual Dispatcher occupancy. Then move only genuinely heavy pure computation/filesystem enumeration off-thread while keeping observable WPF mutations on the Dispatcher.

---

# Full foreground/background state timeline

## Periodic metadata tick

Current state machine:

`Idle -> Tick -> advisory Busy check -> metadataGate try-enter -> Nexus DB/filesystem mutation -> ReloadMods -> RefreshAnalysis -> release metadataGate -> Idle`

Problems:

- no foreground operation lease;
- no staged-draft preservation;
- task lifetime not retained;
- unexpected fault can terminate the loop.

## Foreground RunBusy

Current state machine:

`Idle UI -> BusyVisibility/CriticalOperation set -> action(ct) -> status -> clear busy -> dispose CTS`

Foreground-to-foreground exclusion is effectively centralized by the busy overlay/guard because commands begin on the WPF shell and `BusyVisibility` is set before the first awaited action.

That does **not** provide background exclusion.

## Window close during cancellable action

`RunBusy active(cancellable) -> OnClosing allows close -> OnClosed Dispose -> busy token canceled -> background token canceled -> window closes -> App shutdown/disposal -> async work may still require continuation/compensation`

The process lifetime currently ends before there is a positive proof that all cancellable side-effect workflows reached a safe quiescent point.

---

# Reload semantics audit

Calls to `ReloadMods` fall into different semantic classes.

## Authoritative-state reload — should reflect persisted state

Examples:

- Undo after deployment commit;
- Restore Last Known Good after deployment commit;
- crash-diagnosis restoration;
- operations whose purpose is to change applied state.

These should **not** blindly restore the prior staged draft.

## Metadata/library-shape reload — should not silently discard unrelated draft

Examples:

- SyncMetadata;
- periodic metadata refresh;
- archive import;
- manual adoption;
- family chaining.

Family chaining already preserves the draft explicitly.

Import/adoption can add new members, so preservation must be by stable member ID:
- existing staged member states survive;
- new members use their intended default/persisted state.

Periodic refresh has an even simpler safe option: skip row replacement while staged changes exist and retry next tick.

This distinction should become an explicit helper/use-case contract rather than remaining caller folklore.

---

# Current tests and why they are insufficient

`UxHardeningTests.Background_metadata_and_remote_visual_cache_are_hardened` currently checks source strings such as:

- `SemaphoreSlim metadataGate`;
- `metadataGate.WaitAsync(0,ct)`;
- “foreground/critical operation is active”.

Those assertions prove that defensive-looking code exists. They do not prove:

- atomic foreground/background exclusion;
- staged-draft preservation;
- shutdown completion;
- loop supervision;
- cancellation convergence;
- no post-dispose state mutation.

The test name currently overstates what is behaviorally verified.

No inspected integration test exercises WPF close while a cancellable side-effect workflow is active.

---

# Exact missing tests

## P0 — staged-draft preservation

1. Seed an applied mod OFF.
2. Construct row state and stage it ON without Apply.
3. Run the same reload path used after metadata refresh.
4. Assert staged state is still ON while applied state remains OFF.
5. Repeat with a logical family / partial-member draft.
6. Repeat with metadata regrouping that changes family composition; preserve stable member IDs and use safe defaults for genuinely new members.

For the periodic policy variant, assert a tick with staged changes skips row replacement and leaves the draft untouched.

## P0 — foreground/background exclusion

Introduce a testable coordination seam (not a UI-property assertion).

Deterministic schedule:

1. background metadata obtains permission to begin but pauses before first write;
2. foreground Apply/other mutating RunBusy begins;
3. assert the two mutation scopes cannot overlap;
4. release one side;
5. assert the other proceeds afterward or the background tick is skipped.

Also test the inverse ordering.

## P0 — graceful close of cancellable side-effect work

Use a controllable workflow/fault seam:

1. start Smart Cleanup;
2. pause immediately after successful archive move and before DB delete;
3. request normal MainWindow close;
4. assert close requests cancellation but app lifetime remains open until the workflow reaches compensation/commit;
5. assert final filesystem and DB state converge;
6. only then permit shutdown.

Repeat representative cases for adoption/import where compensation matters.

## P1 — background task supervision

1. inject one unexpected exception from periodic metadata;
2. assert it is logged/observed by the owner immediately;
3. assert defined policy: next tick continues or loop is explicitly marked failed;
4. no reliance on GC-triggered `UnobservedTaskException`.

## P1 — shutdown joins owned loops

1. start metadata loop with a deterministic fake tick;
2. start watchdog loop;
3. initiate shutdown;
4. assert cancellation requested;
5. assert both owned loops complete before logger/database-lifetime teardown;
6. assert no post-dispose persistence/log callbacks.

## P1 — Dispatcher occupancy

WPF/STA test or instrumented runtime fixture:
- record thread/Dispatcher access before/after timer tick and inside representative local metadata phases;
- prove whether metadata continuations resume on UI Dispatcher;
- if so, measure before moving work off-thread.

## P2 — gallery/search lifetime

- rapid selection A -> B -> C while prior visual loads are delayed;
- closing window during pending gallery load/debounce;
- no unobserved exception and no invalid UI mutation.

---

# Recommended implementation boundary

Do not solve this as scattered booleans.

A clean future production checkpoint is:

## Shell operation/lifetime coordinator

Responsibilities:

1. own one foreground mutation lease;
2. give background maintenance a non-blocking `TryEnter`/skip mechanism;
3. retain background loop Tasks;
4. own application-lifetime cancellation;
5. track active cancellable operation completion;
6. support graceful close:
   - critical operation: block close;
   - cancellable side-effect operation: cancel + await;
   - then dispose services/logging;
7. expose state to the ViewModel rather than using WPF visibility as the lock.

This may remain inside MainWindow/App initially; it does not require Generic Host adoption.

Do **not** combine this with:
- broad MainWindow page-model splitting;
- ManagerDatabase decomposition;
- deployment transaction redesign;
- Generic Host migration;
- Nexus transport/performance redesign.

One lifetime boundary at a time.

---

# Minimal fix ordering

If the project chooses to implement these findings, recommended order is:

1. **Prevent staged-draft loss** on metadata-triggered reloads.
2. **Introduce explicit background-vs-foreground mutation exclusion.**
3. **Retain/supervise the metadata loop Task.**
4. **Add graceful cancellation-and-await on normal window close.**
5. Bring watchdog/visual auxiliary tasks under the same lifetime ownership where justified.
6. Only then measure Dispatcher occupancy and consider off-thread metadata work.

The first two can be tested independently of a larger application-lifetime redesign.

---

# Interaction with other audits

## Deep SQLite audit

This audit does not supersede `SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`.

It strengthens D1 by identifying normal WPF close/cancellation as a concrete trigger path for the duplicate-cleanup move-before-delete window.

SQLite transaction boundaries remain authoritative.

## Support Agent 3 UI audit

That audit correctly classified the periodic metadata loop as shell lifetime/application coordination rather than a page-model seam.

This audit deepens that finding by proving staged-draft loss from `ReloadMods` semantics and tracing shutdown/cancellation ordering.

The “stop page-model splitting” conclusion remains unchanged.

## Support Agent 4 test/performance audit

That audit already calls out broad concurrency/cancellation gaps and Nexus fan-out performance.

This audit narrows the WPF lifetime problem into deterministic test cases and does not duplicate deployment concurrency or Nexus throughput work.

---

# Candidate Learned Rule for integration

Do not append this rule blindly while parallel support branches may also be adding ledger IDs. If/when this audit is integrated, allocate the next canonical Learned Rule ID.

Proposed rule:

> **Background mutators require explicit lifetime ownership and mutation coordination.** UI status such as BusyVisibility/CriticalOperation is advisory presentation state, not mutual exclusion. A background task that mutates shared DB/UI/planner state must be retained/supervised, coordinate with foreground mutation through an explicit primitive, and be canceled/awaited before dependent services are disposed.

Trigger/evidence:
- periodic metadata loop passes a non-atomic busy check then mutates provenance/family/supersession state;
- metadata reload reconstructs staged rows from persisted state;
- normal close cancels cancellable side-effect workflows without awaiting convergence.

---

# Successor handoff

A future implementation agent must:

1. verify actual canonical `main` first;
2. read the permanent continuity constitution and active Learned Rules;
3. read this audit together with the MainWindow responsibility audit, deep SQLite audit, and test-gap audit;
4. choose one lifetime checkpoint only;
5. add behavioral/fault tests before claiming the race/shutdown contract is fixed;
6. treat any production source edit as a new exact verification boundary;
7. preserve existing deployment/SQLite safety invariants;
8. update durable continuity and exact verification evidence;
9. explicitly require its successor to preserve and recursively propagate the continuity constitution to the agent after them.

**Do not break the chain.**
