# Updater installed-client E2E harness — 2026-09-29

Status: **candidate harness; hosted execution not yet proven**

## Closed publication facts

Automatic updater publication remains closed at exact production source `5abe40304dfcb48f96e750bd7da3d0075315625b`.

- Hosted Windows Release Gate: `36541891969`, 25/25 PASS.
- Immutable target release: `updater-main-61`.
- Target build: `61`.
- Target ZIP SHA-256: `C31CAA1F5CBA65EBF9D526B02BA718F807EBC18A7E7554269E3754D710F86420`.
- Target release-manifest SHA-256: `4021E5303263A42255E80B40DA6C9C9349B055FB87BB970C2A71DA149DB4D2BE`.
- Prior immutable baseline for the success path: `updater-main-60`, build `60`, source `ffd218b6ad4e9f4fea4b143d266712a6fa17a285`.

Do not reopen publication/C12/retry-tag work without contradictory direct evidence.

## Why a hosted harness is needed

The first disposable installed-client execution on `heaven`
(`manager-20260929-updater-e2e-build61-a1`) was BLOCKED before release acquisition because that machine could not reach `api.github.com:443` and had no updater/private-release credential. Its cached integration-test build passed, but it produced no installed-client success or rollback evidence.

A follow-up local-Codex harness assignment
(`manager-20260929-updater-e2e-harness-a1`) did not implement anything because local Codex usage was exhausted. That is an agent-resource failure, not an updater result.

## Candidate harness

This branch adds:

- `.github/workflows/updater-installed-client-e2e.yml`
- `tests/MhwModManager.IntegrationTests/UpdaterInstalledClientE2ETests.cs`

The hosted workflow uses a disposable GitHub Windows runner and a repository-scoped read token. It must never touch a real user/game installation.

### Scenario A — real immutable build 60 -> 61

The test downloads and verifies the real immutable build-60 package, creates a disposable packaged installation, seeds user-owned `Mods`, `State`, and an unknown file, configures an isolated fake generic-game profile, and launches the **real packaged build-60 application**. Its own production background updater must discover and stage the latest immutable release, prepare/copy the build-60 helper, shut down the old client, apply the update, and restart the packaged target.

The discovered candidate must be exactly build 61 / source
`5abe40304dfcb48f96e750bd7da3d0075315625b`.

The harness observes the production transaction files emitted by that real client/helper chain and requires the restarted packaged build-61 application to write the production startup-health acknowledgement. The test requires:

- the old packaged client exits cleanly after handing off;
- installed build/source exactly 61 / target source SHA;
- exact health token/build/source evidence;
- journal phase `Confirmed`;
- unchanged SHA-256 for all seeded user/unknown sentinels.

A disposable generic-game profile is created under a temporary manager home so the real packaged WPF application can complete normal startup without depending on a real MHW installation.

### Scenario B — real-package deterministic rollback

A separate disposable build-60 install uses the real staged build-61 payload with the existing `UpdateInstaller` deterministic fault seam. Failure is injected after the first live owned-file mutation.

The test requires:

- journal phase `RolledBack`;
- every previous owned product byte plus `product-files.json` and `release-install.json` restored to its exact pre-update SHA-256;
- installed build/source restored to build 60 / its exact source SHA;
- seeded `Mods`, `State`, and unknown-file SHA-256 unchanged;
- any target-only product paths absent after rollback.

The workflow also reruns the existing focused rollback regression guards that explicitly cover restoration/removal around replacement and stale/new product files.

## Completion rule

Do **not** mark the updater fully closed merely because this harness exists or compiles.

The remaining updater boundary closes only when the hosted workflow runs the real packaged scenario and rollback scenario successfully and produces reviewable evidence. If the real packaged application cannot complete startup on a GitHub-hosted Windows runner, preserve that failure as an explicit acceptance gap; do not weaken updater safety or fake the health acknowledgement to make CI green.
