# v8.8.62 federated catalog recovery — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Canonical merge: `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71`
Exact verified source: `9c2976ae96533b3d439610ef7c770a73d0e14fe3`
PR: #553 (merged)

## Integrated in v8.8.62

- Added a final **Browse Mods** tab so existing tab indices and deferred page behavior remain stable.
- Uses the existing source-aware SQLite/FTS cache and provider sync services; provider refresh failures are isolated and cached rows remain browseable.
- Added exact provider-file loading, provider-page handoff, and exact installed-origin status checks.
- Added a catalog acquisition bridge: exact provider/game/mod/file validation, bounded HTTPS temporary download, authoritative SHA-256, existing archive inspection/import, then exact installed-origin persistence.
- Recovered the official CurseForge API adapter/transport/compliance policy plus deterministic fixtures/tests from the stale issue #281 lane.
- Recovered the fail-closed permitted crawler framework with reviewed terms/robots, origin/path containment, size/content-type bounds, and kill switch.
- CurseForge remains opt-in through external `MOD_MANAGER_CURSEFORGE_API_KEY` and `MOD_MANAGER_CURSEFORGE_GAME_ID`; no credential is persisted.

## Verification state

Exact head `9c2976ae96533b3d439610ef7c770a73d0e14fe3` passed Workflow Feature PR Gate `36869271453`, MHW Product Security Gate `36869271833`, and Heaven Toolbox Ownership Gate `36869271597`. The full Workflow gate included repository verification, strict builds, unit/integration tests, focused UX/XAML, migration, deployment-concurrency, catalog-sync, workflow regressions, continuity, and whitespace checks. PR #553 merged as `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71`, and post-merge comparison reports zero file differences from the verified head.

## Unresolved risks and remaining work

- Steam Workshop is conditional: only implement for a game/profile with a supported Workshop contract and required capabilities/credentials. Do not invent an MHW mapping.
- Vortex is optional interoperability, not a catalog backend. Implement only an explicit supported metadata/import-export/handoff contract.
- Issue #350 has a checked-in signed-updater design, but production closure requires real external public/private key provisioning plus an end-to-end signed release. Never commit a fake production key.
- Issue #354 source-side hardening may proceed, but GitHub rulesets remain externally unavailable on the current private-repository/account tier, and Authenticode publisher identity requires external certificate provisioning.
- RECOVERY-007 representative installed-game/runtime discovery proof and RECOVERY-005 installed WPF interaction acceptance remain evidence gaps.
- Older issue #281 recovery branches still carry unique historical commits; classify that ancestry before branch deletion rather than assuming byte-level supersession.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full. Refresh live findings/ownership before mutation. Preserve unresolved evidence/security boundaries and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
