# Generic Development Pipeline

Default flow:

discover → establish canonical truth → understand invariants → plan → implement → self-review → test → adversarial test → integrate → verify → update knowledge → commit → push → handoff

Not every task needs every stage. Select stages by risk and task type.

## Stage selection
**Research/audit only:** discover, canonical truth, inspect evidence, document findings, update knowledge, commit/push/handoff.
**Small local fix:** canonical truth, invariants, implement, focused tests, regression test, self-review, update knowledge, commit/push/handoff.
**Contract/API change:** add caller inventory, whole-project compile/schema validation, migration/compatibility checks.
**Filesystem/data migration/destructive change:** add adversarial/fault/recovery tests and rollback validation.
**Concurrency/async change:** add race, cancellation, retry, and idempotency checks.
**Release change:** add artifact reproduction, signing/integrity, update/rollback, and post-release validation.
**High-risk or cross-cutting change:** require independent review or stress testing before integration.

## Discover
Locate source, tests, docs, CI, release logic, historical incidents, active branches, and unresolved risks.

## Establish canonical truth
Fetch; identify canonical branch/revision; inspect status/history/diffs; reconcile prompt assumptions against the repository.

## Understand invariants
Write down what must remain true before changing code. Include ownership, transactions, ordering, identity, scope, failure state, compatibility, and recovery.

## Plan
Choose the smallest boundary that can be independently verified. Define non-goals and acceptance criteria.

## Implement
Make the narrow change. Preserve unrelated behavior. Add observability where failure would otherwise be opaque.

## Self-review
Read the complete diff. Check callers, failure paths, cancellation, cleanup, and documentation impact.

## Test
Run the most direct tests first, then authoritative compile/integration/platform checks needed by the boundary.

## Adversarial test
Challenge assumptions with malformed state, partial failure, stale state, concurrency, interruption, retries, permissions, missing dependencies, and large inputs where relevant.

## Integrate
Re-fetch remote state, resolve conflicts deliberately, and avoid merging stale handoff snapshots or superseded work blindly.

## Verify
Bind evidence to exact source/artifact identity. Verify the user-facing artifact or deployed state, not merely an intermediate build.

## Update knowledge
Ask whether the task produced a reusable lesson. Generalize and record it before history disappears.

## Commit and push
Create coherent checkpoints, push according to policy, refetch, and verify the remote state.

## Handoff
Record canonical revision, what changed, what was verified, remaining risks/assumptions, active work, and the next best independent action.
