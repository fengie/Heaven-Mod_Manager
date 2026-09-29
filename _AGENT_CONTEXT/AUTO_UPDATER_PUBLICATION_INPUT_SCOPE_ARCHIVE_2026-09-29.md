# Auto-updater publication input-scope finding — archived during branch reconciliation

Date: 2026-09-29

Source branch: `support/updater-publication-input-scope-20260928`

## Preserved finding

The source branch demonstrated that documentation/manual-only changes can satisfy a broad updater release-input classifier and therefore can create a client-visible no-op updater release when documentation paths are treated as publication inputs.

The branch proposed narrowing updater publication relevance so that `docs/`, `legacy-v7/`, `README.md`, `CHANGELOG.md`, `VALIDATION.md`, and debug-launch batch files would not trigger updater publication, while keeping product/build/package/toolchain/version inputs relevant.

Its branch-local checks reported:
- `scripts/Test-UpdaterReleasePolicy.ps1`: PASS
- `scripts/Test-PowerShellSyntax.ps1`: PASS
- `scripts/Test-AgentHandoff.ps1`: PASS
- `git diff --check`: PASS

## Canonical disposition

Do **not** replay the old branch implementation wholesale.

Current `main` has since evolved the release policy and intentionally still classifies several documentation/root files as release-relevant. It also contains newer release-classifier logic that ignores unrelated workflows while retaining the current desktop release-gate semantics.

Therefore:
- the finding and rationale are preserved here;
- the old policy/test patch is intentionally **not** canonicalized as code;
- `support/updater-publication-input-scope-20260928` is safe to delete after this archival commit;
- any future narrowing of documentation release inputs must be evaluated as a fresh policy change against current `main`, with current release-gate verification.

This disposition is part of branch cleanup: preserve durable knowledge, reject stale implementation, remove the obsolete branch.
