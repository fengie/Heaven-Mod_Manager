# v8.8.62 federated catalog recovery — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Candidate branch: `feat/catalog-ui-v8.8.62`
PR: #553

## Implemented on the candidate

- Added a final **Browse Mods** tab so existing tab indices and deferred page behavior remain stable.
- Uses the existing source-aware SQLite/FTS cache and provider sync services; provider refresh failures are isolated and cached rows remain browseable.
- Added exact provider-file loading, provider-page handoff, and exact installed-origin status checks.
- Added a catalog acquisition bridge: exact provider/game/mod/file validation, bounded HTTPS temporary download, authoritative SHA-256, existing archive inspection/import, then exact installed-origin persistence.
- Recovered the official CurseForge API adapter/transport/compliance policy plus deterministic fixtures/tests from the stale issue #281 lane.
- Recovered the fail-closed permitted crawler framework with reviewed terms/robots, origin/path containment, size/content-type bounds, and kill switch.
- CurseForge remains opt-in through external `MOD_MANAGER_CURSEFORGE_API_KEY` and `MOD_MANAGER_CURSEFORGE_GAME_ID`; no credential is persisted.

## Verification state

No final-head Windows verification is claimed yet. PR #553 must pass the repository's exact-head build/analyzer/test, product-security, Toolbox-ownership, and continuity gates after the last source/metadata edit.

## Unresolved risks and remaining work

- Steam Workshop is conditional: only implement for a game/profile with a supported Workshop contract and required capabilities/credentials. Do not invent an MHW mapping.
- Vortex is optional interoperability, not a catalog backend. Implement only an explicit supported metadata/import-export/handoff contract.
- Issue #350 has a checked-in ECDSA P-256 signed-updater design, but production closure requires real external public/private key provisioning plus an end-to-end signed release. Never commit a fake production key.
- Issue #354 source-side hardening may proceed, but GitHub rulesets currently return 403 for this private repository/account tier, and Authenticode publisher identity requires external certificate provisioning.
- RECOVERY-007 representative installed-game/runtime discovery proof and RECOVERY-005 installed WPF interaction acceptance remain evidence gaps.
- The direct local Heaven/Agent Control execution namespace is unavailable in this chat; the self-hosted Heaven Actions runner is the deterministic Windows execution fallback.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full. Preserve unresolved evidence/security boundaries and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
