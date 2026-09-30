# Architecture / implementation decisions

## 1. Do not wrap every call site in ad-hoc try/catch

The user's goal is exhaustive error checking, but blindly wrapping every invocation would change exception propagation, make cancellation/retry behavior dangerous, inflate the codebase, and make debugging worse. v8.8 instead puts a verification/error-observation boundary around each changed/new production executable body. First-chance exceptions from nested calls are associated with active scopes while normal exception semantics remain untouched.

## 2. Verification cache is fingerprint-based, not name-only

A boolean can be reused only when the stable callable/body ID and trivia-free Roslyn token fingerprint still match. Comments/formatting do not invalidate known-good state; executable syntax changes do.

## 3. Cache promotion is fail-closed

A scan may report a body as changed/new, but it cannot promote it by itself. Promotion happens only after the required build/analyzer/test/self-test gates succeed. Build release promotion was deliberately placed after the Windows publish path too. Failed runs preserve the old cache.

## 4. v8.7 exact source is embedded only as verification evidence

A file hash alone would make one version-string edit invalidate every body in a large source file. The trusted v8.7 source snapshot lets the verifier compare bodies individually. Runtime code never reads this archive.

## 5. Anonymous lambdas do not receive independent stable IDs

Lambdas/compiler-generated bodies are included in their containing body's token fingerprint. Independent anonymous IDs based on source position would create false invalidations when code merely moves. Explicit accessors are inventoried independently; generated auto-accessors have no source body.

## 6. Tracing implementation is recursion-exempt

Methods/accessors inside `MasterDebugLog` cannot safely require `MasterDebugLog.BeginMethod()` at entry because doing so recursively calls the tracer. The exemption is path/type-specific. `ProcessDebug` in the same file is not exempt and is traced.

## 7. Do not redesign the backend in this release

Earlier source/research review found the transactional backend boundaries materially healthier than the presentation boundary. v8.8 therefore avoids a simultaneous architecture rewrite. Large WPF/ViewModel decomposition is a separate future change and should not be mixed into verification infrastructure unless necessary.

## 8. Full first-chance logging is opt-in, observation is always on

First-chance notifications occur for handled as well as unhandled managed throws. Every active function scope still receives the observation, but full exception stack writes require `MHW_FIRST_CHANCE_DETAIL=1`. This keeps error checking broad without turning ordinary recoverable exceptions into a permanent hot-path logging cost.

## 9. Continuity is mechanically gated

The project now contains `_AGENT_CONTEXT/handoff-manifest.json`, `CONTINUITY_PROTOCOL.md`, `scripts/testing/Test-AgentHandoff.ps1`, and a source-handoff packager. Future agents must preserve/update these. Verification should fail if the continuity payload disappears, because the user explicitly requires project knowledge to travel with every future source zip.

## 10. Generated build output is outside source verification

`obj`/`bin` generated C# is not authored product source and is not stable across SDK/build states. Function verification explicitly excludes it; otherwise repeat verification after a build could report generated methods as new untraced code.

## 11. Repair revision keeps exact checked evidence

Keep version 8.8.0/common build props unchanged. Preserve the six unaffected Windows stage hashes; mark only exact unchanged functions checked. Linux builds/tests are evidence, not Windows release promotion. Use executable verifier/cache regressions instead of only source-text assertions.

## 2026-09-27 post-v8.8 architecture decisions

1. **Do not combine WPF lifetime/DI framework migration with the first architecture split.** Keep the existing explicit composition root for now; extract cohesive responsibilities first. Reconsider Generic Host/DI after page/service boundaries are stable.
2. **Treat the first database refactor as a boundary migration, not a transaction rewrite.** Read-only presentation projections moved to `PresentationReadRepository`; deployment write/transaction infrastructure stays in `ManagerDatabase` until each write domain can be moved without changing atomicity.
3. **Explain Why must never become a second resolver.** It replays the configured `DeploymentPlanner` and presents the exact `ConflictDecision`, manifest state, provider metadata and evidence already produced by the engine.
4. **Split the giant WPF class incrementally while preserving bindings.** Activity, Coverage, Import and Overlap/Explain orchestration moved to partial feature files as a low-risk seam. Independent page view models remain the next structural step, not a prerequisite for shipping Explain Why.
5. **No FOMOD or enhanced-adapter expansion in this milestone.** Architecture coherence and verification take priority.

