# GLOBAL GIT DIRECTIVE — CANONICAL DELIVERY

`main` is the canonical integration target. Git mechanics exist to preserve correctness and concurrent work, not to create ceremony.

## Before mutation

Refresh remote state when possible and establish exact `origin/main`, current head/worktree status, relevant PRs/branches, and ownership/leases for the mutable boundary. Preserve unexplained dirty or unique work.

Use a temporary branch/worktree when isolation, branch protection, review, concurrent work, or rollback safety warrants it. Do not create a new branch or PR merely because a template says every task needs one.

## Ownership

One primary owner controls a mutable boundary. Reuse safe compatible existing work rather than creating numbered retry branches. Never overwrite another owner's changes or force-push shared/canonical history.

## Coherent changes

Prefer small, self-contained commits/change sets that keep the repository buildable and include the tests or documentation needed to understand the behavior. Separate unrelated refactors from feature/bug changes when doing so materially improves review or rollback.

## Delivery

The owner of a completed change owns delivery unless an external gate or explicit handoff says otherwise:

1. inspect the final diff;
2. run focused verification;
3. refresh `origin/main`;
4. reconcile without discarding newer canonical or unique concurrent work;
5. rerun checks invalidated by reconciliation;
6. satisfy required review/status/protection gates on the exact candidate;
7. integrate into `main` using the repository-appropriate merge method;
8. push;
9. refresh and verify remote `main` contains the intended tree/behavior;
10. safely retire temporary branch/worktree/PR state when no unique work remains.

A commit being reachable from `main` is not sufficient if conflict resolution or later changes dropped the intended tree delta.

A task is not complete merely because a branch was pushed or a PR opened. If delivery is externally blocked, preserve the exact branch/head, attempted operation/error, completed verification, and external action required; report PARTIAL/BLOCKED rather than DONE.

## Races and conflicts

Refresh again immediately before integrating/pushing `main`. If canonical state moved, reconcile and rerun only the checks affected by that reconciliation.

Resolve conflicts semantically. Do not prefer “ours” or “theirs” by age/name alone. Stale branches must not replace newer governance, security, release, updater, packaging, or continuity state.

## Branch cleanup

Delete a temporary branch only after proving its useful content/evidence is integrated, intentionally superseded, or otherwise preserved. Branch count is not a correctness goal.

## Progress visibility / patch version coordination

Every meaningful integrated change set updates the root `README.md`, `CHANGELOG.md`, and patch component in `VERSION.txt`, plus synchronized canonical version metadata. One integrated change set gets one patch increment. Evidence-only attestation/publication for that same change set stays on the same patch.

Version bumps coordinate with current `main`; do not overwrite a newer version from concurrent work.

## Release boundary

Product/user-facing release requirements remain in `_AGENT_TRAINING/CI_RELEASE_ENGINEERING.md` and the task-relevant repository policy reference. Integration evidence is not publication evidence.
