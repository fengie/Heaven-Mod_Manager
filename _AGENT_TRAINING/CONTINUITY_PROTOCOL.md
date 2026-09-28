# Generic Continuity Protocol

Continuity is an engineering property, not administrative cleanup.

## Minimum durable state
A project should make these discoverable:
- canonical repository/branch/revision;
- current architecture and important invariants;
- current implementation state;
- active work and branch ownership;
- unresolved risks and known failures;
- verification status tied to exact inputs;
- unverified assumptions and environment-specific checks still required;
- abandoned/superseded approaches when recurrence is likely;
- next best independently verifiable actions.

Keep this lean. Record what a replacement engineer actually needs.

## Checkpoint rule
After each meaningful implementation or investigation checkpoint, update durable state if architecture/contracts, current task status, verification evidence, failure modes, branch/integration state, next priorities, or reusable engineering knowledge changed.

## Interruption safety
Design work so an unexpected stop leaves:
1. a coherent commit or clearly described working tree;
2. enough evidence to identify what ran and what failed;
3. no hidden destructive-cleanup assumptions;
4. an explicit next safe action.

Push meaningful checkpoints when possible.

## Recovery procedure
1. Re-establish canonical remote truth.
2. Inspect local status, branches, commits, and incomplete artifacts.
3. Read current continuity state and recent relevant diffs.
4. Reconstruct the intended boundary and acceptance criteria.
5. Determine whether partial work is safe to complete, must be reverted, or needs independent verification.
6. Resume only the smallest unresolved boundary.
7. Repair continuity records as soon as truth is known.

## Stale prompts and long pauses
Treat old hashes, statuses, branch names, and next-step claims as hints. Verify them against current repository state before acting.

## Environment loss
Separate implementation state from verification state. If a required computer/tool is unavailable, record exactly which check remains and why; do not invent equivalent evidence.

## Handoff standard
A handoff should state:
- exact starting/current revision;
- what changed and why;
- tests/checks actually run;
- what is not verified;
- branch/PR/commit status;
- risks/assumptions;
- next independent action.

The next engineer must be able to continue without private chat history.

## Continuity must reduce work
Do not turn continuity into a second codebase. Prefer compact machine-readable state plus focused human context. Remove stale duplication and link to authoritative artifacts.

## Knowledge handoff
Before completion, ask whether the task created a reusable lesson. Promote qualifying lessons into the company trainer and keep transient project facts in the project layer.
