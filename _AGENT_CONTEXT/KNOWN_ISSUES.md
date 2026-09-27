# Known issues / limitations

1. **Windows release confirmation is still required for the repaired source.** The two preserved historical Windows runs reached 13 PASS / 12 FAIL, then 24 PASS / 1 FAIL. The four trace gaps from the second run were already fixed in the supplied ZIP. The current repair revision builds cleanly and passes 158 tests plus 11 self-test checks on Linux, but WPF interaction, Windows locks, and publishing were not exercised here. See `AUDIT-2026-09-27.md`.

2. **`verified=true` means the source body survived the project's required verification pipeline, not that a dedicated unit test executed every possible branch.** The runtime scope/error observation helps diagnostics, but it is not code-coverage proof.

3. **First-chance exception observation is intentionally broader than fatal failure.** `ERROR-CHECK` can represent an exception intentionally caught/recovered by a lower layer. Do not automatically convert it into a user-visible failure.

4. **Anonymous lambdas/compiler-generated bodies are fingerprinted through their containing explicit body rather than assigned separate unstable IDs.** Explicit accessor bodies are independently inventoried; generated auto-accessors have no source body.

5. **The tracer itself is exempt from entry tracing to avoid recursion.** Keep this exemption narrowly scoped to `MasterDebugLog` in `src/MhwModManager.Core/MasterDebugLog.cs`.

6. **Presentation architecture is still comparatively concentrated.** `MainWindowViewModel.cs` remains large. Avoid adding filesystem/domain behavior directly to WPF rows/viewmodels when an application/backend service can own it.

7. **Universal-game support is not a completely pure plugin architecture yet.** MHW-specific compatibility/enhanced-semantic branches remain in portions of Core/Filesystem. Do not remove them casually; migrate deliberately behind game capabilities/adapters if refactoring.

8. **Function-level known-good is not proof of every environmental combination.** The full verification pipeline still runs; the boolean cache primarily avoids forcing unchanged bodies back through the changed/new instrumentation gate.

9. **Explicit call-site coverage is syntactic, not a whole-program call graph.** Invocation/object-construction sites are counted inside explicit source bodies. Compiler-generated calls (for example implicit disposal/state-machine mechanics/property accessors without source bodies) are protected diagnostically by the containing runtime scope rather than assigned separate stable call-site IDs.

10. **Full first-chance stack logging is intentionally off by default.** Set `MHW_FIRST_CHANCE_DETAIL=1` only when detailed per-throw records are needed; active scopes still count/record observed exceptions without it.

11. **Stage-cache reuse is exact-input only.** `stage-status.json` is not a blanket waiver. Any change to a project, its transitive project references, common build inputs, SDK, OS, or architecture causes a cache miss and reruns that stage. `Build-Release.ps1` intentionally remains a full release gate.

## Verification closure still needs one final Windows rerun

The second Windows run proved all compile/analyzer/test/self-test stages green but
failed the function scan solely because four changed functions lacked entry traces.
Those four gaps are fixed, and the subsequent repair audit adds further verified
changes. Run Windows verification for the exact current source before promoting
the 23 changed/new fingerprints. Syntax entry checks are not semantic call-graph
proof. Missing-blob recapture restores absent files; corrupt existing blobs remain
subject to deployment integrity checks rather than being silently trusted or repaired.
