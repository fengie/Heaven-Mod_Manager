# v8.8.73 Vortex interoperability handoff — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `main`
Integrated issue: #281
Parent canonical product boundary: `7e74aba165996c39af9daa3e90a7605793495c56`

## v8.8.73 canonical state

- Vortex remains an optional interoperability boundary, never a remote catalog backend.
- The reviewed MHW handoff identity is Vortex `monsterhunterworld`, Nexus `monsterhunterworld`, Steam `582010`, mod root `nativePC`, executable `MonsterHunterWorld.exe`.
- Advanced Tools can export a credential-free `*.vortexhandoff.json`, preview one, and save only exact/verified local matches as a profile.
- Handoff import never downloads archives or mutates the live game tree; normal import/deployment safety remains authoritative.
- Steam Workshop remains disabled for MHW because no reviewed operation-specific Workshop contract exists. Never infer Workshop capability from `SteamAppId`.
- Deterministic tests cover contract identity, export/preview/profile round-trip, wrong-game rejection, and traversal rejection.

## Verification provenance

The last closed predecessor evidence remains v8.8.71 PR #573 exact head `90fe998b2024aa43f7e3999185c87a98cac6a016`. Do not reuse it as v8.8.73 authorization. The exact final v8.8.73 integration head must pass the repository's required gates before merge, and canonical `main` must be read back afterward.

## Unresolved risks

- #558 remains owned separately for broader provider-aware catalog pagination/discovery and deterministic scale/performance work.
- #559 remains separate for Browse Mods filters, sorting, provider-health and richer loading/stale/partial-failure UX.
- #578 remains separate for canonical MHW stale-profile repair and complete same-root profile-set handling.
- #350 still requires a real external production signing key/trust anchor and signed-release E2E before full closure.
- #354 still has repository-administration/account-tier and Authenticode publisher-identity prerequisites.
- RECOVERY-007 still needs representative installed Windows/runtime discovery proof.
- Existing artifact-storage and installed-WPF acceptance constraints remain unchanged.

## Ordered continuation

1. Refresh canonical `main`, open issues/PRs, and durable ownership before selecting work.
2. Do not duplicate live #558/#559/#578 owners; support or integrate their verified work when ownership permits.
3. Treat #350/#354 external prerequisites honestly; source scaffolding is not production-key/ruleset completion.
4. Require fresh exact-head verification for every integration and read back canonical `main` after merge.
5. Preserve and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and relevant learned rules.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
