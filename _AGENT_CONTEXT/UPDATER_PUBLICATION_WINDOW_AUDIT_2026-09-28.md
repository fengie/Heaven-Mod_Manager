# Updater publication-window safety audit — 2026-09-28

## Status

Documentation/support-only audit. No production source, tests, workflow files, verification caches, or release assets are changed by this checkpoint.

Canonical `main` inspected immediately before branch creation:

- `a83dc6e047ccf98e896f10c25772df99b95426d1` — `Require immutable updater releases`
- branch: `agent/support-updater-publication-window-audit-20260928`

This audit follows the permanent continuity constitution and active Learned Rules. The successor must preserve and recursively propagate the same system to the agent after them.

## Why this lane was selected

The active updater implementation has already closed or assigned the major runtime/package concerns. Open PR #49 specifically added a second `origin/main` fetch immediately before `gh release create` to reduce stale-main publication risk.

Canonical `main` then advanced through the updater integration and now contains that recheck plus an immutability check.

The remaining gap is narrower and distinct:

> the last main recheck is still placed before a multi-request release creation/upload/publish sequence, so it does not bound the actual client-visible publication point.

This is independently useful, does not require changing updater runtime code, and does not duplicate the already-landed pre-`gh release create` recheck.

## Source inspected

Primary current source:

- `scripts/Publish-UpdaterRelease.ps1`
- `scripts/Test-UpdaterReleasePolicy.ps1`
- `.github/workflows/windows-release-gate.yml`
- `_AGENT_CONTEXT/CURRENT_REVISION.json`
- `_AGENT_CONTEXT/NEXT_STEPS.md`
- `NEXT-AGENT-START-HERE.md`

Parallel work inspected through current open PR inventory, especially updater PRs #41-#49 and historical updater release-safety PR #32.

## External authoritative behavior checked

GitHub's current CLI documentation states that when `gh release create` is given assets, the CLI performs separate API operations to:

1. create the release as a draft;
2. upload the assets;
3. publish the release.

GitHub also states that immutable-release protections are enforced only after publication and recommends the explicit draft -> attach assets -> publish workflow.

The current CLI supports `isImmutable` in `gh release list --json` and `gh release view --json`.

The CLI also supports publishing a previously created draft with:

`gh release edit <tag> --draft=false`

These facts were checked against the current official GitHub CLI / GitHub Docs on 2026-09-28.

## Existing strengths

The current publisher already has several strong fail-closed properties:

- repository identity is pinned to `fengie/mhw-mods`;
- manifest source SHA and build number must match exact workflow inputs;
- updater channel must be `main`;
- package verification runs before publication;
- an existing updater release is refused unless it is non-draft, immutable, tag-consistent, and asset-consistent;
- release asset count/name/size/server SHA-256 digest are verified;
- latest previous updater build and ancestry are checked;
- evidence-only changes do not generate new updater releases;
- monotonic build behavior is enforced;
- `origin/main` is re-fetched before release creation;
- post-publication tag/source and asset identity are re-verified;
- a newly published release is rejected if GitHub reports it as non-immutable.

The hosted workflow also runs the repository verifier, release build, and publication-policy test before the publication step on the exact workflow SHA.

## Confirmed finding — P1 release correctness / residual stale-main publication window

### Current sequence

The publisher currently does:

1. policy calculation;
2. `git fetch origin main`;
3. compare `origin/main` to `ExpectedSourceSha`;
4. call one `gh release create <tag> <artifact> <manifest> ...` command;
5. inspect the now-published release.

The source comment describes step 3 as a recheck immediately before the irreversible client-visible publication.

### Why that is not the actual publication boundary

Official GitHub CLI behavior makes step 4 a compound sequence: draft creation, one or more asset uploads, then publication.

Therefore a push to `main` can happen after the step-3 recheck but while the CLI is uploading the ZIP/manifest. The old workflow can still publish its verified-but-no-longer-current source because `--target $ExpectedSourceSha` intentionally targets that exact commit.

The earlier recheck narrows the race compared with C11a, but it does not eliminate it and can leave a window proportional to release asset upload time.

### Impact

This does not imply that the artifact itself is unverified or tampered.

It violates the stricter publication policy currently documented by the updater work: only the eligible exact current `main` should become client-visible.

If `main` advances during upload (for example for a correctness or security repair), clients can briefly discover an older updater build after it is no longer canonical current main.

Because immutable publication locks the release/tag/assets after publication, cleanup has additional consequences: GitHub documents that after deleting an immutable release, its tag name cannot be reused.

## Verification gap — current regression does not exercise this boundary

`scripts/Test-UpdaterReleasePolicy.ps1` tests the pure release-decision helper:

- tag parsing;
- relevant-path classification;
- stale-main decision at policy-evaluation time;
- evidence-only suppression;
- product-change publication;
- same-build suppression;
- older-build rejection.

It does not execute or mock `Publish-UpdaterRelease.ps1`'s external sequencing.

Therefore it cannot prove where the final main recheck occurs relative to draft creation, asset upload, and publish.

