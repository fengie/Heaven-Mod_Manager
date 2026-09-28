# Re-audit and external research findings — v8.8.0 handoff hardening

These findings were gathered while re-reading the v8.8 source after the initial Function Verification implementation. They are guidance/evidence, not claims that every recommendation has already been implemented.

## Implemented in this revision

### 1. First-chance exception observation stays, hot-path full logging becomes opt-in

`.NET` raises `AppDomain.FirstChanceException` whenever a managed exception is thrown, before handler lookup. That is useful for associating nested exceptions with active function scopes, but writing a full stack trace for every throw can generate unnecessary I/O/noise for expected handled exceptions.

v8.8 now keeps lightweight counting/scope observation always active and only writes full `FIRST-CHANCE` exception records when `MHW_FIRST_CHANCE_DETAIL=1` (also accepts `true`, `yes`, `on`). Process exit records the total first-chance count.

Official references:
- https://learn.microsoft.com/dotnet/api/system.appdomain.firstchanceexception
- https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics-runtime
- https://learn.microsoft.com/dotnet/core/diagnostics/eventpipe

### 2. Generated `obj`/`bin` C# must never enter function verification

The function scanner is a source verifier, not a generated-code verifier. A recursive `src/**/*.cs` scan can encounter SDK/WPF-generated code after a previous build. The scanner now explicitly excludes any file under `bin` or `obj`.

### 3. Trusted bootstrap must prove its own integrity

The embedded v8.7 source snapshot is used as verification evidence. It is now checked against `trusted-v8.7.0-files.json` during scanning: unmanifested files, missing manifested files, or hash mismatches fail closed.

### 4. Function IDs must not silently collide for explicit-interface implementations

Two explicit interface members can share the same member name/signature while belonging to different interfaces. Function IDs now include explicit-interface qualification for methods/properties/indexers/events to avoid incorrect cache aliasing.

### 5. Report explicit call-site coverage

The verifier now counts explicit invocation/object-construction/constructor-initializer call sites inside each source body. A body is call-site-covered when it is already known-good, has the required runtime function scope, or has the narrow tracer-recursion exemption. Reports include total/covered/uncovered explicit call-site counts.

This does **not** claim static proof for compiler-generated calls (property access, disposal, async state-machine internals, etc.). Exceptions from those still flow through the containing runtime scope.

## Strong future candidates (not implemented here)

### ActivitySource for coarse-grained operation tracing

Microsoft recommends `System.Diagnostics.ActivitySource` for custom distributed tracing. It offers parent/child correlation and listener-controlled sampling. It is a good candidate for major operations such as scan/plan/deploy/recover/Nexus refresh, but **not** a reason to replace every function scope with heavyweight spans. Keep per-function verification distinct from operation observability.

References:
- https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing
- https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs

### Source-generated structured logging for high-frequency diagnostics

Modern .NET recommends `LoggerMessageAttribute` source generation for high-performance structured logging because templates are parsed at compile time and allocations/boxing are reduced. Consider it if the diagnostic system is consolidated in a future architecture pass. Do not mix that migration into transactional/deployment changes casually.

Reference:
- https://learn.microsoft.com/dotnet/core/extensions/high-performance-logging

### Failure-triggered crash/hang diagnostics for tests

`dotnet test` supports blame/crash/hang diagnostics. A future Windows CI/harness improvement could rerun a failed/hung test stage with appropriate dump collection rather than enabling dump generation on every successful run.

Reference:
- https://learn.microsoft.com/dotnet/core/tools/dotnet-test-vstest

## Architecture debt confirmed by source re-audit

These are prioritized future refactor targets, not reasons to destabilize v8.8 now:

- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs` — ~1,245 lines; still contains direct file/directory normalization/import work. Split by feature/application use case.
- `src/MhwModManager.Storage/ManagerDatabase.cs` — ~694 lines; evaluate repository/query-command decomposition while preserving transaction boundaries.
- `src/MhwModManager.Filesystem/NexusMetadataService.cs` — ~685 lines; separate transport/cache/metadata interpretation where natural.
- `src/MhwModManager.Core/AutoCompatibility.cs` — ~681 lines; candidate for rule-family decomposition if tests remain strong.
- `src/MhwModManager.App/App.xaml.cs` — composition/bootstrap remains sizable; keep it composition-oriented and resist new domain/filesystem behavior.

The backend deployment/journal/CAS safety path remains materially healthier than the WPF presentation boundary. Refactor presentation/application boundaries before redesigning the transactional core without evidence.

## 2026-09-27 architecture research

- Current Microsoft .NET guidance favors centralized service registration and constructor injection; a Generic Host is available for desktop lifetime/DI, but this milestone intentionally avoids changing WPF lifetime composition at the same time as feature decomposition.
- Current Microsoft MVVM guidance supports incremental loose-coupling for complex multi-screen applications. The chosen path is therefore feature-boundary extraction first, page view models second.
- Microsoft.Data.Sqlite transaction guidance reinforces that transaction scope is the atomic unit. Repository extraction must not split existing deployment transactions.
- MO2 and Vortex conflict UIs both expose concrete competing providers/winner relationships. Explain Why follows that pattern while adding this manager's confidence/evidence/provenance data.
- Implementation consequence: Explain Why consumes the existing DeploymentPlanner/ConflictDecision output rather than creating a second resolver.



## 2026-09-27 post-closure architecture guidance

Fresh Microsoft guidance was checked before choosing the next slice:

- .NET dependency injection documentation recommends central service
  registration plus constructor injection, and explicitly notes that WPF owns
  its desktop lifetime integration.
- The .NET Generic Host packages DI, configuration, logging, and lifetime
  management and is supported in WPF, but adopting it changes application
  lifetime/composition semantics.
- CommunityToolkit.Mvvm documents constructor injection / an external DI
  container as the normal modularity path rather than providing a competing
  container.
- Microsoft.Data.Sqlite documents a transaction as the atomic unit of work;
  statements participating in an atomic operation must remain on the same
  connection/transaction.

References:
- https://learn.microsoft.com/dotnet/core/extensions/dependency-injection
- https://learn.microsoft.com/dotnet/core/extensions/generic-host
- https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/ioc
- https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions

Implementation consequence: do **not** combine Generic Host adoption with the
next page-view-model/database split. First establish small responsibility seams
under the existing lifetime model, verify them, and treat host/DI migration as
a later independent checkpoint.

## 2026-09-27 Games presentation seam inspection

Source inspection confirmed that game selection is intentionally a process-lifetime
boundary, not an in-process service swap:

- `AppPaths.Discover()` constructs a `GameProfileRegistry`, resolves the active
  game, then derives game-specific workspace/database paths.
- `App.OnStartup` constructs the database, planner, executor, scanners, Nexus,
  automation and other services from that resolved `AppPaths.Game`.
- `MainWindowViewModel.SwitchGame` persists the selected active profile, launches
  a fresh copy of the manager executable, and shuts down the current WPF app.
- `ScanInstalledGames`, `AddGame`, and `ConfigureGame` mutate
  `GameProfileRegistry`; they are application operations, not passive
  presentation reads.
- The safe extraction boundary is therefore only
  `GameProfileRegistry.Load()` -> observable list state. `SelectedGame`,
  mutation, switching, restart/shutdown, busy/status, and composition remain in
  the shell/application layer.

Implementation consequence: `GamesPageViewModel` is deliberately read/list-only.
The existing game commands may live in a MainWindow partial for file-level
decomposition, but they must not move into the page model or change restart
semantics.

## 2026-09-27 MainWindow / storage boundary audit

The post-Games re-audit found that the clean passive presentation seams are
exhausted for now. The remaining MainWindowViewModel state is materially
cross-coupled:

- Mods/ModsView is the common surface for staged state, effective state, update
  badges, issue badges, search/filter views and aggregate counters.
- IssueSuspects refreshes both its own list and ModRowViewModel issue state, then
  refreshes the Mods view/counters; crash report, launch and bisect workflows
  mutate the same persisted evidence.
- Conflicts are planner output from the global staged draft, while conflict
  choices mutate that same draft.
- Overlap presentation uses current Conflicts to distinguish Needs choice from
  Resolved overlay, and Explain Why replays the planner.
- import/Nexus/profile mutation/launch/crash/health flows are application use
  cases rather than passive page state.
- RunBusy, StatusText/FooterText, cancellation and process/application lifetime
  remain shell-global concerns.

Implementation consequence: stop page-model splitting until a new state-ownership
seam exists. See MAINWINDOW_RESPONSIBILITY_AUDIT.md.

The ManagerDatabase audit also confirmed that repository decomposition must be
transaction-aware rather than table-oriented. In particular:

- DeploymentExecutor has three critical shared SQLite transaction boundaries:
  prepared journal; final deployment commit; rollback state reconstruction.
- ReplaceModFilesAsync, ChainManualFamilyAsync, ProfileRepository.SaveCurrentAsync,
  adoption recording, mod-trust batches and issue-suspect batches each own
  legitimate atomic write groups.
- Existing PresentationReadRepository is the preferred model for early
  read-only extraction.
- LoadPlannerSnapshotAsync is the strongest first read boundary because it is
  already a planner-specific projection, has no write transaction ownership,
  and is consumed across analysis/inspection/health/launch-gate workflows.

Recommended next source slice: PlannerSnapshotRepository only. Preserve the
current full/filtered/empty query behavior and current connection semantics in
the first extraction; do not combine that move with GetModsAsync extraction,
transaction redesign, Generic Host/DI migration or DeploymentExecutor changes.
See STORAGE_TRANSACTION_BOUNDARY_AUDIT.md.



## 2026-09-27 PlannerSnapshotRepository first-gate caller correction

Hosted run `36335255922` proved the prior static caller list was incomplete: `NexusMetadataService` and `GameBuildMonitor` also consumed planner snapshots. The compiler, not the connector's private-code search index, was authoritative. Both callers are read consumers only, so adding them to the repository injection set does not change the transaction-boundary conclusion.


## 2026-09-27 planner filter casing parity

Run `36335692754` exposed a test assumption, not a production regression. The pre-extraction query and `PlannerSnapshotRepository` both deduplicate `fileModIds` case-insensitively while retaining the first input string, then bind that string into SQLite `IN` under default comparison semantics. Thus `["B","b"]` against stored id `b` yields no mod_files, while `["b","B"]` yields the rows. This behavior is now regression-tested and deliberately unchanged.

## 2026-09-27 independent WPF/MainWindow support audit

Support Agent 3 independently re-ran the MainWindow ownership audit against canonical state through `161b5fcba88470b7d941a3831624bdbf071ff668`, including the concurrent PlannerSnapshotRepository candidate.

Result: the earlier **stop page-model splitting** decision still holds. The new planner read repository removes direct database coupling from planner-input consumers, but does not change ownership of the shared staged Mod draft, conflict output/choice mutation, issue projection into Mods, overlap dependence on Conflicts, shell-global busy/status/cancellation, or game/application lifetime.

The existing Activity, Coverage, Profiles-list, and Games-list ViewModels remain the clean passive/read-state seams. Future extraction pressure should be evaluated as application-use-case ownership rather than by MainWindow line count.

Two concrete UI-thread projection areas were recorded for measurement, not speculative refactoring: `ReloadMods` constructs logical/row presentation state inside a Dispatcher callback, and `RefreshIssueSuspects` projects suspect state across Mods inside a Dispatcher callback. `ObservableRangeCollection.ReplaceAll` already batches replacement into one Reset, and planner computation is already off-thread.

One shell-lifetime risk was newly documented: the periodic metadata loop is launched fire-and-forget and its pre-tick busy/critical check is not atomic with a foreground `RunBusy` start. It is correctly cancellable and metadata-gated, but a future hardening checkpoint should retain/await the loop task and explicitly coordinate background state mutation with foreground operations if this risk is prioritized. No failure was reproduced, so no Learned Rule was added.

Full detail and future acceptance criteria are in `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md`.

This audit changed documentation only and does not close or alter the active PlannerSnapshotRepository verification boundary. Preserve the continuity constitution recursively for the successor and the agent after them.


Post-audit revalidation: canonical main advanced to `0e561f3c059475ad443a79ac4a27dd68264a7bdb` with a PlannerSnapshotRepository caller-migration repair after failed run `36335255922`. The MainWindow changes did not alter state ownership or create a new page seam, so the support-audit conclusion remains unchanged.


Integration note: PlannerSnapshotRepository subsequently closed at `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3` / run `36336190920`; the UI audit remains documentation-only and does not change that verified production state.


## 2026-09-28 post-PR57 archive failure-cleanup audit

Canonical main includes PR #57's streamed archive extraction, mid-entry cancellation, actual-output budgeting, and current-partial-file cleanup for cancellation / budget `InvalidDataException`. The final support branch was reconciled onto main `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`, which also contains the separate PR #61 provenance repair.

A focused post-merge source audit found a separate failure-cleanup contract gap. The cancellation handler deletes the owned partial file before rethrowing the original `OperationCanceledException`. If that cleanup itself fails, the cleanup exception replaces cancellation; at the current Smart Inbox boundary, `IOException` / `UnauthorizedAccessException` are caught as recoverable item failures, so requested cancellation can conditionally be downgraded and later inbox work can continue. Non-matching streamed I/O failures after output creation also bypass the current per-file cleanup path. No runtime delete/write-fault reproduction is claimed.

See `_AGENT_CONTEXT/ARCHIVE_STREAMING_FAILURE_CLEANUP_AUDIT_2026-09-28.md`. LR-011 records the reusable rule: cleanup must not replace the primary failure/cancellation semantics. Keep the older LR-008 import-publication problem separate; #57's per-current-file cleanup does not make whole import destinations transactionally invisible or residue-free.
