# Reconciliation & Continuity Audit — 2026-09-29

## Canonical snapshot

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Audited canonical source before this continuity checkpoint: `fb3fb7ea5c5e4b133cea96f1c52dd9f4a3df327f`
- Release identity verified from `VERSION.txt`, `Directory.Build.props`, README, and CHANGELOG: **8.8.7**
- Last fully closed hosted verification already recorded in continuity: source `5abe40304dfcb48f96e750bd7da3d0075315625b`, run `36541891969`
- Evidence rule: verification is exact-SHA scoped; historical results are not silently extended to later product commits.

The audit began while several owners were integrating concurrently. Whenever `main` moved, stale reconciliation branches/PRs were abandoned instead of being forced over newer canonical work. The local Heaven command bridge reached its monthly execution quota during the checkpoint, so repository writes used the authorized GitHub path. No unexecuted local check is reported as passing.

## Canonical changes observed during reconciliation

- v8.8.6 profile containment and responsive UI integration was already canonical.
- Agent Control 0.5.1 liveness/machine policy reached main.
- Agent Control reliability verification coverage reached main.
- Agent Control security/authorization hardening reached main.
- Updater cross-session single-writer ownership reached main and advanced the shipped release identity to v8.8.7.
- Updater installed-client E2E runner/filter and release-starvation follow-ups also reached main.
- PR #95 was closed unmerged as a superseded release-metadata snapshot.
- Conflicted reconciliation PRs #107 and #109 were closed rather than forced over newer canonical history.

## Open PR snapshot

At the start of this direct-main continuity checkpoint, the open non-reconciliation PR set was: #110 Record Agent Control security authorization closure; #105 Agent Control: operator UI and CLI visibility/controls; #100 Complete federated Agent Manager dashboard contract.

Treat that as a timestamped snapshot, not permanent truth. Re-query before integration.

## Branch and PR disposition rules

- An open PR whose implementation has already landed on main is a cleanup/supersession candidate, not a reason to replay its stale branch.
- Agent Control registry/dashboard/operator lanes overlap `federated-registry.mjs`, package metadata, and/or public UI. Settle the registry contract first, then re-diff dependents against the new main.
- Never merge the highly divergent Heaven bridge recovery history wholesale; it contains generated queue/controller state. Salvage only narrow reviewed logic.
- Preserve newer canonical behavior and exact verification provenance over stale continuity snapshots.

## Integration constraints

1. Re-fetch exact `origin/main` before every merge/rebase/cherry-pick and immediately before pushing main.
2. Product owners retain current-main sync, conflict resolution, verification, merge/push, and remote-main confirmation responsibility.
3. Reconciliation provides Git truth and semantic ordering; it is not the routine merger for all owner branches.
4. Distinguish implementation, executed tests, local integration, remote-main push, and shipped/release evidence.
5. Retire superseded PR/branch state when safe, but never trade branch cleanup for loss of unique work.

## Verification limitation for this checkpoint

This is a continuity-only repository correction. JSON structures are parsed during publication and remote file contents are re-read afterward. The repository's local handoff/negative-fixture scripts were not executed in this checkpoint because the authorized Heaven command quota was exhausted; no pass is claimed for them.

