# Reconciliation & Continuity Audit — 2026-09-29

## Canonical snapshot

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Initial detailed branch audit began before main advanced; latest canonical observation for this checkpoint is `38772a9bcf8547402de7f98ada0740d7a6aa070f`.
- Release identity observed on main: `8.8.6`.
- Last fully closed hosted verification: source `5abe40304dfcb48f96e750bd7da3d0075315625b`, run `36541891969`.
- Evidence rule: historical hosted verification remains exact-source proof only.

The primary Heaven checkout contained unrelated Agent Control work, so local inspection used an isolated worktree. The local command bridge later hit its monthly execution quota; repository writes used the authorized GitHub path. No unexecuted local test is reported as passing.

## Completed reconciliation actions

- PR #95 (release v8.8.6 metadata branch) was closed unmerged as superseded after proving it lagged newer canonical v8.8.6 work.
- Agent Control 0.5.1 liveness/machine policy is now on canonical main at `17ac640fbc15622341ea1fddcee4d6817d73ade7`.
- Updater cross-session ownership implementation/tests/gate are now on main through `ef1dd35c805857750471eb0eb004ee186312e4d0`, `dd1411693e2da467ea5e1dfb5191b9793213ebdf`, and `dfc02ee1506842b5769a165358b9e12f84c8625f`.
- A one-shot obsolete-branch cleanup landed at `38772a9bcf8547402de7f98ada0740d7a6aa070f`; do not infer branch deletion beyond refs actually re-queried.

## Open PR snapshot after those integrations

- **#98 — updater cross-session ownership:** still open and diverged even though the core ownership implementation/tests/gate are now canonical. Its updater runtime blob matches main, while its workflow/test blobs differ after newer main fixes. Treat as stale/cleanup unless a fresh semantic diff proves unique needed work; do not merge wholesale.
- **#101 — federation registry reconciliation:** active, diverged, and touches shared registry/package/docs plus continuity. Registry owner must reconcile to current main first.
- **#100 — dashboard contract:** active, diverged/non-mergeable at audit, overlapping federated registry/package/public UI.
- **#105 — operator UI/CLI:** active replacement/final lane, overlapping registry/package/public UI; reconcile after registry semantics settle.
- **#104 — security/authorization:** active, diverged/non-mergeable at audit, touching bridge runner/server safety. Reconcile against integrated liveness/server changes before delivery.
- **#99 — reliability stress:** active test/evidence lane; reconcile its federation-registry tests after the registry contract is settled.

Older release/updater support branches with no unique commits were classified already-integrated or superseded. The highly divergent Heaven bridge recovery branch must never be merged wholesale because it includes generated queue/controller state; salvage only narrow reviewed recovery logic.

## Integration ordering constraints

1. Re-fetch `origin/main` immediately before every merge/rebase/cherry-pick and before pushing main.
2. Preserve newer canonical behavior over stale release/continuity snapshots.
3. Keep updater PR #98 separate from Agent Control work and retire it if fresh comparison confirms no unique required behavior.
4. Within Agent Control, settle federation registry semantics (#101) before dashboard/operator/reliability dependents (#100/#105/#99).
5. Reconcile security/authorization #104 against the already-integrated liveness/server state before delivery.
6. Product owners retain validation and main-delivery responsibility; Reconciliation provides Git truth and semantic ordering rather than blindly merging their branches.
7. Distinguish implementation, executed tests, local integration, remote-main push, and shipped/released verification evidence.

## Continuity corrections

This checkpoint aligns current version/lineage with v8.8.6, scopes the older hosted verification correctly, records PR #95 retirement, records concurrent integration of liveness and updater ownership, updates the live PR inventory, and adds this audit to the handoff manifest.

No product behavior, verification cache, or release artifact is introduced by this continuity-only reconciliation checkpoint.
