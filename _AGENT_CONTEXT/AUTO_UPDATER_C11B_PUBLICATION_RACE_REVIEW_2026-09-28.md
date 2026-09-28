# Auto-updater C11b publication race support review

## Scope
- Base: `support/updater-c11-gap-closure-20260928` at `b1d765c`.
- Review is intentionally limited to immutable updater publication eligibility.
- No product C#, updater runtime behavior, packaging contents, or verification caches are changed.

## Finding
The C11b publication script fetched `origin/main` during preflight, evaluated publication policy, then performed additional release/tag inspection before `gh release create`.

A concurrent push to `main` in that interval could make the exact build stale while still allowing the old source SHA to become client-visible as a new updater release.

This violates the documented requirement to confirm `origin/main == GITHUB_SHA` immediately before publication.

## Repair
Immediately before `gh release create`:
1. fetch `origin/main` again;
2. resolve the refreshed remote-main SHA;
3. skip publication if it no longer equals the exact expected source SHA;
4. fail closed if the refresh itself fails.

Existing immutable-tag, asset-integrity, monotonic-build, evidence-only, and no-clobber behavior is unchanged.

## Verification
- `scripts/Test-UpdaterReleasePolicy.ps1`: PASS.
- `Publish-UpdaterRelease.ps1` parses successfully as PowerShell.
- `scripts/Test-AgentHandoff.ps1`: PASS.
- `git diff --check`: PASS.

The primary C11 support agent can cherry-pick this checkpoint or merge the support branch before handing publication code back to the updater agent.
