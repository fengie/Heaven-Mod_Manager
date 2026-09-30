# Agent Operating Standard

This is the default programming behavior for every repository. Project rules may specialize it without duplicating it.

## Core loop

**Inspect → understand → choose the smallest coherent task → implement → test → verify → integrate → document → hand off.**

Do not substitute repeated planning, audits, or governance writing for implementation when the assignment calls for a change.

## Inspect and understand

Before editing:

- establish current canonical source, working state, ownership, and the acceptance criteria;
- inspect the surrounding architecture, callers, tests, data/control flow, and failure boundaries that actually constrain the change;
- distinguish observed facts from assumptions;
- reproduce a reported defect before fixing it when practical;
- identify the invariant or user-visible behavior that must change and the behavior that must remain stable.

Read context on demand. Do not require a full repository map or historical corpus when the task needs one local boundary.

## Implement

Prefer the smallest coherent change that solves the actual problem.

- Keep modules, classes, and functions focused.
- Preserve separation of concerns and avoid unnecessary coupling.
- Reuse an abstraction when reuse is real; do not create speculative frameworks for hypothetical reuse.
- Avoid unrelated refactors, speculative features, and compatibility layers with no demonstrated requirement.
- Make failure modes explicit. Stateful, destructive, security-sensitive, and recovery boundaries fail closed.
- Remove dead code when safe instead of accumulating permanent bypasses or duplicate implementations.
- Comments should explain intent, invariants, or surprising constraints, not restate code.
- Preserve public behavior unless the task intentionally changes it.

A bug fix should address the root cause, not merely the visible symptom.

## Test and verify

Verification is risk-calibrated, not ceremonial.

1. Run the narrowest useful check that directly exercises the changed behavior.
2. Add/update tests for behavior changes. An escaped bug should normally gain a regression that fails before the fix.
3. Broaden to compile, integration, end-to-end, platform, fault, concurrency, performance, or release checks when the changed boundary or repository policy requires them.
4. Stop repeating broad checks when no new change, failure, or risk justifies another run.

Verify meaningful postconditions, especially after failure or cancellation. Do not equate “an exception occurred” with correct recovery.

Never:
- claim a command or check ran when it did not;
- inherit green evidence across changed inputs without a proven equivalence rule;
- weaken tests, analyzers, warnings, authorization, or safety gates to get green;
- normalize flaky tests, warnings, ignored failures, or unexplained state as background noise.

## Review and integration

Read the complete diff before delivery. Check contract/caller impacts, failure paths, cleanup/cancellation, state transitions, tests, and documentation impact.

Keep changes reviewable and independently reversible where practical. Branches and PRs are coordination tools, not goals. Reconcile against fresh canonical state, preserve concurrent unique work, run affected checks on the reconciled candidate, then verify the canonical tree contains the intended result.

## Debugging and observability

Prefer evidence-producing debugging over guess-and-patch loops. Use logs, traces, focused assertions, reproductions, and state inspection that can distinguish hypotheses. Add observability only where it improves diagnosis or operation; avoid permanent debug noise.

## Delegation and context efficiency

Use another agent when the task is independent, bounded, and benefits from separate context or parallelism. Keep short or dependency-chained work with the owner. Never assign two unsynchronized agents to the same mutable boundary.

A delegation packet should contain: objective, boundary, relevant context, acceptance criterion, expected artifact/evidence, and non-goals. The owner synthesizes and verifies the returned work.

Senior agents should reserve context for architecture, hard reasoning, review, integration, and high-risk verification. Offload mechanical retrieval or repetitive independent work when useful, not by default.

## Documentation and durable knowledge

Update documentation when truth changed, not as a ritual after every command. Keep current state current and historical detail historical.

A newly discovered lesson does not automatically become a permanent rule. First check whether an existing invariant already covers it. Prefer strengthening that invariant or its mechanical enforcement. Record a new durable rule only for a material, reusable gap.

## Completion

A task is complete only when:

- the requested implementation or investigation is actually finished;
- relevant checks pass, or remaining verification limits are explicit;
- canonical/integration state is known;
- durable documentation reflects changed truth;
- unresolved risks/assumptions are explicit;
- a fresh successor can continue from the durable handoff without private chat history.

If blocked, preserve the smallest useful checkpoint and exact next action. Do not relabel partial work as done.
