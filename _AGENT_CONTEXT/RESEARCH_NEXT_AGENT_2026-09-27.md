# Final research handoff — 2026-09-27

User requested immediate push as remaining usage reached 1%. Research stops here. No production source changed during these research checkpoints. No new tests were executed beyond documentation handoff checks. Prior Linux/Windows CI evidence remains valid for its recorded source, not for unexecuted scenarios proposed here.

## Read and act in this order

1. CHECKPOINT.md and SESSION_HANDOFF_2026-09-27.md: branch, PR, architecture, all twelve implemented workflows, tested commits, tools and Git publishing mechanics.
2. RESEARCH_FOMOD_2026-09-27.md: first reproduce Inbox partial-import cleanup and duplicate success/failure reporting; fix with focused regression tests. Then resolve FOMOD omitted-folder-destination and priority-tie compatibility through fixtures. Hidden-step alwaysInstall semantics remain unresolved.
3. RESEARCH_RECOVERY_2026-09-27.md: actual transaction boundary, current crash/undo coverage, proposed missing cases, profile delta behavior, and diagnosis outcome limitations.
4. Perform native Windows interaction and real-game acceptance in a disposable workspace before release. Do not claim CI exercised the WPF screens.

## Final Windows/performance findings

Sources read on 2026-09-27:
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model

Microsoft distinguishes UI virtualization from retaining the underlying data. TreeView virtualization can be enabled; recycling reduces container churn. Dispatcher work must remain short to preserve responsiveness. These principles guide measurement; they do not prove a measured regression in this app.

Source observation: WorkflowWindow.RefreshAsync runs WorkflowAnalysis.Explore in Task.Run, but its folder builder instantiates TreeViewItem objects for all directories. A virtualized file grid therefore does not make the entire explorer lazy. Benchmark synthetic large file sets with many distinct folders, measuring time to first usable screen, UI responsiveness, peak memory and filter latency. If needed, bind lightweight directory nodes and create children on expansion. Preserve selection/filter state when refreshing. Avoid starting an optimization without a baseline.

Native acceptance priorities:
- Open every workflow tab; verify errors reach the status display and controls recover after failure.
- FOMOD chooser: keyboard navigation, exactly-one selections, hidden-page transitions, cancel, remembered choices, long descriptions, DPI scaling and window resizing.
- Inspector: displayed provider matches actual plan; blocked paths do not imply deployable output; staged versus deployed state is clear.
- Rules: cycle rejection, stale selections, exact-file provider validation, shared-provider pin, global incompatibility without file overlap.
- Profiles: parent edits flow through absent overrides, explicit child disables stay disabled, comparison uses the same catalog/rules snapshot, staging does not deploy immediately.
- Migration: preview warnings, locked file, apply/undo, cancellation and recovery; saved profiles and cross-version FOMOD choices are not silently migrated.
- Graph: 50-node scope is understandable; truncated neighborhoods do not imply absent relationships; evidence labels remain legible.
- Stability: launch associations are evidence, not causal quality scores.

Adapter follow-up: IGameAdapter is an initial compiled contract. Audit host call sites for discovery, executable, roots, save paths, dependencies and validation before advertising complete support for another game. Preserve generic fallback and MHW semantic gating. No external DLL loading or new game certification was completed.

## Continuity requirements

Push each meaningful checkpoint with plan, changes, exact evidence, unresolved questions and next action. Update this handoff and require the next agent to do the same. Never manually promote function verification booleans. Preserve AGENTS.md, continuity protocol and handoff manifest. Do not merge draft PR #1 or publish a release without authorization. Local Git history is synthetic: use the actual remote parent for API commits; never force-push to repair the mismatch.
