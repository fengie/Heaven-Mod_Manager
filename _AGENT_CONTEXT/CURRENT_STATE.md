# v8.8.81 authoritative MHW discovery hardening — canonical state

v8.8.81 closes the remaining confirmed discovery gaps adjacent to the authoritative game-profile lifecycle work.

## Behavior

- When authoritative MHW discovery resolves the real `MonsterHunterWorld.exe` under a root already owned by a live generic profile, rebuild that owner from the canonical MHW adapter while preserving its user-owned ID, display name, save path, and established store.
- Reuse the existing single-owner persistence path so duplicate same-root siblings are removed deterministically and an active marker targeting a removed duplicate is repointed before registry rewrite.
- When Steam app 582010 is reported but the resolved executable is not `MonsterHunterWorld.exe`, reject the candidate without mutating registry state.
- Preserve existing generic-game behavior and make repeated discovery/restart idempotent.

## Verification boundary

v8.8.80 exact source `6e97751252ce1875550a6cd35630bb63e25a1de3` remains the last closed hosted-Windows verification source, with run `37091006888` passing 26/26 stages. The later main commit `8d7f9061eaf784776a2be21a0678a2dbf05a129a` is evidence-only.

v8.8.81 changes product source and integration tests, so it requires fresh exact-input verification. Do not inherit the v8.8.80 green boundary.

## Remaining independent work

- #350 still requires a real independently protected updater signing identity, embedded production public trust anchor, release-side signing, and a real signed-release E2E.
- #354 remains externally gated on repository-admin controls and a stable Windows publisher identity.
- #558/#559 retain Browse Mods scale and broader UX work.
- RECOVERY-005/RECOVERY-007 still retain representative installed Windows acceptance beyond these deterministic discovery regressions.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve exact-input verification and release-safety boundaries, and recursively propagate the continuity obligation.
