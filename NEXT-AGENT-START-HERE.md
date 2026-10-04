# v8.8.95 responsive Auto Populate planning — handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Primary open performance issue: #735

## Candidate behavior

- v8.8.94 already bounded sparse Auto Populate planner rebuilds and removed the duplicate pre-Apply analysis.
- v8.8.95 keeps those planner/dependency/conflict semantics unchanged and moves only the pure `AutoPopulateService.BuildAsync` call off the WPF Dispatcher.
- Preferred IDs are captured before background execution; `RunBusy` remains the foreground gate; row staging/`Changed` remains Dispatcher-owned; Apply still re-plans and validates current state before game-file mutation.
- A focused STA/WPF behavioral regression must prove Dispatcher responsiveness during blocked background work; a second regression must prove pre-cancelled execution never enters the worker.

## Verification boundary

The last fully released source remains v8.8.94 `2311b658762c8fe51c21919926fdabc870147a0e`, with Windows Release Gate run `37230142789` passing. Canonical evidence-only main before this reconciled candidate is `6cd39527e65458733a026bed7d3332443eab798d`. The updater-evidence security hardening from #771 is preserved.

Any v8.8.95 source candidate requires fresh exact-head verification. Do not inherit v8.8.94 release evidence as proof for the new source.

## #735 acceptance state and unresolved risk

- Sparse service-level Auto Populate is materially improved versus the pre-v8.8.94 baseline: the recorded 256×100 sparse median improved from about 9169 ms to about 282 ms with one full planner pass.
- The measured remaining responsiveness bottleneck is Dispatcher occupancy during `BuildAsync`; prior STA/WPF probes recorded roughly 376–458 ms max callback stalls direct versus roughly 0.6–5.8 ms with experimental offload.
- This v8.8.95 slice addresses that measured bottleneck only. It does **not** by itself close #735.
- Remaining acceptance requires real installed enable/disable + Auto Populate UI/persistence timing, transitive recomputation evidence, and a durable latency/responsiveness budget.

## Execution/offload note

When a callable Heaven/Agent Control execution surface is unavailable in the current session, use exact-head repository/Windows CI evidence and do not fabricate local execution or installed-client timing.

## Next action

1. Run exact-head focused/full Windows verification for the v8.8.95 candidate and fix any regression without weakening gates.
2. Reconcile fresh canonical main, rerun invalidated checks, merge only an exact-green candidate, and verify remote-main tree/readback.
3. Run the strongest available installed #735 enable/disable + Auto Populate timing probe. Close #735 only if every acceptance criterion is evidenced; otherwise preserve the measured remaining blocker and smallest next slice.
4. Re-scan open PRs/issues only after #735 is terminal or its remaining live-runtime acceptance is durably handed to a healthy capable owner.

## Successor obligation

As the successor, you must preserve and propagate this continuity contract to the agent after you, and require that agent to pass it to the next successor. Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW main; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and all safety invariants above. Before finishing, update the repository handoff with the same explicit recursive requirement.

**Do not break the chain.**
