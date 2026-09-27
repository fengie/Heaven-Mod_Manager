# MainWindowViewModel responsibility / coupling audit — 2026-09-27

## Decision

Stop splitting MainWindowViewModel into additional page models at the current architecture boundary.

Activity, Coverage, Profiles, and Games already captured the clean passive/read-state seams. The responsibilities still in MainWindowViewModel are mostly shell coordination, cross-feature workflows, or state clusters whose presentation state is mutated by multiple workflows. Moving them into another page model now would mostly move code while introducing back-references to Mods, selected state, global busy/status, planner/deployment services, or application lifetime.

This is a design checkpoint only. No production source was changed by this audit.

## Classification legend

1. passive presentation/read state
2. page-local presentation orchestration
3. shell/global coordination
4. cross-feature application workflow
5. domain behavior
6. filesystem behavior
7. persistence behavior
8. composition/lifetime behavior

## Responsibility map

| Area | Class | Reads | Mutates | Key dependencies / workflows | Boundary assessment | Regression risk if extracted now |
|---|---:|---|---|---|---|---|
| Mods collection / ModsView / search / smart filters | 2 + 3 | Mods, staged/effective/update/issue state, SearchText, ModViewMode | ModsView filter/refresh, visible counts, selected tab | LogicalModFamilies, ModRowViewModel, issue/update/effective badges | Not a passive page seam. This is the central presentation state consumed by staging, analysis, issues, Nexus, profiles, launch and deployment. | High |
| Aggregate counts / header / footer / labels | 3 | Mods, Conflicts, IssueSuspects, OverlapRows | HeaderSummary, FooterText, property notifications | Changed(), RunBusy(), staging, issue refresh, analysis | Shell/global derived state. Moving it would create a bidirectional shell/page dependency. | High |
| SelectedMod + gallery loading | 2 + 4 | SelectedMod, member descriptors | ModRowViewModel visuals | ModVisualService, WPF dispatcher | Small in isolation but still anchored to the central Mods rows. Moving only the loader would be code motion, not a useful boundary. | Medium |
| Staged state capture / discard / enable-visible / disable-visible | 4 | Mods / ModsView | ModRowViewModel staged state and counters | profile staging, preview/apply, conflict choices, launch | Cross-feature draft state. It is the shared input to planner and deployment workflows. | High |
| Conflict analysis / effective-state summaries | 4 + 5 | Mods, staged state, planner snapshot, game semantics | Conflicts, ModRowViewModel effective state, counters | ManagerDatabase, DeploymentPlanner, LogicalModFamilies | A real future application/domain service may exist, but it is not a page-model seam: input/output intentionally span Mods and deployment analysis. | High |
| Conflict preview enrichment | 2 + 4 | Conflicts, Mods, previews | conflict option preview rows | TexturePreviewService | Coupled to analysis output and Mods identity mapping. Keep adjacent to analysis until that application boundary is designed. | Medium |
| Conflict choice staging | 4 | SelectedConflict, Mods | staged member/logical enabled state, analysis, timeline | RefreshAnalysis, ChangeTimelineService | User action changes the shared deployment draft. Not page-local read state. | High |
| Manual family chaining | 4 + 5 + 7 | SelectedConflict, Mods, staged state | database family/rule graph, rebuilt Mods, restored staged state | ManagerDatabase.ChainManualFamilyAsync, RuleGraph, Timeline | Atomic persistence/domain workflow plus UI draft preservation. Do not page-extract. | High |
| IssueSuspects | 2 + 4 | issue service, Mods members | IssueSuspects, every ModRowViewModel issue badge, ModsView, issue/header counters | ModIssueFallbackService, crash reporting, bisector, launch observation | Explicitly rejected as a page model. The same refresh both presents suspect rows and projects issue state back onto Mods. | High |
| OverlapRows / Explain Why | 2 + 4 | Inspector heatmap, Conflicts, SelectedOverlap | OverlapRows, SelectedExplanation, ExplainWhyStatus | EffectiveInspectorService, deterministic planner output, global RunBusy | Overlap display is read-oriented but its blocking/resolved label depends on current Conflicts; Explain Why is a planner-backed application query. Splitting now would require shell inputs/back-references. | Medium |
| Preview Apply | 4 + 5 + 6 + 7 | staged state, current DB mods | source capture, Conflicts, PlanPreviewText, StatusText, SelectedTab | CatalogService, planner, DB | Cross-feature dry-run orchestration. It intentionally shares most inputs with Apply. | High |
| Apply | 4 + 5 + 6 + 7 | process blockers, staged state, current DB | CAS/catalog capture, audits, deployment transaction, revalidation, applied row state, analysis/activity/status | ProcessGuard, Catalog, Planner, ManagerDatabase, DeploymentExecutor | Core application workflow crossing filesystem and SQLite commit semantics. Must stay above repositories. | Very high |
| Undo | 4 + 6 + 7 | committed deployment history | filesystem + DB via executor, Mods/analysis/activity/status | DeploymentExecutor | Transaction/recovery workflow, not presentation. | Very high |
| Nexus metadata / update sync | 4 + 6 + 7 | metadata gate, DB/catalog state | provenance/settings/supersession/families, Mods, analysis, status | NexusMetadataService, metadataGate | Cross-feature synchronization. Could later be an application use case, but not a page model. | High |
| periodic metadata loop | 3 + 4 + 8 | BusyVisibility, CriticalOperation | Nexus metadata, Mods/analysis | backgroundCts, metadataGate, Nexus | Shell lifetime/background coordination. Remains shell-owned. | High |
| import archive | 3 + 4 + 6 + 7 | file picker selection | source library, Nexus metadata, Mods, status | WPF OpenFileDialog, ArchiveImportService, Nexus | Application workflow with shell dialog and global refresh. | Medium-high |
| smart inbox / duplicate cleanup | 4 + 6 + 7 | inbox/archive/catalog state | library folders, DB catalog, Mods/analysis/status | SmartInboxService, DuplicateCleanupService, CatalogService | Cross-feature use case. Not passive presentation. | High |
| manual live-file adoption | 4 + 6 + 7 | live tree, planner manifest | source package, adoption DB records, catalog/Nexus/Mods | UnmanagedAdoptionService, Catalog, Nexus | Filesystem + persistence workflow. | High |
| profile list | 1 | ProfileRepository list | ProfilesPage rows | ProfilesPageViewModel | Already cleanly extracted and Windows-verified. | Closed |
| profile stage | 4 | SelectedProfile, profile_mods | every ModRowViewModel staged state | ProfileRepository, Changed() | Mutates global deployment draft; deliberately not part of ProfilesPageViewModel. | High |
| profile save | 4 + 7 | current persisted mod state, NewProfileName | profiles/profile_mods, profile list, status | ProfileRepository.SaveCurrentAsync | Atomic persistence operation plus shell status. Keep out of passive list model. | Medium |
| Activity read/list | 1 | operations + automation events | Activity rows | PresentationReadRepository | Already extracted and Windows-verified. | Closed |
| Coverage read/list | 1 | armor/index/manifest projections | Coverage rows | PresentationReadRepository | Already extracted and Windows-verified. | Closed |
| Games read/list | 1 | GameProfileRegistry.Load | Games rows | GamesPageViewModel | Already extracted and Windows-verified. | Closed |
| game scan/add/configure/switch | 3 + 4 + 8 | staged count, selected game, registry | registry, active game, process restart/WPF shutdown | GameProfileRegistry, dialogs, ProcessDebug, Application lifetime | Shell/application lifetime boundary. Must not move into GamesPageViewModel. | High |
| launch modded | 4 + 6 + 7 | staged count/blockers | optional Apply, backup/launch history/trust/issues/last-good, activity/status/tab | AutomationCoordinator, DeploymentExecutor indirectly | Cross-feature workflow. | Very high |
| safe-mode launch | 4 + 6 + 7 | planner snapshot / process guard | backup, transactional disable, game process, transactional restore | Backup, Planner, DeploymentExecutor | Explicit temporary deployment workflow; not page-local. | Very high |
| crash report / automatic diagnosis | 4 + 5 + 6 + 7 | last-good, current mods, launch history, issue suspects | repeated transactional deployment probes, process launches, issue state/timeline, restoration | CrashBisectorEngine, Issues, Planner, Executor | Deliberately spans diagnosis, deployment, persistence, process lifetime and UI. | Very high |
| health / support / diagnostics | 3 + 4 + 6 + 7 | DB/CAS/managed live state/logs | support files / diagnostic process / status | HealthService, SupportBundleService, external PowerShell | Shell command surface over cross-cutting diagnostics. | Medium |
| collection recipe export | 4 + 6 + 7 | mod/build/provenance state | JSON file on disk, status | CollectionRecipeService, ToolRoot | Small but still an application/export use case, not presentation state. | Low-medium |
| RunBusy / cancel / status / telemetry | 3 | BusyVisibility, critical state, staged count | busy CTS, busy UI, StatusText, FooterText, CriticalOperation | DiagnosticTelemetry, ExceptionPolicy, logger | Canonical shell-global operation coordinator. Must remain in MainWindowViewModel for current architecture. | High |
| Dispose / background lifetime | 8 | CTS fields | cancellation/disposal | WPF VM lifetime | Composition/lifetime behavior. Keep shell-owned. | High |

