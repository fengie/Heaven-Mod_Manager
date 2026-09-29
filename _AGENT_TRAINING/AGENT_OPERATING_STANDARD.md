# Agent Operating Standard

## Before editing
1. Identify the canonical repository and source-of-truth branch.
2. Fetch remote state and inspect actual HEAD, working-tree status, recent history, and relevant branches.
3. Preserve unexplained local changes; never overwrite them for convenience.
4. Read project startup, continuity, architecture, and verification instructions.
5. Inspect the code, call sites, tests, and invariants directly related to the task.
6. State which facts are verified and which remain assumptions.

## While editing
- Control scope. Avoid unrelated refactoring and opportunistic cleanup.
- Prefer the smallest sufficient change.
- Inspect callers before changing contracts or removing APIs.
- Preserve behavior unless the task intentionally changes it.
- Patch root causes and invariants, not only visible symptoms.
- Keep destructive/stateful operations fail closed.
- Do not hide, swallow, or relabel failures to obtain a green result.
- Do not disable or weaken tests, analyzers, safety checks, or verification gates to make them pass.
- Keep changes interruption-safe and checkpoint meaningful progress.
- Update architecture/current-state/verification/knowledge docs when their truth changes.

## Verification integrity
- Never claim a command, test, build, release, or manual check ran when it did not.
- Never transfer verification from one revision or artifact to another without a valid identity/fingerprint rule.
- Passing tests do not prove untested assumptions.
- A real escaped bug should normally become: bug fix + regression test + reusable lesson review.
- When a platform-specific boundary matters, run the authoritative platform check or clearly leave it unverified.

## Availability and liveness evidence
- Separate **resource presence** from **control-channel health**. A failed transport, adapter, runner, heartbeat relay, or cached observation does not by itself prove the underlying resource is offline.
- Negative availability claims require current authoritative evidence. When a local/cached liveness record is missing or stale, refresh the authoritative source when safe and bounded before degrading status.
- If transport health is known-bad but resource presence is not independently known, use an explicit unknown/degraded state rather than `offline`.
- Keep read-only presence checks distinct from mutation/write-readiness checks; local dirtiness, lock state, or replica divergence may block writes without invalidating remote presence.
- Add regression tests for both evidence refresh and state mapping whenever a false offline/online classification escapes.

## Git and persistence
- Make coherent commits that can be reviewed and reverted independently.
- Push recoverable progress before expensive work can be lost.
- Do not force-rewrite shared canonical history unless repository policy explicitly permits it.
- Before final push, fetch again and reconcile concurrent remote work.
- Verify the remote contains the commit(s) you claim were pushed.

## Documentation checkpoint
At each meaningful checkpoint ask:
1. Did repository truth change?
2. Did architecture or an invariant change?
3. Did current state or verification status change?
4. Did we discover a failure mode?
5. Did next-step priority change?
6. Does another agent need new information?
7. Did this reveal reusable engineering knowledge?

Update only what changed. Avoid ceremonial documentation churn.

## Completion standard
A task is not complete until:
- the intended behavior or investigation is finished;
- relevant verification is complete or limitations are explicit;
- the working tree is understood;
- durable state is updated;
- intended commits are pushed according to repository policy;
- the next agent can resume safely;
- reusable lessons have been considered for promotion into company doctrine.

## Defect-class closure
When a bug, warning, failed integration, false completion claim, or process escape is found:
- identify why existing prevention did not catch it earlier;
- inspect sibling instances of the same defect pattern;
- add regression coverage that would have failed before the fix;
- strengthen analyzers/verifiers/gates when mechanically enforceable;
- update project precedent and promote reusable learning into the generic trainer;
- treat recurrence after an earlier rule as a prevention-control failure requiring stronger enforcement.

Do not mark complete with only a local symptom patch while the failure class remains open.
