# v8.8.76 game-profile reconciliation — canonical-ready handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Integrated predecessor: v8.8.75 plus the later canonical #591/#354/#350 security hardening
Change set: issue #578 stale/duplicate game-profile lifecycle

## v8.8.76 behavior

- `games.json` remains the single authoritative persisted game-profile registry.
- One discovery scan reconciles an in-memory snapshot and atomically writes the complete registry once instead of durably mutating one profile at a time.
- Stale generic Monster Hunter: World state is rebuilt from the canonical MHW adapter shape while preserving the existing profile ID, display name, save path, and legitimate store choice.
- Live same-root profiles are preserved. Stale same-root siblings are removed deterministically so dead duplicate records do not survive discovery/restart cycles.
- If reconciliation removes the active stale sibling, `active-game.txt` is mapped to a deterministic surviving/repaired owner. If interruption occurs between the registry and marker writes, `GetActive()` heals the stale marker on restart.
- Repeated reconciliation is idempotent; cleanup-only scans are reported explicitly instead of claiming that nothing changed.

## Security boundary inherited from canonical main

- #591 removed the completed Heaven2 one-shot identity probe and added a repository security regression against arbitrary `Win32_Process.CommandLine` and unrestricted local-heartbeat logging.
- #350 integrated the independently signed updater metadata verification core. Production public trust-anchor/key ceremony and a real signed-release E2E remain external completion requirements.
- #354 restored machine-enforced self-hosted-runner and tracked secret/private-key policy gates. Account-tier repository rulesets and stable Authenticode publisher provisioning remain external requirements.
- Do not weaken any of those boundaries while reconciling older feature work.

## Verification boundary

The last closed predecessor verification record in `CURRENT_REVISION.json` is not authorization for v8.8.76. Require fresh exact-head gates for the complete v8.8.76 tree, including version/continuity files, before integration. After merge, read back canonical `main`, verify issue #578 closure, and retire stale PR #582 rather than merging its stacked v8.8.72-era history.

## Remaining work

- RECOVERY-007 still needs representative installed Windows/runtime discovery proof even after #578 source/state lifecycle completion.
- Issue #281 / PR #585 Vortex interoperability remains independent and must use an explicit credential-free handoff contract.
- #350 and #354 retain the external production-signing/repository-tier/publisher-certificate prerequisites described above.
- #558/#559 and RECOVERY-005 retain their existing independent scopes.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, single-owner profile persistence, and release/security trust boundaries. Propagate this continuity obligation to the next agent after you. **Do not break the chain.**