## Plausible extraction candidates considered and rejected

### Mods / ModsView page model

Dependencies: ManagerDatabase reads, LogicalModFamilies, ModRowViewModel, planner analysis, Nexus update metadata, issue projection, profile staging, deployment draft.

State read: Mods, SearchText, ModViewMode, staged/effective/update/issue fields.

State mutated: Mods rows, ModsView, staged state, effective state, issue/update/visual badges, aggregate counts.

Commands/workflows: filter, stage visible, discard, reindex, preview, apply, profiles, conflicts, launch, diagnosis.

Why rejected: this would become a second shell with callbacks into deployment, profiles, issues and analysis. It reduces MainWindowViewModel line count but does not create a lower-coupling boundary.

Risk: high.

### Conflicts page model

Dependencies: planner snapshot, Mods logical identity, staged state, texture previews, manual family persistence, timeline.

State read/mutated: Conflicts, SelectedConflict, Mods staged member state.

Commands/workflows: preview/apply, choose winner, manual chain, RefreshAnalysis.

Why rejected: conflict rows are an output of the global deployment draft and conflict choices mutate that same draft. Moving them without first extracting an application-level draft/analysis service creates cyclic ownership.

Risk: high.

### IssueSuspects page model

Dependencies: ModIssueFallbackService, launch history, crash reporting/bisector and Mods.

