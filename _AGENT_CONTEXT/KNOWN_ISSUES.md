# Known issues / limitations

1. **The v8.8.0 Windows baseline is closed, but the post-v8.8 architecture candidate is not yet verified.** Hosted run `36321128433` passed 25/25 for commit `5f6789af499fcc1afe6cb5d38244927bb02335fb`. Explain Why and the presentation-boundary refactor change production inputs and must pass a new exact-input Windows gate before being called green.

2. **`verified=true` means the source body survived the project's required verification pipeline, not that a dedicated unit test executed every possible branch.** The runtime scope/error observation helps diagnostics, but it is not code-coverage proof.

3. **First-chance exception observation is intentionally broader than fatal failure.** `ERROR-CHECK` can represent an exception intentionally caught/recovered by a lower layer. Do not automatically convert it into a user-visible failure.

4. **Anonymous lambdas/compiler-generated bodies are fingerprinted through their containing explicit body rather than assigned separate unstable IDs.** Explicit accessor bodies are independently inventoried; generated auto-accessors have no source body.

5. **The tracer itself is exempt from entry tracing to avoid recursion.** Keep this exemption narrowly scoped to `MasterDebugLog` in `src/MhwModManager.Core/MasterDebugLog.cs`.

6. **Presentation architecture is still comparatively concentrated, although the first extraction is underway.** Activity, Coverage, Import and Overlap/Explain now have feature partials; presentation SQL moved to `PresentationReadRepository` and archive filesystem work moved to `ArchiveImportService`. `MainWindowViewModel` and `MainWindow.xaml` are still large, so future work should turn stable seams into page view models/views instead of adding domain/filesystem behavior back into WPF.

7. **Universal-game support is not a completely pure plugin architecture yet.** MHW-specific compatibility/enhanced-semantic branches remain in portions of Core/Filesystem. Do not remove them casually; migrate deliberately behind game capabilities/adapters if refactoring.

8. **Function-level known-good is not proof of every environmental combination.** The full verification pipeline still runs; the boolean cache primarily avoids forcing unchanged bodies back through the changed/new instrumentation gate.

9. **Explicit call-site coverage is syntactic, not a whole-program call graph.** Invocation/object-construction sites are counted inside explicit source bodies. Compiler-generated calls (for example implicit disposal/state-machine mechanics/property accessors without source bodies) are protected diagnostically by the containing runtime scope rather than assigned separate stable call-site IDs.

10. **Full first-chance stack logging is intentionally off by default.** Set `MHW_FIRST_CHANCE_DETAIL=1` only when detailed per-throw records are needed; active scopes still count/record observed exceptions without it.

11. **Stage-cache reuse is exact-input only.** `stage-status.json` is not a blanket waiver. Any change to a project, its transitive project references, common build inputs, SDK, OS, or architecture causes a cache miss and reruns that stage. `Build-Release.ps1` intentionally remains a full release gate.

## Current verification boundary

The closed v8.8 baseline is authoritative only for its exact source. The architecture/Explain Why candidate changes production inputs and therefore awaits a new Windows Release Gate run. This milestone does not alter deployment integrity, CAS, journal, rollback/recovery, or path-safety semantics.
