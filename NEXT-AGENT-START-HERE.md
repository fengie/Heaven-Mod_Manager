# v8.8.57 Heaven Toolbox cutover — current handoff

Canonical MHW base for this cutover was `main` at `b469feb38448e4a38a2e19f24bc8feb99a04616e`. Global reusable agent authority is now `fengie/heaven-toolbox@main`; MHW is authoritative only for its product source, tests, project workflows/scripts, `_AGENT_CONTEXT/`, recovery ledger, release state, and runtime evidence.

## What changed

The MHW-side transition removes the duplicated global training tree, `GLOBAL_GIT_DIRECTIVE.md`, reusable plugins, Heaven Bridge, Agent Control, and the root `tools/` ownership surface. The two MHW-owned verifier applications were preserved by relocating them to `tests/MhwModManager.FunctionVerifier` and `tests/MhwModManager.SelfTest`, with solution/build/release references rewritten accordingly.

MHW governance now requires Toolbox-first bootstrap and contains a fail-closed ownership regression gate. MHW security validation now covers MHW-specific updater/release/package invariants instead of duplicating generic Toolbox repository policy.

## Verification state

The cutover candidate must pass the MHW governance baseline + negative fixtures, Heaven Toolbox ownership gate, product security gate, solution/build verification affected by the verifier relocation, and the canonical Toolbox transition verifier in `--cutover` mode before this handoff may claim exact-head closure. Historical v8.8.56 verification remains evidence only for the source it actually covered.

## Unresolved risk and next work

RECOVERY-007 still requires representative Windows/runtime installed-game discovery evidence before it can be marked DONE. Existing Agent Manager/heaven2 authentication and live-smoke risks remain project work; this ownership cutover does not claim those runtime gaps are solved. Continue the active items in `_AGENT_CONTEXT/PROJECT_PLAN.md` rather than reviving archived refs blindly.

Start every successor from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and preserve active `LEARNED_RULES.md`. Your successor must preserve this continuity contract and propagate it to the agent after them; that agent must repeat the same obligation onward.
