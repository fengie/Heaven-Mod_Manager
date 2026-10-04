# v8.8.95 responsive Auto Populate planning — canonical state

v8.8.95 carries forward the verified v8.8.94 planner, storage, updater, conflict/dependency, filesystem, and release-safety baseline and adds only the measured #735 Dispatcher-responsiveness slice.

## Behavior

- v8.8.94's bounded sparse Auto Populate planner semantics remain unchanged: provably isolated libraries require one mandatory final full planner pass, while interacting cases retain the authoritative fallback.
- Auto Populate now runs only the pure `AutoPopulateService.BuildAsync` phase on background work so CPU-heavy planning does not monopolize the WPF Dispatcher.
- Preferred mod IDs are captured before background execution; `RunBusy` remains the foreground-operation gate.
- Row staging and `Changed` remain Dispatcher-owned, timeline evidence remains unchanged, cancellation is propagated, final dependency validation remains mandatory, and Apply still performs the authoritative current-state re-plan immediately before game-file mutation.
- Behavioral STA/WPF regression coverage proves a queued Dispatcher callback can run while the background build is blocked and proves a pre-cancelled operation never enters the worker.
- Dependency, conflict, override, protected-anchor, persisted-state, filesystem, updater, and release-safety invariants remain unchanged.

## Verification boundary

The last fully closed hosted-Windows source before v8.8.95 is v8.8.94 `2311b658762c8fe51c21919926fdabc870147a0e`, which passed Windows Release Gate run `37230142789` with 0 failed checks. The subsequent evidence-only main `6cd39527e65458733a026bed7d3332443eab798d` does not change tested-source identity. Exact v8.8.95 source requires fresh post-integration Windows verification and release closure; v8.8.94 evidence must not be inherited as proof for changed v8.8.95 inputs.

## Remaining independent work

Issue #735 remains open after this slice. Service-level sparse planning and synthetic Dispatcher responsiveness are materially improved, but real installed enable/disable + Auto Populate UI/persistence timing, transitive recomputation evidence, and a durable latency/responsiveness acceptance budget are still required. Do not select another optimization except from the measured remaining dominant cost.

Other independent backlog, recovery, updater-trust, catalog-scale, and release-administration work remains governed by the live project plan and current issue/ownership state.

Every successor must preserve the permanent continuity constitution, exact-input verification, conflict/dependency/override safety, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and must propagate this continuity contract to the next successor.
