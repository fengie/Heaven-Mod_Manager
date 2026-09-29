# GLOBAL GIT DIRECTIVE — FINISHED WORK MUST LAND ON `main`

This rule is mandatory for every development agent, sub-agent, swarm, manager, integration agent, programmer, reviewer, and automation working on this repository.

## PRIMARY RULE

`main` is the canonical integration branch.

Task branches are temporary workspaces only.

When you finish your assigned implementation, **you are responsible for getting your own completed work into `main` immediately**.

Do NOT finish work and leave it sitting on a branch for another agent to discover, reconcile, or merge later.

Do NOT create unnecessary integration debt.

The agent that creates the change owns its integration until the change is successfully present on remote `main`.

---

# REQUIRED COMPLETION FLOW

After completing your assigned task:

1. Finish the implementation completely.
2. Run the relevant tests, checks, linting, builds, or validation.
3. Commit your completed work.
4. Fetch the latest remote repository state.
5. Synchronize with the newest `main`.
6. Rebase or otherwise reconcile your task branch against current `main`.
7. Resolve conflicts caused by your work yourself whenever reasonably possible.
8. Re-run relevant validation after reconciliation.
9. Merge or fast-forward the completed work into `main`.
10. Push `main` to the remote repository.
11. Fetch/inspect the remote again and verify that the expected commit/change is actually present on remote `main`.
12. If you used a temporary task branch, confirm it contains no unique commits absent from remote `main`.
13. Delete the completed local task branch.
14. Delete the corresponding remote task branch if it exists and is no longer needed.
15. Prune stale remote-tracking refs and verify the cleanup.
16. Only then may the task be considered complete.

The desired lifecycle is:

`task → implement → test → sync main → reconcile → merge into main → push main → verify remote main → delete completed branch → done`

NOT:

`task → implement → push random branch → tell someone else to merge it`

---

# BRANCH POLICY

Branches are allowed while active work is in progress.

They are NOT intended to become permanent parking lots for completed work.

Use short-lived branches when isolation is useful, such as:

`agent/<task>`
`fix/<issue>`
`feature/<feature>`

Once the task is complete and integrated:

- the branch must contain no unique completed work that is absent from remote `main`;
- delete the completed local task branch;
- delete the corresponding remote task branch when it exists and is no longer needed;
- prune stale remote-tracking refs after deletion;
- do not continuously generate successor branches for trivial work;
- do not leave dozens of completed branches for an integration agent to clean up later.

Branch deletion is part of task completion, not optional housekeeping.

If the work can safely be performed directly against current `main` without disrupting another active change, that is acceptable too.

The important requirement is that **completed validated work reaches `main` immediately**, and temporary branches are removed after that integration is verified.

---

# BRANCH DELETION SAFETY

Delete aggressively only after proving the work is safe on canonical remote `main`.

Before deleting any branch:

1. Fetch the latest remote state.
2. Verify the expected change is present on `origin/main`.
3. Verify the branch has no unique/unmerged commits that still need preservation.
4. Confirm the branch is not the currently checked-out branch.
5. Confirm the branch is not `main`, the repository default branch, a protected branch, an active release branch, or a branch explicitly retained by repository policy or the user.
6. If all checks pass, delete the local branch and its remote counterpart.

Never delete a branch merely because its name looks stale or because a PR is closed. Preserve any branch that still contains unique work until that work is explicitly integrated, superseded, archived, or otherwise dispositioned.

For routine completed task branches, the default is deletion immediately after verified integration.

---

# OWN YOUR MERGE

Do not say:

- "My branch is ready for integration."
- "Someone should merge this."
- "Integration agent can pick this up."
- "PR created; task complete."
- "Changes are pushed to my branch."
- "Waiting for another agent to reconcile it."

Those are intermediate states, not completion.

Instead, continue working until the change is safely integrated into `main`, unless an actual blocker prevents it.

You created the change, so you normally have the best context to resolve conflicts involving that change.

Do not dump that work onto another agent unnecessarily.

---

# CONCURRENT AGENT SAFETY

Multiple agents may be working simultaneously.

Therefore, **never assume the version of `main` you started from is still current**.

Immediately before integrating:

```bash
git fetch origin
git switch main
git pull --ff-only origin main
```

Then reconcile your completed work against that latest state.

If using a task branch, update/rebase it against current `main` before final integration.

Example:

```bash
git fetch origin
git switch <task-branch>
git rebase origin/main
```

Resolve conflicts carefully and run validation again.

Then integrate:

```bash
git switch main
git pull --ff-only origin main
git merge <task-branch>
```

Run the appropriate final verification and then:

```bash
git push origin main
```

Finally verify remote state:

```bash
git fetch origin
git status
git log origin/main --oneline -n 10
```

Exact commands may vary with repository policy, but the outcome must be the same.

---

# RACE CONDITION RULE

Another agent may push to `main` between your synchronization and your push.

