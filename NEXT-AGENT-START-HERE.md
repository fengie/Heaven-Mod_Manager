# v8.8.57 Heaven Toolbox authority cutover — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`

## Cutover state

The source-side takeover is implemented in this branch. MHW contains no local global trainer, shared Git directive, reusable plugin tree, Heaven Bridge tree, Agent Control tree, or root `tools/` ownership surface. MHW-owned FunctionVerifier and SelfTest were preserved under `tests/MhwModManager.FunctionVerifier` and `tests/MhwModManager.SelfTest`, and solution/build/release references now use those product-owned locations.

Every MHW agent must refresh and bootstrap from current `fengie/heaven-toolbox@main` first, then refresh MHW and load its product-specific `AGENTS.md`, `_AGENT_CONTEXT/`, source, tests, plans, releases, and evidence. Reusable/global doctrine or tooling must not be re-created in MHW.

The MHW ownership regression gate fails if migrated global roots return or routing points back to MHW. MHW's security gate now covers product/updater/release invariants only; reusable repository security doctrine belongs to Toolbox.

## Verification state

Before integration, verify the exact candidate with the MHW governance baseline + negative fixtures, Heaven Toolbox ownership gate, product security gate, verifier relocation/build paths, and the canonical Toolbox `scripts/verify-mhw-transition.mjs --cutover` check. Historical v8.8.56 verification remains evidence only for the exact source it covered; this migration does not claim new runtime product proof.

## Coordination

The separately owned `fix/runtime-updater-hardening-v8.8.57-20261001` branch contains product updater/runtime work and must not be overwritten. After this cutover claims v8.8.57 on main, that lane must refresh main and allocate the next patch version when it integrates.

## Existing product risk

RECOVERY-007 universal installed-game discovery is merged but still requires representative Windows/runtime discovery evidence before DONE. Agent Control/Agent Manager/Heaven/plugin global follow-up has transferred to `fengie/heaven-toolbox` issue #5 and is not MHW product work. Continue only the mod-manager recovery items in `_AGENT_CONTEXT/PROJECT_PLAN.md`.

Start every successor from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and preserve active `LEARNED_RULES.md`. Your successor must preserve this continuity contract and propagate it to the agent after them; that agent must repeat the same obligation onward. **Do not break the chain.**
