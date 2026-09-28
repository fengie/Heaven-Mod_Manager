# Multi-Agent Coordination

## Principle
Parallelism is useful only when ownership and integration state remain discoverable from the repository.

## Task selection
- Split work by independent boundaries, not arbitrary file counts.
- Avoid assigning two agents the same mutable surface unless one is explicitly reviewer/test-only.
- Record active ownership when collisions are plausible.
- Prefer support agents for research that can unblock one primary implementation boundary.

## Branch ownership
- One active owner per feature branch.
- Use descriptive branch names that identify role and boundary.
- Do not treat branch names or old prompt hashes as canonical state.
- Commit coherent checkpoints and push recoverable progress incrementally.

## Required branch disposition states
An integration record should be able to classify work as:
- active;
- reviewed;
- merged;
- rejected;
- superseded;
- abandoned;
- unresolved.

For rejected/superseded/abandoned work, record why and whether any evidence remains useful.

## Integration discovery
An integration agent must:
1. fetch all remotes/branches;
2. establish current canonical main;
3. inspect branch tips and diffs relative to current main;
4. detect branches whose useful work is already canonical;
5. separate code/test changes from stale branch-local handoff snapshots;
6. resolve conflicts using current runtime/repository truth;
7. update the disposition ledger.

## Duplicate and stale work
- Prefer the newest independently verified implementation when two branches solve the same boundary.
- Salvage unique tests/research from superseded branches where useful.
- Never let an older branch overwrite newer canonical continuity or verification records merely because its merge is clean.

## Interrupted agents
A partial branch is evidence, not automatically valid work.
Recover by inspecting commits, working-tree state, tests, and intended acceptance criteria. Complete or revert the smallest boundary; do not assume the agent's final chat summary exists.

## Unavailable environments
Record machine/platform-dependent verification separately from code completion. Do not block unrelated research, but do not mark the boundary closed until required environment evidence exists.

## Integration ledger minimum
For each relevant branch record:
- branch/ref;
- base or comparison revision;
- purpose;
- files/types of changes;
- review result;
- integration commit if merged;
- reason if rejected/superseded/abandoned;
- verification performed;
- unresolved follow-up.

A fresh integration agent should be able to answer, from the repository alone: what work exists, what was reviewed, what was merged, what was rejected, what was abandoned, and what remains unresolved.
