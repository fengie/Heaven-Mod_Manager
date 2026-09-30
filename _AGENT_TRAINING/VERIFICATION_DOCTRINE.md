# Verification Doctrine

## Core rule
**Passing tests does not equal proven correctness.** Tests prove only the behavior and states they actually exercise.

## Evidence ladder
Use the strongest applicable evidence:
- static inspection for simple structure;
- unit tests for local behavior;
- property/invariant tests for broad input classes;
- integration tests for subsystem contracts;
- end-to-end tests for user workflows;
- regression tests for escaped bugs;
- fault injection for partial failure;
- concurrency tests for races and ordering;
- stress/scale tests for limits and performance;
- platform-specific tests for OS/runtime behavior;
- manual verification for surfaces automation cannot faithfully cover;
- artifact/deployment verification for what users actually receive.

## Dangerous boundaries to exercise
When relevant, include malformed/corrupted state, partial completion, stale state, concurrent operations, repeated operations, idempotency/retry, cancellation/interruption, rollback/recovery, permissions/locks, missing dependencies, unexpected filesystem topology, large inputs/resource pressure, and platform-specific native behavior.

## Test meaningful postconditions
Do not stop at “an exception occurred.” Assert the state that matters after failure: bytes, rows, journal status, ownership, visible catalog state, retryability, retained recovery evidence, and external side effects.

## Escaped bug rule
A real escaped bug should normally produce one engineering unit:
1. root-cause fix;
2. regression test that fails on the old behavior;
3. review for a reusable lesson or missing invariant.

## Contract closure
Search is not authoritative for caller closure. Use the compiler, schema validator, linker, runtime contract test, or other true authority when available.

## Exact-source semantics
Verification must identify source revision/fingerprint, relevant configuration/environment, command or test set, result, and artifact identity when applicable. Do not inherit green evidence across changed inputs unless a documented cache/fingerprint system proves equivalence.

## Independent verification
High-risk changes should receive a review/test/stress pass whose author is not forced to accept the implementer's assumptions.

## Honest limitations
If the authoritative environment or dependency is unavailable, say what remains unverified. Do not substitute a weaker check and present it as equivalent.

## Defect-class and sibling closure
An escaped failure is evidence about the verification system, not only the product. After reproducing the defect, inspect sibling uses of the risky pattern, add a regression that fails on old behavior, prefer invariant/property/analyzer enforcement for broad classes, and verify the actual risk surface. A green suite after a one-line repair is insufficient if equivalent sibling defects remain discoverable.
## Strict analyzer rerun semantics
When a verification pipeline performs a relaxed build before a strict warning-as-error or analyzer build, the strict phase must force actual compiler/analyzer execution. A successful incremental no-op is not strict verification: up-to-date outputs can suppress diagnostics that were visible in the relaxed phase. Use a rebuild/no-incremental mechanism (or otherwise prove analyzer execution), and regression-test that invariant in the verifier itself.



## Performance optimization protocol
- Measure representative Release builds and distinguish cold startup, warm startup, and time to meaningful interactivity. Do not claim a speedup from source inspection alone.
- Profile the critical path before broad rewrites. Prefer removing, narrowing, caching, or demand-loading optional work over adding concurrency blindly.
- Keep network access, broad filesystem scans, artwork/preview generation, metadata enrichment, and rarely used feature initialization off the first-interactive-frame path unless correctness requires them there.
- Preserve safety and consistency barriers on the blocking path. Recovery, migrations, transactional invariants, and correctness-critical validation must not be deferred merely to improve a startup number.
- For repeated reads, fetch only the columns/state the caller needs and avoid wide joins/object materialization when a narrow query is sufficient.
- Treat parallelism as a measured optimization: prove independence and verify that added concurrency does not increase SSD contention, UI-thread pressure, lock contention, allocation rate, or peak memory.
- Protect important performance boundaries with benchmarks or structural regression checks, then remeasure after the change. Record exact source revision, workload, environment, latency, allocation/memory evidence, and any remaining measurement gap.

### Final assembled browser-script parse gate

When HTML embeds or generates JavaScript, verification must parse the **final assembled script source** after all merges/transforms. Structural substring tests are insufficient: duplicate top-level declarations, truncated generated source, or merge artifacts can make the browser reject the entire script before initialization. Add a parser-only regression that does not execute browser APIs and make parser failure integration-blocking.



## Multi-way inferred decision proof
When an algorithm chooses one winner from three or more candidates using pairwise heuristics, do not assume the pairwise relation is transitive and do not use a sequential tournament as correctness evidence. Verify the global invariant directly: one candidate must satisfy the authoritative precedence/compatibility predicate against every other eligible candidate. If any pair is incomparable, the relation cycles, or no unique complete dominator exists, fail closed or require an explicit choice. Add a non-transitive/cyclic regression fixture so deterministic iteration order cannot masquerade as correctness. Apply this pattern to package/provider selection, override resolution, preference arbitration, reconciliation, and other N-way inferred winner systems.

## Canonical tree proof after integration
A commit being reachable from canonical history is not proof that its content survived integration. Merge commits, conflict resolutions, record-only branch-tip preservation, or later reverts can retain ancestry while dropping the intended tree delta.

For integration verification, prove both:
1. **provenance:** which implementation/test commits were considered; and
2. **postcondition:** the canonical post-merge tree actually contains the intended paths/content/behavior.

Use direct canonical-tree inspection, exact post-merge diffs, or an equivalent invariant. Never close a task, delete its branch, or inherit verification merely from ancestry/merge-base evidence.

