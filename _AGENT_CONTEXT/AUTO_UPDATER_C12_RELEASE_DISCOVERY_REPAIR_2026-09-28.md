# Auto-updater C12 — first-publication discovery repair

## Repository and failure
- Canonical repository: `fengie/mhw-mods`.
- Canonical starting `origin/main`: `a83dc6e047ccf98e896f10c25772df99b95426d1`.
- Clean implementation branch: `agent/auto-updater-publication-fix-20260928`, created in a separate worktree from that exact SHA.
- Hosted Windows Release Gate run **36428542918** (run 37, exact starting SHA) passed repository verification, release build/package verification, and publication-policy tests. Publication then failed while parsing the empty release inventory: PowerShell strict mode reported that a returned object had no `tagName` property at `Publish-UpdaterRelease.ps1:73`.
- GitHub's authoritative Releases API returned an empty list. No updater release/tag is currently published.

## Repair
- Added `ConvertFrom-UpdaterReleaseList` to normalize empty CLI output and `[]` to an empty release collection.
- Nonempty output must be a JSON array; each entry must have `tagName`, `isDraft`, and `isImmutable`, with a nonempty tag. Malformed or unexpected output fails closed before publication.
- Publisher now parses the normalized list before duplicate-tag and monotonic-build policy checks.
- Added release-policy regressions for empty stdout, empty JSON, a valid single release, malformed JSON/object, null rows, and missing required fields.
- No updater runtime, package bytes, authentication, or release-gate workflow behavior changed.

## Exact local evidence so far
- Windows PowerShell **5.1.26100.8737**; .NET SDK **10.0.401**.
- Test-first run failed because the parser function was absent, as expected.
- After repair, `scripts/Test-UpdaterReleasePolicy.ps1`: **PASS**.
- `git diff --check`: **PASS**.
- Full `Verify-Release.ps1`, `Build-Release.ps1`, hosted rerun, release publication, and disposable old-to-new/rollback proof have not yet been run on this repair branch.

## Next actions
1. Review the exact diff and continuity updates; run script parse plus policy tests.
2. Run `scripts/Verify-Release.ps1` and `scripts/Build-Release.ps1`; inspect updater ZIP/manifest identity and SHA-256.
3. Commit/push the coherent repair checkpoint and open/update its PR; preserve the current main worktree.
4. After eligible exact-main integration, rerun the existing Windows Release Gate and verify the immutable release/tag/assets and exact digests.
5. Continue disposable installed-client old-to-new and injected rollback tests, recording user-data hashes, restarted executable/build identity, and health acknowledgement. Do not claim end-to-end closure until these pass.

Preserve the permanent continuity constitution and active Learned Rules; explicitly require the successor to recursively propagate them to the agent after them.