The hosted workflow will execute the real publisher, which is strong end-to-end evidence for a successful exact run, but a normal green run does not inject a concurrent main advance during the upload phase.

## Recommended implementation checkpoint

Do not merely add another fetch immediately before the existing `gh release create` call; that has already been done.

Refactor the publication phase into explicit steps:

1. create the release as a **draft** targeting `ExpectedSourceSha` (no client-visible immutable release yet);
2. upload exactly the ZIP and `update-manifest.json` while it remains draft;
3. inspect the draft identity/assets and verify exact names, sizes, and hashes as far as the API permits before publication;
4. **re-fetch `origin/main` after all uploads and immediately before draft publication**;
5. if main differs from `ExpectedSourceSha`, delete the draft/tag and skip publication;
6. publish the existing draft with `gh release edit <tag> --draft=false`;
7. confirm `isImmutable=true`, exact tag/source identity, and exact assets;
8. preserve current fail-closed handling for any inconsistent state.

This does not make a distributed ref check and release publication mathematically atomic, but it removes the potentially long asset-upload interval from the stale-main exposure window. The remaining window becomes the short recheck -> publish API transition.

## Regression / fault-injection contract

Before changing production publication behavior, add a deterministic command seam or disposable harness that can control the release command sequence.

Required cases:

1. main matches before draft creation and still matches after upload -> publish allowed;
2. main advances before draft creation -> no draft/publish;
3. main advances **during asset upload** -> draft is withdrawn and never published;
4. final main refresh fails -> fail closed, do not publish;
5. asset upload fails -> draft remains non-client-visible or is cleaned up deterministically;
6. draft asset verification fails -> do not publish;
7. publish succeeds but `isImmutable=false` -> current withdrawal/failure policy remains explicit;
8. existing immutable exact release remains idempotent;
9. existing non-immutable release remains rejected;
10. concurrent run/build ordering remains monotonic and does not clobber another tag or asset.

Do not make the test a text/regex assertion about command ordering. Exercise an injectable command/process boundary so the regression proves control flow.

## Severity and confidence

- **Severity:** P1 for release correctness under the project's explicit exact-current-main policy; artifact integrity itself remains separately protected by hashes, source SHA checks, package verification, and immutable-release validation.
- **Confidence:** high for the existence of the upload-phase race, based on current source plus GitHub's documented multi-request `gh release create` behavior.
- **Runtime reproduction:** not performed in this support checkpoint. No synthetic GitHub release was created.
- **Probability:** not measured.

## Deliberately not changed

This support checkpoint does not modify:

- updater C# runtime;
- helper apply/rollback logic;
- WPF integration;
- package layout;
- `Publish-UpdaterRelease.ps1`;
- publication workflow;
- verification cache;
- existing releases/tags;
- repository settings.

The implementation should be owned by the updater publication lane and should receive focused fault injection plus the full exact Windows release gate.

## Verification actually performed

- re-established canonical `main` at `a83dc6e047ccf98e896f10c25772df99b95426d1` immediately before branch creation;
- inspected current publisher, pure policy test, workflow, continuity, and updater handoff state;
- inspected current open PR scopes to avoid duplicating the pre-`gh release create` race repair;
- checked current official GitHub CLI/GitHub Docs behavior for immutable releases, `gh release create`, `isImmutable`, and draft publication;
- confirmed `heaven2` is online;
- inspected the only discovered local MHW mirror on `heaven2`; it is the known uncommitted/no-remote mirror and was not modified or used as canonical state.

No local PowerShell/product tests, hosted workflow, or live release mutation are claimed by this audit.

## Learned-rule decision

No new Learned Rule is proposed.

The durable lesson is already covered at company/project level by:

- exact verification belongs to exact inputs;
- build/CI/publishing are supply-chain-sensitive;
- verification infrastructure is production infrastructure;
- safety-sensitive behavior should receive failure-path/fault-injection coverage.

The new information here is specific to GitHub Release sequencing and belongs in this specialized audit rather than duplicating those broader rules.

## Parallel-agent integration notes

- PR #49 remains the authority for the already-landed first/second preflight `origin/main` recheck.
- This audit is the specialized authority for the residual **recheck -> gh draft/upload -> publish** window.
- Do not replay stale branch-local continuity snapshots over newer main.
- Recheck canonical main and open updater PRs before implementing; if another agent has already split draft/upload/publish and added a post-upload main check, treat this audit as satisfied rather than duplicating it.

## Successor handoff

The next implementation agent should:

1. re-establish current canonical `main`;
2. read this audit and current updater handoff;
3. preserve all current exact-SHA, package, monotonic-build, immutable-release, and two-asset checks;
4. add deterministic publication-sequencing fault coverage first;
5. split draft creation/upload from final publish;
6. place the final main refresh after uploads and before publish;
7. run the exact repository/Windows release gates for the changed publication infrastructure;
8. record exact verification/run/release evidence;
9. preserve the permanent continuity constitution and require its successor to preserve and recursively pass it to the agent after them.

**Do not break the chain.**
