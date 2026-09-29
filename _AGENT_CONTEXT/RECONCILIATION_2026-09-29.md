# Reconciliation & Continuity Audit — 2026-09-29

## Canonical snapshot

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Audited canonical base: `dd1411693e2da467ea5e1dfb5191b9793213ebdf`
- Release identity observed on main: `8.8.6`
- Last fully closed hosted verification: source `5abe40304dfcb48f96e750bd7da3d0075315625b`, run `36541891969`
- Evidence rule: historical hosted verification remains exact-source proof only; current main must earn fresh exact-SHA evidence.

The primary Heaven checkout contained unrelated uncommitted Agent Control work, so the audit used an isolated worktree for local inspection. The local command bridge then reached its monthly execution quota before the continuity commit could run; repository writes therefore used the authorized GitHub path. No local test result is inferred from that fallback.

## PR and branch disposition

### PR #95 — CLOSED / SUPERSEDED

Head `a68606bd8c36e89556f91cd58cd241ed9c97b4a7` on `integration/v8.8.6-release-convergence-20260929` was behind canonical main and contained release metadata already represented by newer v8.8.6 main. It was closed unmerged. The r2 release-convergence branch is likewise unsafe to replay wholesale because its stale tree drops newer updater-E2E/workflow/product changes.

### Active PRs re-queried after main advanced

- **#96 — liveness scheduler / machine policy:** head `61c7714822d56084f11b7b4b7838b827648b408e`; 13 ahead / 0 behind at audit; mergeable. Owns liveness/server/control-core behavior. Originating owner remains responsible for exact-main recheck, verification, and delivery.
- **#98 — updater cross-session ownership:** head `fcc30786b5230eac81094aa0d42c7efc0fa2214e`; 5 ahead / 5 behind; non-mergeable. Changes updater runtime, tests, and installed-client E2E workflow. Must be reconciled by updater/security owner; no blind merge.
- **#99 — Agent Control reliability stress:** head `972fe61422ea3e818370ca5354c4e7e9c5f75b6f`; 4 ahead / 2 behind. Test-only federation registry stress coverage. Reconcile after the registry contract it verifies is settled.
- **#100 — dashboard contract:** head `4a0ad53bfddee5fc7c4a2dd561fa8c80e6d25c49`; 1 ahead / 4 behind; non-mergeable. Overlaps federated-registry, package, UI, and dashboard tests.
- **#101 — federation registry reconciliation:** head `99a6c9aa889aabc0f6af22a4aaa13205c6495468`; 8 ahead / 11 behind. Owns the shared federated-registry contract and overlaps package/docs with other Agent Control PRs. Registry semantics should be reconciled before dependent dashboard/operator/test lanes.
- **#103 — operator UI/CLI:** head `8dc990d4ea455addefac8629a0cd024220188a6c`; 7 ahead / 5 behind. Overlaps registry/package/public UI with #100/#101; UI/CLI owner must rebase/reconcile rather than race shared files.

Historical branches that had no unique commits at the earlier audit include `release/v8.8.5-20260929`, `agent/updater-installed-client-e2e-harness-20260929`, and `agent/updater-existing-release-rest-ref-main-reconcile-20260929`. The older profile-containment and alternate updater-E2E branches are superseded by later canonical integrations. The highly divergent Heaven bridge recovery branch must never be merged wholesale because it includes generated queue/controller state; salvage only narrowly reviewed recovery logic.

## Integration ordering constraints

1. Re-fetch `origin/main` before every merge/rebase/cherry-pick and immediately before pushing main.
2. Preserve newer canonical product and continuity behavior over stale branch snapshots.
3. Keep updater PR #98 separate from Agent Control lanes.
4. Within Agent Control, settle the federation registry contract (#101) before merging dependent dashboard/operator/stress changes (#100/#103/#99); re-evaluate overlap after each integration.
5. Liveness PR #96 is a separate owner boundary but shares package/docs/server integration surfaces; its owner must re-check canonical main before delivery.
6. Do not use the Reconciliation role as a routine merger for product-owner branches. Owners retain validation and main-delivery responsibility.
7. Keep implementation, testing, local-main integration, remote-main push, and shipped/released evidence explicitly distinct.

## Continuity corrections

This checkpoint aligns the current version/lineage with v8.8.6, scopes the older hosted verification correctly, records PR #95 retirement and the live PR inventory, replaces stale PR #90/#95 guidance at the top of current-state/next-step documents, and makes this audit part of the handoff manifest.

No product behavior, test implementation, verification cache, or release artifact is introduced by this reconciliation checkpoint.
