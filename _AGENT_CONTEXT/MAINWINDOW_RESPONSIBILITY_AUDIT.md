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
