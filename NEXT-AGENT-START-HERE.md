# v8.8.57 Heaven Toolbox authority cutover — current handoff

Canonical MHW product repository: `fengie/mhw-mods`  
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`

## What this patch changes

MHW is being reduced to the **MHW Manual Mod Manager product repository only**. Global programming-agent training, reusable plugins/tools, Heaven Bridge, Agent Control, and shared Git/integration doctrine now belong canonically to Heaven Toolbox.

Every MHW agent must therefore:

1. refresh and bootstrap from current `fengie/heaven-toolbox@main`;
2. then refresh MHW and read this repository's MHW-specific `AGENTS.md`, `_AGENT_CONTEXT/`, source, tests, plans, releases, and evidence;
3. never author new reusable/global training or tooling in MHW first.

The local `_AGENT_TRAINING/`, reusable `plugins/`, `heaven-bridge/`, `tools/agent-control/`, and `GLOBAL_GIT_DIRECTIVE.md` are temporary compatibility copies only. Their final removal is tracked by issue #546 and the canonical Toolbox `MIGRATION_PLAN.md`.

## Remaining cutover phases

- Prove one clean MHW integration/development cycle using Toolbox-first bootstrap.
- Relocate MHW-owned `tools/MhwModManager.FunctionVerifier` and `tools/MhwModManager.SelfTest` into the MHW project/test layout.
- Delete migrated global compatibility copies from MHW and remove the root `tools/` directory.
- Run Toolbox `scripts/verify-mhw-transition.mjs --cutover`.
- Add an MHW ownership gate that rejects reintroduction of deleted global roots.

## Coordination

The separate `fix/runtime-updater-hardening-v8.8.57-20261001` branch contains product updater/runtime work and must not be overwritten. This cutover patch owns only repository/bootstrap authority and continuity metadata. After this patch claims v8.8.57, the updater-hardening lane must refresh main and allocate the next patch version when it integrates.

## Existing product risk

The v8.8.56 universal installed-game discovery product work is merged. Its prior live Windows representative-discovery risk remains a product/runtime concern and is independent of this repository-ownership cutover.

You inherit the permanent MHW continuity constitution in `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`. Preserve MHW-specific continuity recursively, while sourcing reusable/global training from Heaven Toolbox rather than MHW's temporary compatibility trainer.
