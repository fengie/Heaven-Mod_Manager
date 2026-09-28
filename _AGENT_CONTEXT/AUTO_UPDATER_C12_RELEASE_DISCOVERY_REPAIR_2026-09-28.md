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

## Exact local evidence
- Exact tested commit: `9234c61c47f9ebc82b3a6ce546799ccaa6f395a2`.
- Windows PowerShell **5.1.26100.8737**; .NET SDK **10.0.401** on heaven2.
- `scripts/Test-UpdaterReleasePolicy.ps1` and `git diff --check`: **PASS**.
- `scripts/Verify-Release.ps1`: **25/25 PASS**.
- FunctionVerifier: **727/727 known-good**, **7749 explicit call sites / 0 uncovered**, **0 trace gaps**, **0 parse errors**.
- Core **79/79**, Automation **24/24**, Integration/fault injection **173/173**, automation self-test **11/11**.
- Strict solution and win-x64 App analyzers/build: **PASS, 0 warnings / 0 errors**.
- ReadyToRun app publish and self-contained updater-helper publish: **PASS**.
- `scripts/Build-Release.ps1`: **PASS**.
- Local updater build **230**; ZIP SHA-256 `E613A43E69B75B5CCFF87852F918D8BD270888B3F8D4A493E88A3A8DFD5E67D8`.
- Hosted rerun/publication and disposable old-to-new/rollback proof remain pending.

## Next actions
1. Commit/push this exact local-verification evidence on the isolated repair branch.
2. Integrate through the repository workflow without modifying the preserved stale/dirty updater worktree.
3. Run and inspect the exact-main Windows Release Gate; verify the immutable updater release/tag, exact two assets, server digests, and tag target.
4. Run disposable installed-client old-to-new and injected rollback tests, recording seeded user-data hashes, restarted executable/build identity, and health acknowledgement.
5. Only after both hosted publication and disposable client proofs are independently green call the updater end-to-end complete.

Preserve the permanent continuity constitution and active Learned Rules; explicitly require the successor to recursively propagate them to the agent after them.