State read/mutated: IssueSuspects plus ModRowViewModel IssueBadge/IssueScore/IssueReason, ModsView, issue/header counters.

Commands/workflows: report GPU crash, report game crash, clear suspect, launch observation, automatic bisect.

Why rejected: one refresh intentionally projects persisted issue evidence onto both the Issues list and Mods rows. This is not page-local state.

Risk: high.

### Overlaps page model

Dependencies: EffectiveInspectorService and current Conflicts.

State read/mutated: OverlapRows, SelectedOverlap, SelectedExplanation, ExplainWhyStatus.

Commands/workflows: RefreshAnalysis -> RefreshOverlaps; Explain selected.

Why rejected now: heatmap rows are read-oriented, but their Needs choice / Resolved overlay presentation is computed against current Conflicts, while Explain Why is a planner-backed query with global busy/status. A page model would need shell-fed conflict state or duplicate resolver interpretation.

Risk: medium.

### Profile mutation model

Dependencies: ProfileRepository and central Mods staged state.

State read/mutated: SelectedProfile/NewProfileName, profile persistence, all Mod staged state.

Why rejected: list/read ownership is already cleanly extracted; mutations are application workflows over the global deployment draft.

Risk: medium-high.

### Import / Nexus / launch / crash / health page models

Why rejected: these are use cases, not passive page state. The correct future boundary, if needed, is an application service/use-case layer, not another WPF page model.

