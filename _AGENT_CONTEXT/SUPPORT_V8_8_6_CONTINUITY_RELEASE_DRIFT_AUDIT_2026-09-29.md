# Support audit — v8.8.6 continuity / release-state drift — 2026-09-29

## Canonical SHA inspected

`37ea6658d6004fee3d13cb51f27005638cae74bc` on `fengie/mhw-mods/main` immediately before this artifact was created.

During the investigation, canonical `main` advanced from `ad3de9e78232fe9f66dace188c3e31dd4ced7a7c` to `37ea6658d6004fee3d13cb51f27005638cae74bc`. That live movement is evidence of concurrent integration work and is the reason this support lane does **not** directly rewrite shared release/continuity files owned by reconciliation/release roles.

## Scope

Bounded support investigation of version identity, continuity truth, exact-head verification provenance, and stale release-queue state after the v8.8.6 integration sequence. No production behavior is modified by this artifact.

## Methodology

- Resolved canonical `main` through the GitHub ref/compare surface.
- Inspected recent canonical commits around the v8.8.6 integration.
- Read `VERSION.txt`, `Directory.Build.props`, `README.md`, `CHANGELOG.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/README_FIRST.md`, and `_AGENT_CONTEXT/NEXT_STEPS.md` at the inspected canonical head.
- Checked GitHub workflow/status records for the then-current exact `main` heads.
- Re-queried PR #95 rather than relying on its earlier open state.

## Findings

### F1 — Shipped version identity is 8.8.6

At the inspected canonical head:

- `VERSION.txt` is `8.8.6`.
- `Directory.Build.props` has `<Version>8.8.6</Version>` and informational version `8.8.6-profile-containment-ui-polish`.
- `README.md` and `CHANGELOG.md` lead with v8.8.6 material.

This establishes the current release-facing identity as v8.8.6.

**Severity / impact:** informational baseline.

### F2 — Canonical continuity metadata is materially stale and internally inconsistent

At the same canonical head, `_AGENT_CONTEXT/CURRENT_REVISION.json` says `currentVersion: 8.8.6` but still carries:

- `status: v8.8.4-hosted-published-e2e-pending`;
- `verificationAppliesToCommit`, `lastClosedVerificationCommit`, and `candidateSourceCommit` = `5abe40304dfcb48f96e750bd7da3d0075315625b`;
- a verification note centered on updater build 61 / v8.8.4 publication;
- a next milestone that still treats the disposable build-61 installed-client E2E as unfinished.

`_AGENT_CONTEXT/README_FIRST.md` still begins with `READ THIS FIRST — MHW Manual Mod Manager v8.8.4` and presents v8.8.4 support reconciliation as the newest top-level state.

`_AGENT_CONTEXT/NEXT_STEPS.md` still declares the build-61 v8.8.4 updater E2E as the current critical path even though canonical history now contains the installed-client updater E2E harness plus later v8.8.6 source/release integration.

**Severity / impact:** high for agent coordination. A new agent following continuity literally can select obsolete work, attribute verification to the wrong source, or regress release state.

### F3 — Exact-current-head verification is not established by the GitHub evidence checked here

For `ad3de9e78232fe9f66dace188c3e31dd4ced7a7c`, GitHub returned no workflow runs and no combined commit statuses when queried.

Canonical `main` then advanced to `37ea6658d6004fee3d13cb51f27005638cae74bc`. The new head contains production source changes in `src/MhwModManager.App/App.xaml.cs`, and no exact-head verification result was observed during this bounded investigation before artifact creation.

This does **not** prove tests fail. It means exact-current-head success must not be claimed from the older v8.8.4 / `5abe403...` evidence.

**Severity / impact:** high for release provenance; unknown for runtime correctness.

### F4 — Product source continued changing after the explicit v8.8.6 metadata completion commit

The v8.8.6 metadata completion commit was `0fb5cdaf2c5eed8a4058a94e70a8ff0ecf791f79`.

Later canonical commits included:

- `ad3de9e78232fe9f66dace188c3e31dd4ced7a7c` — production trace instrumentation for `InstalledCountLabel`;
- `37ea6658d6004fee3d13cb51f27005638cae74bc` — production generic-profile workspace containment in `App.xaml.cs`.

Repository policy requires shipped source changes to remain aligned with version/release/documentation identity. Because release/integration ownership is active, this audit does not choose a version number. The release owner should establish one final exact source candidate, then ensure version/docs/manifests describe that exact candidate before publication.

**Severity / impact:** medium-high release-consistency risk.

### F5 — PR #95 is no longer an integration candidate

PR #95 (`Release v8.8.6 profile containment and responsive UX integration`) was re-queried and is now **closed, unmerged**. Its recorded base/head are older than current canonical `main`.

Do not reopen or blindly merge it merely because earlier observations showed it open. Any still-unique content must be compared against current `main` first.

**Severity / impact:** medium queue/reconciliation risk.

## Missing verification

This support investigation did **not** run the Windows release gate, solution tests, updater E2E, or local scripts. No claim is made that current `main` passes those gates.

The exact-current-head release owner / verification owner should run the repository-required gates after the source/version/continuity candidate stops moving.

## Recommended implementation / reconciliation

1. Re-read the latest `origin/main` immediately before editing shared continuity.
2. Let the active Reconciliation / Integration & Release owner reconcile `CURRENT_REVISION.json`, `README_FIRST.md`, `NEXT_STEPS.md`, verification provenance, and release metadata as one exact-head truth update.
3. Establish the exact release candidate SHA after all intended v8.8.6 (or successor-version) production commits are present.
4. Run exact-candidate focused/full verification required by repository policy.
5. Persist only evidence that names that exact candidate; never copy the older `5abe403...` hosted result forward as if it verified newer source.
6. Re-query closed/stale PRs and branches before harvesting anything; compare unique content rather than replaying branch-local continuity snapshots.

## Exclusions

- No production code review beyond the release/continuity implications above.
- No attempt to own the Reconciliation, Integration & Release, Verification/Reliability, UI, profile-containment, or updater implementation boundaries.
- No version bump chosen here.
- No claim about local workstation state because this investigation used the connected GitHub repository surface.

## Unresolved questions

- Which exact post-`0fb5cdaf...` product commits are intended to ship together in the next immutable release artifact?
- Has an exact-head Windows gate run started or completed outside the GitHub records observed during this investigation?
- Has the installed-client E2E harness already produced durable success/rollback evidence that has not yet been reflected in the canonical continuity files?

## Successor handoff

Treat this document as evidence of drift at the SHA above, not as a replacement for current canonical truth. Re-fetch `main` first. If another owner has already reconciled these fields, do not duplicate or overwrite that newer work.

Preserve the permanent continuity constitution and pass it to the next agent.
