# Auto-updater consumer release-trust hardening — 2026-09-28

## Canonical commit inspected

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Canonical HEAD at task selection: `a83dc6e047ccf98e896f10c25772df99b95426d1`
- Support branch: `agent/support-updater-release-consumer-trust-20260928`

This support slice is intentionally independent from `agent/auto-updater-publication-fix-20260928`, which already owns the hosted empty-release-inventory publication failure.

## Scope

Audit and narrowly harden the updater's GitHub Release discovery trust boundary.

Files changed:

- `src/MhwModManager.Updater/GitHubUpdateSource.cs`
- `src/MhwModManager.Updater/UpdateModels.cs`
- `tests/MhwModManager.IntegrationTests/UpdaterCoreTests.cs`
- this durable report

No publisher script, WPF lifetime behavior, updater apply/rollback logic, packaging, workflow, verification cache, or shared continuity snapshot is changed.

## Methodology

1. Re-established canonical `main`, recent commits, active PRs/branches, continuity rules, and updater checkpoint state.
2. Inspected the exact hosted Windows Release Gate failure for `a83dc6e...`.
3. Avoided the already-owned publication failure after finding `agent/auto-updater-publication-fix-20260928`.
4. Compared publisher trust requirements with `GitHubUpdateSource.FindLatestAsync`.
5. Checked current GitHub REST release schema and immutable-release documentation.
6. Added the smallest consumer-side policy enforcement and regression coverage.

Primary platform references:

- GitHub REST releases: https://docs.github.com/en/rest/releases/releases
- GitHub immutable releases: https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases

## Existing strengths

The current updater already:

- authenticates only to exact HTTPS `api.github.com`;
- excludes draft releases;
- parses only `updater-main-<build>` tags as updater builds;
- selects the highest eligible build newer than the installed build;
- validates the downloaded manifest and exact artifact size/hash;
- downloads release assets using the binary media type;
- has publisher-side checks that require an immutable release and exact assets.

## Confirmed finding — P1 release-consumer trust asymmetry

Before this support slice, the publisher required immutable releases, but the client DTO modeled only:

- `tag_name`
- `draft`
- `assets`

and release discovery filtered only `!Draft`.

Therefore a non-draft release named `updater-main-N` could be selected even when GitHub reported it as mutable or as a prerelease. A higher-numbered unsafe entry could outrank a lower immutable normal release because trust state was not part of selection.

This is a consumer-side policy gap, not evidence that such a release has already been published. At task time the repository had no published GitHub Releases because hosted run `36428542918` failed in the separate publication step.

GitHub's current REST release schema exposes both `prerelease` and `immutable`. GitHub documents immutable releases as locking assets and tags after publication. The repository's publisher already treats immutability as mandatory, so the consumer should enforce the same invariant rather than trusting tag shape alone.

## Narrow implementation

`GitHubReleaseDto` now models:

- `prerelease`
- `immutable`

`GitHubUpdateSource.FindLatestAsync` now considers only releases satisfying all three conditions before build ordering:

- not draft;
- not prerelease;
- immutable.

Unknown/omitted `immutable` deserializes to the default `false`, which is intentionally fail-closed.

## Regression coverage added

`UpdaterCoreTests` now makes ordinary release fixtures explicitly `immutable=true` and `prerelease=false`.

Two regressions were added:

1. a mutable build 13 and prerelease build 12 must not outrank a safe immutable normal build 11;
2. when every newer release is mutable or prerelease, discovery returns no candidate and must not request any release asset.

These tests are designed to fail against the pre-change client behavior.

## Things deliberately not changed

- No changes to `Publish-UpdaterRelease.ps1`; its current empty-inventory failure is owned by another active branch.
- No changes to release ordering/build-number policy.
- No signature/attestation verification was added.
- No WPF, helper, apply, rollback, staging, product ownership, or filesystem logic was changed.
- No global `CURRENT_REVISION`, `NEXT_STEPS`, `NEXT-AGENT-START-HERE`, or handoff manifest was edited because the active publication-fix branch is already changing that shared continuity surface. This report plus the PR are the isolated handoff artifact for this support lane.
- No Learned Rule was added: immutable-consumer release trust is already required by the canonical updater release/security audit and generic release doctrine; this slice enforces that existing invariant rather than introducing a new rule.

## Verification actually performed

Performed:

- canonical GitHub state/history/parallel-branch inspection;
- exact hosted run/job/step/log inspection for run `36428542918`;
- source and existing test-body inspection;
- current GitHub primary-documentation check for `draft`, `prerelease`, and `immutable` release fields;
- static diff review after editing.

Not yet performed in this chat environment:

- .NET compile;
- focused `UpdaterCoreTests`;
- full IntegrationTests;
- strict whole-solution build;
- `Test-AgentHandoff.ps1`;
- `Verify-Release.ps1`;
- `Build-Release.ps1`;
- hosted Windows Release Gate.

Do not convert the authored regression coverage into a PASS claim until the tests execute.

## Exact next verification commands

On an authorized Windows checkout of this branch:

```powershell
dotnet test tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~UpdaterCoreTests'
dotnet test tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj -c Release
dotnet build MhwModManager.sln -c Release -warnaserror
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AgentHandoff.ps1
```

If this slice is selected for integration, rebase/reconcile it after the active publication-fix branch lands, then run the repository's exact full verification/release gates on the resulting exact inputs.

## Parallel-agent integration notes

- `agent/auto-updater-publication-fix-20260928` owns the hosted empty-release-list crash. Preserve its publication repair and continuity updates.
- Open updater support PRs cover publication race, binary asset media type, WPF restart health args, and native replacement/recovery. This branch intentionally does not replace those scopes.
- Shared continuity files were intentionally left untouched to avoid stale support-branch snapshots colliding with the active main updater agent.
- If another branch independently implements consumer immutable/prerelease filtering before this PR integrates, compare regression strength and keep the more specialized coverage rather than duplicating production edits.

## Success / handoff

This support slice is ready for code review once focused Windows tests have been run. The successor must re-check canonical `main`, preserve the permanent continuity constitution and active Learned Rules, reconcile active updater publication work, and explicitly require its own successor to preserve and recursively propagate the same rules to the agent after them.

**Do not break the chain.**