## Resulting rule for future work

Do not reopen page-model extraction merely because MainWindowViewModel is large. A future extraction should be justified by ownership of state and dependencies, not line count.

The next architecture work is the ManagerDatabase transaction-boundary audit in STORAGE_TRANSACTION_BOUNDARY_AUDIT.md. The first storage production change should be one read-only query seam only, independently verified before any second extraction.


---

# Independent Support Agent 3 re-audit — 2026-09-27

## Scope and canonical state

This addendum independently re-audits the WPF/MainWindow ownership decision after the original audit, rather than assuming the earlier conclusion is still correct.

Audit branch: `agent/support-3-mainwindow-ui-audit-20260927`.

Canonical `main` was re-queried repeatedly during the audit because another agent was working concurrently. The branch was cut from exact canonical commit `161b5fcba88470b7d941a3831624bdbf071ff668` ("Document PlannerSnapshotRepository verification boundary"). That state includes production source `8e0068bd44cc6735ffa9478067923ad5d9c54506` and parity-test hardening `64e666a19ce17c21bc696b46cce9c07bb257a686`.

The PlannerSnapshotRepository work was performed concurrently by another agent. This audit did **not** implement, alter, or extend that production boundary.

This session used the GitHub repository connector rather than a local checkout. There is therefore no local working tree whose `git status` can be inspected. To preserve the intent of the parallel-work rule, canonical remote history/state was inspected before every write and all audit writes are isolated on this dedicated branch.

At the branch point the PlannerSnapshotRepository candidate was explicitly **not yet Windows-closed**. This audit must not be used to imply green evidence for that production change.

## Independent conclusion

**The previous "stop splitting MainWindowViewModel into more page models" conclusion remains correct.**

The PlannerSnapshotRepository extraction reduces direct persistence coupling for planner-input reads, but it does not change ownership of the central mutable UI draft or turn any of the remaining large MainWindow responsibilities into a clean page-local seam.

Activity, Coverage, Profiles-list, and Games-list remain the only currently proven passive/read-state page seams. The remaining responsibilities are still one of:

- shared deployment-draft presentation state;
- cross-feature application orchestration;
- shell-global busy/status/cancellation state;
- WPF dialog/process/application lifetime coordination;
- background lifetime work;
- domain/filesystem/persistence operations already delegated to services.

A new page ViewModel would mostly require callbacks or shell back-references into `Mods`, staged state, conflict analysis, issue projection, global status/busy state, or process/application lifetime. That is line-count reduction, not coupling reduction.

**Further UI page-model architecture work is not currently justified.** If future refactoring pressure remains after the active production boundary is independently closed, prefer a narrowly scoped application-use-case/service extraction only when one workflow can own its dependencies without taking ownership of WPF bindings or the shared deployment draft.

## What changed since the original audit

The material architecture change since the original audit is the new read-only `PlannerSnapshotRepository`.

Current composition creates one planner-snapshot repository and supplies it to MainWindow planner workflows plus EffectiveInspector, GameUpdateImpact, LaunchHealthGate, UnmanagedAdoption, and Health. MainWindow no longer needs `ManagerDatabase.LoadPlannerSnapshotAsync` for analysis/restore/safe-mode paths.

That change is healthy layering, but it actually reinforces the original ownership result:

- MainWindow analysis still begins from `Mods` and its staged member state.
- Conflict rows remain derived from that shared draft.
- conflict choices still mutate that shared draft.
- overlap classification still reads the current conflict set.
- Apply/Undo/safe mode/crash diagnosis still coordinate planner output with deployment/process/lifetime services.
- global busy/status/cancellation and critical-operation state remain shell concerns.

The cleaner storage read boundary therefore removes one dependency direction without creating a new presentation-state owner.

## Current responsibility ownership

### Mods / ModsView / search / filters / counters

