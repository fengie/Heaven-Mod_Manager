# Auto-updater C9 recovery review — 2026-09-28

Purpose: independent support review of updater checkpoint C9 without changing production behavior.

- Support branch: `support/updater-c9-recovery-review-20260928`
- Reviewed primary tip/base: `112be5bc51e660decf0a48e280afb92bc82a7133`
- C9 production/test commit under review: `8cdf54d0bc4065a55124aef96c68b26de2a78f0c`
- Added one test-only theory covering native errors 1176 and 1177 across a fresh `UpdateInstaller` instance.
- The regression proves `RollbackRequired` state is consumable on the next apply: validated backup is restored first, the update is then retried successfully, and journal reaches `AppliedAwaitingHealth`.
- It also proves first-attempt native recovery evidence remains preserved rather than being silently discarded.
- The earlier local support draft that immediately rolled back 1176/1177 was intentionally rejected; C9's fail-closed preservation semantics are safer and remain unchanged.

Verification on `heaven`, Windows x64, .NET SDK 10.0.401:
- `UpdateInstallerTests`: 29/29 PASS
- focused updater suite: 63/63 PASS
- full IntegrationTests: 159/159 PASS
- strict whole-solution Release build: 0 warnings / 0 errors
- `git diff --check`: PASS

No production code changed. No Verify-Release, Build-Release, hosted Windows Release Gate, publication, or live old→new closure is claimed.

Integration: cherry-pick this support commit only if the primary updater branch has not already gained equivalent fresh-process 1176/1177 recovery coverage.

The successor inherits the permanent continuity constitution and active Learned Rules, must preserve them, and must require its own successor to recursively propagate them again.
