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
18. **Control-path failure is not host-offline evidence.** Diagnose the narrowest failed layer, refresh stale cached liveness from an authoritative source before negative availability claims when safe, and model resource presence separately from transport/write readiness; absent independent host evidence, degraded transport means presence unknown rather than offline.
19. **Recovery redundancy must cross failure domains.** Two recovery owners sharing one scheduler/principal/runtime/config dependency are not truly independent.
20. **Validation instances are not canonical installed/runtime identity.** Verify user-facing state through the canonical launcher plus executable/process/artifact identity; clean up temporary validation instances.
21. **Escaped bugs require defect-class closure.** Fix root cause, add regression coverage, inspect sibling occurrences, and mechanically enforce durable invariants when practical.
22. **Swarm completion requires an orphaned-work sweep.** Inventory failed/stale/disconnected workers, leases, branches, worktrees, partial artifacts, and unfinished verification; dispatch recovery owners before declaring completion.
23. **Implemented and delivered are different states.** If project policy requires immediate release/deploy after gates, completion includes publication and post-publication identity/health verification.
24. **Exact identity beats labels.** Folder names, shortcut names, UI titles, branch names, cached status, and chat summaries are hints; verify canonical revision/build/path/hash/process/ref identity.
25. **Primary data surfaces get first claim on constrained space.** On dense desktop/table/library pages, keep the core data workspace star/fill-sized and prevent auxiliary filters/actions/help from consuming extra vertical rows at supported window sizes; prefer horizontal overflow, trimming, tooltips, or compact controls and verify the real constrained/windowed path.
26. **Sequential durable IDs need collision-safe allocation.** Concurrent writers must not infer the same next integer from stale state; serialize/reserve allocation or use collision-resistant IDs, and make integration validate uniqueness before canonicalization.
27. **A blocked direct path should trigger research and wraparound, not premature surrender.** Prove the limitation with current evidence, research authoritative alternatives, and when the underlying outcome remains technically achievable, use or build the narrowest authorized adapter/wrapper/bridge/local replacement; verify the original acceptance criteria end to end and never bypass legitimate auth, consent, safety, or governance boundaries.

28. **Structural regression tests must evolve atomically with intentional structure changes.** When a UI/schema/layout refactor deliberately replaces the structure a regression asserts, update the test in the same change to encode the new durable invariant and run the full relevant suite before merge.

29. **Long-running autonomy needs progress supervision plus an independent controller supervisor.** A live PID/heartbeat proves only process/channel liveness, not useful forward progress. Track progress separately, preserve durable work before one-for-one replacement, gate replacement on execution capacity, persist pending recovery across restarts, rate-limit restart storms, and place the controller itself under a recovery owner outside its process/failure domain.


30. **Programmatic source edits require emitted-source validation.** When an agent generates or rewrites source code, validate the final bytes as target-language source—not merely the edit template or diff—and run the narrowest native parser/compiler/build before handoff when execution is available. Escaped control characters, quoting artifacts, encoding damage, or templating residues are a recurring defect class; a source-producing workflow should not advertise readiness until syntax closure is proven.

31. **Secret egress and artifact acquisition require complete identity binding.** A secret-bearing transport must bind credential emission to the canonical intended origin even when alternate endpoints are injectable for tests. Any direct-download/install/acquisition resolver must validate the complete provider + game + item + artifact identity before returning a usable artifact; partial identity matches are not authorization.

32. **Authoritative external budgets must gate fan-out between calls.** When a provider returns quota, retry, circuit, or remaining-capacity state, sequential/batched callers must re-observe that state before dispatching the next request. A successful final-budget response is not permission to exceed the reported budget on the next iteration.

33. **Strict analyzer closure includes internal concrete-type choices.** In warnings-as-errors repositories, private fields should not retain a broader collection/interface type when construction always materializes one concrete type and polymorphism is not part of the design. Review new internal collections for CA1859-style regressions before integration handoff; analyzer failure cascades should be traced to the first compile diagnostic rather than misclassified as many downstream test failures.

34. **Exact-head green is mandatory merge evidence.** A cancelled, pending, superseded, or older-SHA run is not verification. Immediately before canonical integration, bind the merge decision to the exact candidate SHA and a successful required gate; replacement branches must explicitly inherit every known defect fix from the lineage they supersede.
35. **Pinned test-framework APIs are compile-time contracts.** Do not assume assertion return values from older framework versions; under xUnit v3, retrieve nullable values separately when assertions return void, assert them, and continue with null-safe access under the pinned analyzer profile.
36. **New provider/capability getters must satisfy verifier tracing.** Where function coverage is fingerprinted, use explicit getter blocks with `MasterDebugLog.BeginMethod()` as the first executable statement rather than expression-bodied public members.

## Future-project rule

At each meaningful checkpoint, compare project-local learned rules/incidents with this catalog and the deeper trainer documents. Every active reusable project lesson must be represented in generic doctrine, explicitly classified project-specific, or tracked as pending promotion with an owner.


### PowerShell startup/recovery scripts need syntax-class gates
- Validate changed PowerShell startup, installer, watchdog, recovery, and generated scripts with the native PowerShell parser before registering or executing them.
- Do not rely only on structural/string tests: PowerShell has lexical traps such as `$name:` inside double-quoted strings, where punctuation can be parsed as part of a scoped-variable token. Use `${name}:` or explicit formatting.
- When one such defect escapes, add a source-level regression for the entire syntax class so non-Windows CI can still prevent recurrence.


## Capability declarations are executable boundaries

When integrating an external provider, treat declared capabilities and compliance metadata as executable boundaries. Resolver/output code must not produce a stronger action than the provider contract permits. In particular, a browser-assisted flow must point to a provider-controlled user-facing page, not a direct asset endpoint discovered in metadata. Pair the declaration with an exact-target regression so policy drift and implementation drift fail together.

## CI supply-chain and runner trust

Treat CI Actions as executable dependencies: pin them to immutable commit SHAs and update them through reviewed automation. Persistent self-hosted runners are trusted machines, not disposable sandboxes; never allocate them to fork PR code, avoid persisted checkout credentials, and prefer ephemeral isolation for less-trusted execution. Keep workflow permissions explicit and least-privilege, and make direct/transitive dependency vulnerability auditing a build invariant. See `_AGENT_TRAINING/SECURITY_SUPPLY_CHAIN.md`.
37. **Constructor/member assignments must be unambiguous after source transplants.** Rebase/transplant edits can accidentally turn an intended local-to-field assignment into field self-assignment when names collide or an intermediate local disappears. Prefer direct assignments from validated parameters/results (for example, `this.baseUri = NormalizeBaseUri(apiBaseUri)`), and let exact-head warnings-as-errors compilation gate integration. When compilation fails, fix the first analyzer/compiler error before interpreting downstream missing-artifact/test failures.