Mutable ownership remains split intentionally between `ModRowViewModel` row state and the MainWindow shell:

- `ModRowViewModel` owns applied/staged member dictionaries, parts, effective state, issue/update/visual badges, and row-derived labels.
- MainWindow owns the `Mods` collection identity, `ICollectionView`, search/filter mode, aggregate counters, selection, and the shared `Changed()` notification/refresh fan-out.
- profile staging, conflict choice, Apply, diagnosis, metadata refresh, and reload all read or mutate the same row state.

This is not presentation-local to a Mods page. Extracting a ModsPageViewModel now would either make it the new shell or require callbacks for planner/deployment/profile/issue workflows.

### Conflicts and staged choices

Conflicts are planner output from the same staged draft represented by `Mods`. Choosing a winner writes back into `ModRowViewModel` staged state, then re-runs analysis. Manual family chaining additionally crosses persistence, logical-family rebuilding, staged-state restoration, and timeline recording.

The state owner is therefore the deployment draft + application workflow, not the Conflicts tab.

### IssueSuspects

`RefreshIssueSuspects` intentionally has two outputs:

1. the `IssueSuspects` list;
2. issue badge/score/reason projection back onto every affected `ModRowViewModel`, followed by ModsView/counter refresh.

Crash reports, launch observation, clearing, and automatic bisect mutate/read the same persisted issue evidence. A page model cannot own this without cross-writing the Mods page.

### Overlaps / Explain Why

Overlap rows are read-oriented, but their "Needs choice" vs "Resolved overlay" presentation reads current `Conflicts`. Explain Why is a planner-backed application query and participates in global `RunBusy` state.

The new PlannerSnapshotRepository makes the Inspector's persistence dependency cleaner; it does not make current conflict state local to the Overlaps page.

### Profiles

The existing split is still the correct one:

- `ProfilesPageViewModel` owns list reads and stable row collection identity.
- MainWindow owns selected profile, staging into the global Mod draft, save workflow status, and global busy handling.

Moving profile mutation into the page model would cross the deployment-draft ownership boundary.

### Games

The existing split is still the correct one:

- `GamesPageViewModel` owns registry list projection only.
- discovery/add/configure/switch mutate the registry or open WPF dialogs.
- switching persists the active game, starts a fresh manager process, and shuts down the current WPF application because the service graph is game-scoped.

That is application lifetime behavior, not page state.

### Import / Nexus / inbox / cleanup / adoption / health / support / recipes

These are application use cases over services. WPF owns only user interaction (file dialog, global operation/status surface) while actual archive, Nexus, catalog, filesystem, diagnostics, or export behavior is delegated.

If any of these are extracted later, an application use-case/service boundary is more appropriate than another page ViewModel.

### Apply / Preview / Undo / safe mode / launch / crash diagnosis

These flows intentionally coordinate the global staged draft, planner snapshots, planner output, transaction-aware deployment, process blockers, launch observation, issue state, activity, and status.

They are not UI page responsibilities. They must remain above storage and deployment transaction owners.

### RunBusy / status / cancellation / critical operation

`RunBusy`, `StatusText`, `FooterText`, busy overlay state, foreground cancellation, and `CriticalOperation` are shell-global operation coordination.

`MainWindow.OnClosing` consults `CriticalOperation` and blocks normal close while a non-cancellable transactional/startup operation is active. That lifecycle contract must remain centralized unless a future application-operation coordinator replaces it as one coherent boundary.

## WPF / binding contract safety

Current binding safety depends on keeping `MainWindowViewModel` as the DataContext and preserving the legacy public surface.

The closed page extractions deliberately alias collection identity:

- `ActivityRows = Activity.Rows`
- `OutfitRows = Coverage.Rows`
- `Profiles = ProfilesPage.Rows`
- `Games = GamesPage.Rows`

That is important: changing to replacement collection objects can silently break selection/view/list semantics even if property names remain the same.

