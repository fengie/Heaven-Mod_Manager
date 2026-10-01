# v8.8.57 Heaven Toolbox cutover — MHW product state

MHW is being finalized as the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

The v8.8.57 cutover candidate removes the migrated global roots, relocates MHW-owned FunctionVerifier and SelfTest projects under `tests/`, narrows MHW CI/security to product concerns, and adds a fail-closed ownership gate preventing global copies from returning.

## Current MHW product work

- **RECOVERY-007 / P0:** universal installed-game discovery source is integrated; representative Windows/runtime discovery proof is still required before DONE.
- **RECOVERY-002 / P0:** in-app catalog browser recovery remains active on its existing product lane.
- **RECOVERY-004 / P0:** updater/runtime hardening recovery remains product-owned; the existing updater branch must refresh after the v8.8.57 cutover and take the next patch version.
- **RECOVERY-003 / P1:** manual update-check UI remains product work.
- **RECOVERY-005 / P1:** dark ComboBox chrome/contrast remains product work.

Legacy Agent Control, Agent Manager, Heaven Bridge/auth/provider, Agent Work Reports, plugin-toolbox, and global coordination follow-up has transferred to `fengie/heaven-toolbox` issue #5. It is not active MHW work.

## Verification boundary

PR #548 must pass its exact-head ownership, product-security, governance/continuity, integration, and affected build/test gates before integration. Historical v8.8.56 evidence remains exact-source evidence only. This cutover does not claim RECOVERY-007's remaining representative runtime proof.