If your push is rejected because remote `main` changed:

**this is not a reason to abandon integration.**

Instead:

1. Fetch the new remote `main`.
2. Reconcile your work with it.
3. Resolve any conflicts.
4. Re-run affected tests.
5. Attempt the integration again.
6. Push.
7. Verify.

Repeat until your completed work is actually present on remote `main`, unless a genuine blocker exists.

Never force-push over someone else's work just to win the race.

---

# CONFLICT POLICY

When conflicts occur:

- understand both sides before choosing a resolution;
- preserve valid work already merged by other agents;
- preserve your intended functionality;
- combine compatible changes where appropriate;
- do not blindly choose "ours" or "theirs";
- rerun affected tests after resolving conflicts.

If another agent's recently merged work changes assumptions your implementation depended on, adapt your implementation to current `main`.

Current `main` is authoritative.

---

# PULL REQUESTS

A PR is a collaboration/review mechanism, not an excuse to leave completed work stranded.

If repository rules require PRs:

- create/update the PR;
- perform required checks/reviews;
- merge it once requirements are satisfied;
- confirm the resulting commit is on `main`;
- delete the completed task branch locally and remotely once safety checks pass.

Do not mark the underlying task complete merely because a PR exists.

If no PR is required, do not manufacture unnecessary PR bureaucracy simply to move code between branches.

---

# MANAGER / SWARM RESPONSIBILITY

Managers must enforce this behavior across their agents.

When dispatching work, include:

> You own implementation through integration. Your task is not complete until the validated change is present on remote `main`.

Managers should actively detect:

- finished branches not merged into `main`;
- stale PRs containing completed work;
- duplicated branches containing equivalent changes;
- agents handing integration responsibilities to unrelated agents;
- branches diverging significantly from `main`;
- work reported as "done" when it exists only locally or on a side branch;
- merged task branches that were never deleted.

When found, instruct the originating agent to integrate its work immediately whenever possible.

---

# INTEGRATION AGENT ROLE

The integration agent should NOT become a janitor for every ordinary completed task.

Normal agents are expected to merge their own finished work.

The integration agent should focus on exceptional repository-wide duties such as:

- reconciling genuinely overlapping work from multiple agents;
- resolving complex cross-feature conflicts;
- validating release-wide compatibility;
- identifying abandoned/stale work;
- performing repository-wide stabilization;
- coordinating release boundaries;
- cleaning up historical branch debt.

If every programmer leaves finished work on a branch and expects the integration agent to merge it, the workflow is broken.

---

# DO NOT CLAIM COMPLETION EARLY

A task is NOT complete merely because:

- the code was written;
- tests passed on a side branch;
- a commit exists;
- the branch was pushed;
- a PR was opened;
- another agent was informed;
- an integration request was created.

For repository changes, the default definition of **DONE** is:

> The implementation is validated, reconciled with current `main`, merged into `main`, pushed to the remote, verified as present on remote `main`, and any completed temporary task branch has been safely deleted.

---

# BLOCKERS

Only stop before integration for a genuine blocker, such as:

- required permissions are unavailable;
- mandatory checks cannot be satisfied;
- repository policy prohibits the merge;
- unresolved conflicting requirements require a human decision;
- integrating would knowingly break `main`;
- another active migration requires explicit coordination.

If blocked, report precisely:

- what is finished;
- what branch/commit contains it;
- what prevented integration;
- what commands/checks were performed;
- what exact action is needed to unblock it.

Do not use "another agent should merge it" as a generic blocker.

---

# KEEP `main` HEALTHY

Fast integration does NOT mean careless integration.

Never merge knowingly broken code merely to obey this directive.

Before landing work:

- validate the affected functionality;
- inspect unexpected repository changes;
- avoid committing unrelated files;
- protect secrets and credentials;
- respect current architectural decisions;
- ensure generated/build artifacts follow repository policy.

The goal is:

**small, tested, rapidly integrated changes on a healthy `main`.**

Not:

**reckless pushes to `main`.**

---

# VERSION / RELEASE COORDINATION

If your completed work requires a version bump, manifest change, changelog entry, generated metadata update, packaging change, or release-state update that falls within your assignment, update it as part of the same integration rather than leaving the repository inconsistent.

Do not create situations where application code is updated on one branch while version/release metadata waits elsewhere.

---

# STANDING ORDER

From this point forward, every agent should operate under this assumption:

> `main` is where completed work lives.

> Branches exist to isolate work while it is being performed, not to store finished work.

> The agent responsible for the change is also responsible for synchronizing, reconciling, merging, pushing, and verifying that change on `main`.

> Do not create downstream cleanup work for other agents when you are capable of completing the integration yourself.

> Once a temporary task branch is safely integrated and verified on remote `main`, delete it locally and remotely.

This directive overrides any older workflow instruction that encourages agents to leave ordinary completed work on long-lived feature branches for a separate integration agent to merge later.