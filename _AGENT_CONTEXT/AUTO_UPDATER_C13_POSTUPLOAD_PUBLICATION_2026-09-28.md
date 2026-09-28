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
- Pre-publication failures during upload, draft verification, or final-main refresh attempt deterministic draft/tag cleanup.
- Once the publish call begins, automatic cleanup is disabled because a nonzero client result is state-ambiguous: the release may already have become immutable.
- Existing post-publication immutable/tag/source/asset verification remains in place.
- Added fault-injection coverage for success ordering, main advance during upload, upload failure, draft-verification failure, final-main refresh failure, cleanup failure, and publish-command ambiguity.
- Added a narrow PR-only Windows gate for these publication scripts; it runs the publication policy/fault tests and continuity handoff check with read-only repository permissions and performs no release publication.

## Verification status
Not yet closed. This chat environment has not executed Windows PowerShell, the full repository verifier, Build-Release, or a hosted exact-main publication. The branch is intentionally not merged on static inspection alone.

Required closure:
1. require the PR-only Windows publication gate to pass `scripts/Test-UpdaterReleasePolicy.ps1` and `scripts/Test-AgentHandoff.ps1`;
2. run the repository handoff check and exact release verification/build gates;
3. review the PR diff and any PR-triggered checks;
4. integrate only after those are green;
5. on an eligible exact `main`, inspect the hosted Windows Release Gate and immutable release assets;
6. complete the disposable installed-client old→new plus injected rollback proof before calling automatic updates end-to-end complete.

## Continuity
Preserve the permanent continuity constitution and active Learned Rules. The successor must preserve them and explicitly require its own successor to recursively propagate them to the agent after them.

**Do not break the chain.**
