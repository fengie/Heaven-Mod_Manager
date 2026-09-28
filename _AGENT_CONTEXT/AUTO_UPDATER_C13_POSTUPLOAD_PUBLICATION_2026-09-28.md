# Auto-updater C13 — post-upload publication boundary

## Canonical start
- Repository: `fengie/mhw-mods`.
- Exact canonical `main` at branch creation: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.
- Branch: `agent/auto-updater-postupload-publication-20260928`.
- This checkpoint implements the confirmed residual publication-window finding documented in `UPDATER_PUBLICATION_WINDOW_AUDIT_2026-09-28.md`.
- Parallel product/UI/archive work remains outside this updater publication boundary.

## Problem
The publisher previously re-fetched `origin/main` before invoking one `gh release create` command with assets. GitHub CLI creates the release as a draft, uploads assets through separate API calls, then publishes it. Therefore `main` could advance during asset upload and the older exact source could still become client-visible.

## Change
- Added `scripts/UpdaterReleasePublication.ps1` with a deterministic publication sequencer.
- Publication now creates a draft with no assets, uploads the exact ZIP and `update-manifest.json` without clobbering, verifies draft identity and server-side asset sizes/SHA-256 digests, refreshes `origin/main` after upload, withdraws the draft/tag if `main` advanced, and only then publishes the draft.
- Pre-publication failures after confirmed draft creation (upload, draft verification, or final-main refresh) attempt deterministic draft/tag cleanup. A create-command failure is treated as response-ambiguous: no publish occurs and the operator must inspect possible private draft state before retry rather than guessing with destructive cleanup.
- Once the publish call begins, automatic cleanup is disabled because a nonzero client result is state-ambiguous: the release may already have become immutable.
- Existing post-publication immutable/tag/source/asset verification remains in place.
- Added fault-injection coverage for success ordering, main advance during upload, upload failure, draft-verification failure, final-main refresh failure, cleanup failure, and publish-command ambiguity.
- Added a narrow PR-only Windows gate for these publication scripts; it runs the publication policy/fault tests and continuity handoff check with read-only repository permissions and performs no release publication.

## Verification status
The implementation has been extended by PR #65 / `agent/updater-publication-verification-20260928` after repeated hosted evidence exposed a separate post-publication verification race. Windows Release Gate runs **36442433856** and **36443919632** both published correct immutable releases 43/44 and then failed because immediate Git transport could not fetch the new tag, even though GitHub REST exposed the correct direct-commit ref. The publisher now verifies that just-created tag through the GitHub REST git-ref endpoint and fails closed on wrong ref identity, non-direct-commit targets, or malformed SHAs.

Exact locally verified code head before continuity-only edits: `98115515b2dc5fb256560c50ed8c40d08abb21f6`, based on main `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`. Fresh heaven2/Windows/.NET 10.0.401 evidence: publication policy PASS; handoff PASS; `Verify-Release.ps1` **25/25**; FunctionVerifier **728/728** with **7,772 / 0 uncovered**; Core **79/79**; Automation **24/24**; Integration **177/177**; self-test **11/11**; strict builds/analyzers PASS; app ReadyToRun and updater-helper publish PASS; `Build-Release.ps1` PASS with test-only build **900000065** and ZIP SHA-256 `5ABE8B2E4F8F6BF2E0ECED05BC690156274158B9A046FADD550ED08A0DD6F26C`. Generated verification/cache/log outputs were restored rather than committed.

Required closure:
1. require the PR #65 hosted Windows publication gate to pass on its final continuity-updated head;
2. integrate only after rechecking current `main` and PR mergeability;
3. inspect the resulting exact-main Windows Release Gate and require it to finish green through post-publication tag/source/asset verification;
4. verify the resulting immutable release tag/source and exact two asset names/sizes/server SHA-256 digests;
5. close/supersede PR #58 because PR #65 contains its sequencing work;
6. complete the disposable installed-client old→new plus injected rollback proof before calling automatic updates end-to-end complete.

## Continuity
Preserve the permanent continuity constitution and active Learned Rules. The successor must preserve them and explicitly require its own successor to recursively propagate them to the agent after them.

**Do not break the chain.**
