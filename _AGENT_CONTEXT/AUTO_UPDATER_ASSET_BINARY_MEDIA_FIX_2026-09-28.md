# Auto-updater release-asset binary media fix — 2026-09-28

## Scope
- Repository: `fengie/mhw-mods`.
- Support branch: `support/updater-artifact-accept-20260928`.
- Base updater checkpoint: `0afbbf8`.
- This is a narrow GitHub release-asset transport fix; no WPF, packaging, rollback, or publication behavior is changed.

## Defect
`GitHubUpdateSource.DownloadArtifactAsync` created an authenticated GitHub API request but left the generic
`application/vnd.github+json` Accept header in place. GitHub release-asset API requests require the binary
media type when the client expects asset bytes. The manifest download path already did this correctly.

## Repair
The artifact request now clears the generic Accept header and requests `application/octet-stream` before send.
The existing exact HTTPS/API-host credential restriction remains unchanged.

## Regression
`UpdaterGitHubAssetDownloadTests.Artifact_download_requests_binary_media_type` captures the outgoing request,
asserts the binary media type, returns known bytes, and proves the verified bytes are published to the destination.

## Exact local evidence on heaven
- Windows x64, .NET SDK 10.0.401.
- Focused updater suite including the new regression: 64/64 PASS.
- Full IntegrationTests: 160/160 PASS.
- Strict whole-solution Release build with `-warnaserror`: 0 warnings / 0 errors.
- `git diff --check`: PASS.
