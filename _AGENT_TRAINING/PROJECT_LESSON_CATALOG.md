# Portable Project-Lesson Catalog

This is the product-agnostic catalog of reusable engineering lessons promoted from real project work. It is intentionally safe to copy into future repositories: keep product names, machine names, versions, hashes, and transient state in project-local evidence instead.

## Continuous promotion contract

A meaningful incident is not fully closed when the immediate symptom is fixed. For every bug, regression, false completion, process/agent failure, release failure, coordination failure, user correction, or durable workflow improvement:

1. identify root cause and the violated invariant;
2. preserve project-local evidence/precedent;
3. decide whether the failure class is reusable beyond the project;
4. if reusable, update the generic trainer and provenance in the same engineering cycle;
5. strengthen tests, analyzers, prompts, gates, or automation so the lesson is enforced;
6. if the same class escapes again, strengthen the prevention control itself.

## Portable lessons

1. **Structural code moves require verification re-audit.** Behavior-preserving moves/extractions can invalidate instrumentation, fingerprints, coverage, registration, or tracing.
2. **Search does not prove caller closure.** For removed/relocated shared contracts, use the compiler, linker, schema validator, generated-client build, or other authoritative closure mechanism.
3. **Failed system/native operations may already have mutated state.** Characterize real failure postconditions and recovery state; never assume exception = no side effect.
4. **Lexical containment is not physical containment.** Path normalization/prefix checks do not account for links, reparses, mounts, aliases, or topology changes.
5. **Restartable migrations need ownership and convergence.** Verify pre-existing artifacts, scope destructive reset to owned state, report cleanup truthfully, and prove retry convergence.
6. **Sanitize at the export boundary.** Shareable diagnostics need centralized export-time redaction plus canary tests even if individual producers also redact.
7. **Entity retirement must close semantic references.** Retire live references in configuration, caches, text keys, manifests, and external indexes while preserving explicitly historical evidence.
8. **In-progress work must be invisible until commit-on-success.** Stage imports/downloads/generation outside discovery/publication namespaces or prove an equivalent visibility protocol.
9. **Automated diagnosis must validate controls before durable blame.** Require a valid negative control and positive condition under comparable provenance; ambiguous evidence stays inconclusive.
10. **Authorization/containment checks must precede the protected mutation.** A check after a write/delete/create/publish is detection, not prevention.
11. **Cleanup failure must not replace the primary outcome.** Preserve cancellation/primary error and report cleanup failure separately.
12. **Immutable stores need independent byte ownership.** Point-in-time digest verification is insufficient when mutable aliases still share the same bytes.
13. **Recovery takeover requires orphan proof or exclusivity.** Incomplete durable state does not prove the previous writer is dead.
14. **Lock scope must match mutation authority.** Synchronization must cover every supported process/session/account capable of mutating the shared target.
15. **Prefer the narrowest purpose-built execution capability.** Discover purpose-built plugins/tools/skills before generic shell, remote-control, browser, UI, or API fallbacks.
16. **Capability discovery is runtime-specific.** Re-read the task and discover/load the capabilities actually available in the current session; require evidence before declaring one unavailable.
17. **A control channel needs an independent recovery owner.** Critical bridges/workers need a supervisor and local liveness/progress signal that do not depend on the channel being repaired.
18. **Control-path failure is not host-offline evidence.** Diagnose the narrowest failed layer and require independent host evidence before claiming the host/service is offline.
19. **Recovery redundancy must cross failure domains.** Two recovery owners sharing one scheduler/principal/runtime/config dependency are not truly independent.
20. **Validation instances are not canonical installed/runtime identity.** Verify user-facing state through the canonical launcher plus executable/process/artifact identity; clean up temporary validation instances.
21. **Escaped bugs require defect-class closure.** Fix root cause, add regression coverage, inspect sibling occurrences, and mechanically enforce durable invariants when practical.
22. **Swarm completion requires an orphaned-work sweep.** Inventory failed/stale/disconnected workers, leases, branches, worktrees, partial artifacts, and unfinished verification; dispatch recovery owners before declaring completion.
23. **Implemented and delivered are different states.** If project policy requires immediate release/deploy after gates, completion includes publication and post-publication identity/health verification.
24. **Exact identity beats labels.** Folder names, shortcut names, UI titles, branch names, cached status, and chat summaries are hints; verify canonical revision/build/path/hash/process/ref identity.

## Future-project rule

At each meaningful checkpoint, compare project-local learned rules/incidents with this catalog and the deeper trainer documents. Every active reusable project lesson must be represented in generic doctrine, explicitly classified project-specific, or tracked as pending promotion with an owner.
