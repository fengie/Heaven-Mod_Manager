# Repository layout cleanup — 2026-09-29

Canonical repository: `fengie/mhw-mods`.

## Landed change

Main commit `ec52562be0c12f5284dd4946ca8b1935529afa5a` moved the low-conflict root research notes below into `docs/research/` and added `docs/REPOSITORY-LAYOUT.md`:

- `RESEARCH-FAMILY-INFERENCE.md`
- `RESEARCH-MULTIGAME.md`
- `RESEARCH-VISUALS-AND-UPDATES.md`

No production code or build behavior changed.

## Coordination boundary

Active PR #139 owns README/AGENTS/CHANGELOG, workflow docs, selected `_AGENT_CONTEXT` files, and a broad src/tests workflow surface. The cleanup intentionally did not move root files whose references would require editing those active files.

Future structural cleanup should follow `docs/REPOSITORY-LAYOUT.md`, re-query active PR/branch ownership first, and move referenced files atomically with all references.
