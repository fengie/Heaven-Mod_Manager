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