The integration suite currently has source-level contract tests for Activity, Coverage, Profiles, Games, conflict-family UI, Explain Why, and inline Run binding mode. This is useful but is **not** a complete reflection/compile-time proof of all MainWindow bindings. MainWindow.xaml currently exposes a broad binding surface, including generated relay commands and many computed properties.

Future refactors must therefore preserve, or explicitly regression-test, at minimum:

- `MainWindowViewModel` DataContext assumptions;
- existing property and command names;
- `Mods`, `Conflicts`, `IssueSuspects`, `OverlapRows`, and extracted-page collection identities;
- two-way selected state where used;
- generated CommunityToolkit relay-command names;
- Ctrl+F code-behind behavior that selects the Mods tab and focuses `ModSearchBox`;
- MainWindow closing/closed semantics;
- game switch restart/shutdown behavior.

Do not infer safety from a successful backend test run alone when changing the binding surface.

## Dispatcher / threading audit

### Correctly off-thread or asynchronous

The current source already avoids several important UI-thread hazards:

- deterministic planner building and heavy analysis projection run inside `Task.Run`;
- game discovery is sent through `Task.Run`;
- database/service reads are awaited before observable-collection mutation;
- overlap heatmap row projection is performed before the final Dispatcher collection replacement;
- page VMs query repositories first and use the Dispatcher only for row collection mutation;
- `DispatcherWatchdog` runs its timer loop off-thread and posts only one lightweight heartbeat at a time.

No evidence was found that deployment filesystem mutation, archive extraction, hashing, migration, or the planner itself is intentionally executed synchronously on the WPF Dispatcher.

### UI-thread work worth watching

Two concrete projection paths still do more than the minimum inside Dispatcher callbacks:

1. `ReloadMods` builds logical families, constructs all `ModRowViewModel` rows, projects update badges, replaces the collection, refreshes the view, and fans out notifications inside one Dispatcher invocation.
2. `RefreshIssueSuspects` replaces suspect rows, projects issue state across all Mods/members, refreshes ModsView, and fans out counters inside one Dispatcher invocation.

These are real source-observable UI-thread costs, but this audit has no timing evidence showing either is currently a user-visible hot path. They should be profiled before optimization. If measurements show stalls, compute immutable projections off-thread and keep only observable-object/collection mutation on the Dispatcher.

Conflict preview enrichment also awaits preview generation serially per missing option. That may add latency when many blocking choices require previews, but there is no measurement here proving it is a hot path.

## Collection and refresh efficiency

`ObservableRangeCollection.ReplaceAll` preserves collection identity and performs the replacement against the underlying item list, then emits one `Reset` rather than one change event per item. This is the correct batching shape for the current UI.

The current MainWindow/partials contain a small number of explicit `ModsView.Refresh()` calls concentrated in:

- mode/search changes;
- `Changed()` when a filter/search is active;
- full Mod reload;
- effective-state analysis refresh;
- issue projection refresh.

Staging bulk operations already use `suppressChanged` and perform one final `Changed()`, avoiding an obvious per-row refresh storm.

The aggregate counter getters scan `Mods`, and `Changed()` raises a broad group of notifications. That is simple and safe; it becomes a performance concern only if measured library size/interaction frequency makes the repeated scans material.

No evidence was found of large collection replacement causing per-item CollectionChanged storms.

## Background lifetime and concurrency finding

The periodic Nexus metadata loop is correctly shell-owned, has its own `backgroundCts`, uses `metadataGate` to serialize metadata refreshes, skips a tick when it observes a foreground/critical operation, and is cancelled during ViewModel disposal.

There are, however, two lifetime/concurrency hardening opportunities that are **not** page-model seams:

1. `InitializeAsync` starts the loop with `_ = AutoMetadataLoopAsync(backgroundCts.Token)` and does not retain/await the Task. Disposal cancels the CTS but cannot await loop termination, and an unexpected exception outside the loop's filtered network/IO catches can become an unobserved fault.
2. The `BusyVisibility/CriticalOperation` check is advisory, not an atomic exclusion with foreground `RunBusy`. A foreground workflow can begin after the periodic loop passes that check. `metadataGate` serializes Nexus metadata operations, but unrelated foreground workflows such as deployment do not take that gate while the background loop can later call `ReloadMods` and `RefreshAnalysis`.

