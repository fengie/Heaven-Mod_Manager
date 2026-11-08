# v8.9.0 source and release delivery — canonical continuity handoff

Canonical repository: `fengie/Heaven-Mod_Manager` (historical `fengie/mhw-mods` product/updater identity must remain intact).
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical branch: `main`
Current product version: **8.9.0**

## Current behavior

- #784 prevents an empty NieR library from recursively hashing live game assets at startup, skips zero-mod conflict analysis and preserves explicit/manual adoption and error reporting.
- #785 organizes new installs and downloaded archives by canonical game ID with separate Installed, Downloads, Inbox and Archive folders; legacy MHW and generic-game data remain indexed without migration.
- The patch preserves strict provider compliance, verified archive hashes, updater user-data path exclusion, rollback E2E sentinels, and cross-process updater test state exclusivity.
- #558 and #735 remain open for their independent catalog coverage and installed performance acceptance.

## Verification boundary

**v8.9.0 canonical release and installed-runtime acceptance are not yet verified.** Last successful historical v8.8.99 Windows closure: source `c08b31c5327522c5896704d7a24b5ddc6f70b9c4`, run `38006379575`, evidence `_AGENT_CONTEXT/EVIDENCE/v8.8.99-heaven-windows-closure.log`. This earlier evidence must not be attributed to v8.9.0. New source/test/workflow changes require fresh exact-input Windows checks.

## Next action

1. Refresh live `main`, #785 ownership/merge, exact current branch trees, and `_AGENT_CONTEXT/CURRENT_REVISION.json`.
2. After verified integration, require the full `.github/workflows/windows-release-gate.yml` on exact canonical `main` with `force_publish=false`; do not bypass expired real provider Terms reviews.
3. Require `Verify-Release.ps1`, `Build-Release.ps1`, updater policy, signed/provenance checks, immutable public release mirroring and installed-client E2E results to pass.
4. Allow `Sync-VerificationContinuity.ps1` to record only actual exact-source/run closure after success; never hand-edit historical green results or reuse old version tags.
5. Verify an actual installed NieR:Automata zero-mod launch and game-specific mod import on a permitted machine before claiming physical deployment; if no authenticated runtime route exists, keep it explicitly unverified.
6. Installed-client E2E must use an unoccupied updater state root. It now fails closed rather than deleting user data; if this preflight fails, use a separately authorized dedicated Windows test account/runner instead of clearing live updater files.

## Unresolved risk

Until full exact-main Windows verification and new published updater assets exist, v8.9.0 remains unreleased. Windows checks cannot establish which build is installed on Heaven2 without a fresh installed source/health receipt. Preserve every Mods, State, legacy library, user-owned download, CAS, crash recovery, rollback, and updater security boundary.

## Successor obligation

You inherit the repository's permanent continuity constitution. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full before modifying the project, obey `_AGENT_CONTEXT/LEARNED_RULES.md` and bootstrap fresh `fengie/heaven-toolbox@main`. Preserve exact verification provenance, update durable repo handoff and justified learned rules, and explicitly require your successor to inherit, preserve and **recursively propagate** these same rules to the agent after them. That successor must repeat the obligation again. Do not break the chain.
