# v8.8.102 game-intake and in-place game-session integration — canonical continuity handoff

Canonical repository: `fengie/Heaven-Mod_Manager` (legacy signed updater/product identity `fengie/mhw-mods` is unchanged).
Global bootstrap/training: `fengie/heaven-toolbox@main`.
Current product version candidate: **8.8.102**.

## Current integrated behavior

- v8.8.101 publication: immutable `updater-main-495` for exact source `446e933f778dae8f2da0238951e28b8b84f3f0b0`. This was published, but the installed-client E2E later refused occupied shared updater state. Do not claim real update/rollback or physical Heaven2 deployment.
- PR #789: auto game-mod intake, source-folder monitoring, conservative classification, retained original downloads, per-game Inbox routing, visible Select All/Deselect All, Auto Populate discovers and imports Inbox before planning. Windows feature gate and main Windows release gate passed. Integrated on main as `9fa8c50e07c877eb3ae65ae0a34ec3433950d3fb`.
- PR #790: single-instance activation and in-process game switch with candidate profile rollback/retention. Windows feature and AutoUpdate Contract gates passed. Integrated on main as `54ae2c3852c6bb5e4e63553a20c69fee8ac0182f`.
- Audit-only PR #792 began with four deliberately red tests on baseline v8.8.101; those assertions now become release regression guards for merged features. This release metadata + audit candidate must still pass all exact-head Windows checks before merging.
- Issue #791 is the separate unimplemented archive decomposition feature (one mixed-game archive containing multiple mods); do not claim #789 implements that.
- Issue #793 is the occupied shared updater-state H1 installed-client E2E blocker. The following workflow classified/skipped the real updater job; it is not installed-client proof.

## Verification and release boundaries

1. Confirm #792 exact-head Windows Workflow Feature PR Gate, AutoUpdate Contract, README timestamp, security and ownership status checks for version candidate 8.8.102. Fix substantive issues before integration; do not weaken tests.
2. Merge only after all required checks pass and recheck exact protected `main` SHA. The version metadata and documentation from this candidate must be on `main` before publishing a new package.
3. Require canonical main Windows Release Gate to build/publish an immutable `v8.8.102` package with exact source and SHA-256. Check release tag, manifest, artifact identity, provenance, and gate logs. Do not force-republish.
4. Real installed-client update/rollback job and physical Heaven2/NieR startup/health remain independent. Do not delete unknown updater LocalApplicationData, change permissions, restart user hosts, or infer physical installation from a green source gate.
5. Preserve the E2E and release verification branches, and promote verified evidence into protected main through ordinary approved PRs; never manually set cached verification statuses. Keep #792's red baseline history linked as a regression explanation.

## Unresolved risks

The current version and deployed build may differ; no installed device receipt yet. Some anonymous mod archives cannot be classified reliably and stay in manual review. Starting a second app instance and switching game sessions require real packaged WPF acceptance in addition to source/integration tests.

## Successor obligation

You inherit the repository's permanent continuity constitution. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full before modifying the project, obey `_AGENT_CONTEXT/LEARNED_RULES.md`, and bootstrap fresh `fengie/heaven-toolbox@main`. Update justified durable lessons and exact verified source/run/owner handoff before stopping. Every successor **must preserve, obey and recursively propagate** these Core Rules to the agent after them; that successor must repeat the requirement for the next agent. Do not break the chain.