No failure was reproduced in this audit, so this is recorded as a concrete concurrency/lifetime risk from source structure, not as a confirmed production bug.

If this is hardened later, keep it as one shell-lifetime checkpoint: retain the background Task, establish explicit coordination with foreground state mutation, cancel and await it on disposal, and add tests for shutdown plus foreground/background overlap. Do **not** create a page ViewModel to solve it.

## Performance verdict

Observed/obvious:

- planner computation is deliberately off-thread;
- range collection replacement is batched;
- bulk staged mutations suppress repeated `Changed()` calls;
- ReloadMods and issue projection contain non-trivial UI-thread projection work;
- full `ModsView.Refresh()` is intentionally used after state classes that affect filtering;
- background refresh can interleave with foreground operation start because its busy check is not a lock.

Not established by evidence:

- that MainWindow itself is currently a measurable bottleneck;
- that the counter scans are expensive enough to cache;
- that conflict preview serialization is user-visible;
- that another page extraction would improve performance.

Do not use file size or theoretical complexity alone to justify a refactor.

## Acceptance criteria for any future UI ownership change

Before changing another MainWindow ownership boundary, require all of the following:

1. one state owner can be named without shared mutation through shell callbacks;
2. the extracted unit has a narrower dependency set than MainWindow;
3. no shell back-reference is required;
4. XAML public property/command names and collection identity are explicitly preserved or intentionally migrated with tests;
5. WPF Dispatcher responsibility is reduced, not merely relocated;
6. application shutdown/restart/critical-operation behavior is unchanged;
7. focused contract tests cover the seam;
8. exact repository verification is rerun for changed production source;
9. the current active source boundary is closed before starting the next one.

No remaining page responsibility currently meets those criteria better than staying in the shell.

## Learned Rules decision

No new Learned Rule is justified by this audit alone.

The background-loop concerns are newly documented risks, not reproduced incidents. Existing LR-001 already covers the concrete moved-production-body instrumentation incident. Add another Learned Rule only if a future implementation or failure provides durable evidence.

## Handoff / integration notes

- This audit is documentation-only; no production C# or XAML was changed.
- Preserve the active PlannerSnapshotRepository verification boundary. Do not claim this audit closes it.
- The independent verdict is: **stop splitting page models remains correct**.
- Do not begin another MainWindow source extraction while the PlannerSnapshotRepository candidate is unresolved.
- If UI architecture work resumes later, investigate measured Dispatcher stalls or application-use-case ownership before page-model decomposition.
- The next agent must re-read `AGENTS.md`, `CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, this audit, and the current canonical `NEXT_STEPS.md` before editing.
- The successor must preserve the permanent continuity constitution and explicitly require the agent after them to inherit and recursively propagate it again.

**Do not break the chain.**


## Post-branch canonical delta revalidation

While this audit was being written, canonical `main` advanced through `528401925b1d09b3d65c9652de8e4f2024e3677f` and documentation commit `0e561f3c059475ad443a79ac4a27dd68264a7bdb` to repair PlannerSnapshotRepository caller migration after Windows Release Gate `36335255922` failed.

The MainWindow delta converts `RestoreLastGood` and `LaunchSafeMode` from expression-bodied relay-command methods to block-bodied methods while retaining the same shell-owned `RunBusy`, staged/planner/deployment, status, and process-lifetime responsibilities. App composition also injects PlannerSnapshotRepository into additional read consumers. No new page-local state owner or WPF binding seam was introduced.

Therefore the independent UI ownership conclusion is unchanged at `0e561f3c059475ad443a79ac4a27dd68264a7bdb`.

The failed Windows gate belongs to the active PlannerSnapshotRepository production boundary and must be repaired/closed independently. This documentation audit does not make that candidate green.
