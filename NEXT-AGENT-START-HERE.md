# v8.8.102 game-intake and in-place game-session integration — canonical continuity handoff

Canonical repository: `fengie/Heaven-Mod_Manager` (legacy signed updater/product identity `fengie/mhw-mods` is unchanged).
Global bootstrap/training: `fengie/heaven-toolbox@main`.
Current integrated source version: **8.8.102**. Immutable release and installed-client validation are separate boundaries.

## Current integrated behavior

- v8.8.101 publication: immutable `updater-main-495` for exact source `446e933f778dae8f2da0238951e28b8b84f3f0b0`. This was published, but the installed-client E2E later refused occupied shared updater state. Do not claim real update/rollback or physical Heaven2 deployment.
- PR #789: auto game-mod intake, source-folder monitoring, conservative classification, retained original downloads, per-game Inbox routing, visible Select All/Deselect All, Auto Populate discovers and imports Inbox before planning. Windows feature gate and main Windows release gate passed. Integrated on main as `9fa8c50e07c877eb3ae65ae0a34ec3433950d3fb`.
- PR #790: single-instance activation and in-process game switch with candidate profile rollback/retention. Windows feature and AutoUpdate Contract gates passed. Integrated on main as `54ae2c3852c6bb5e4e63553a20c69fee8ac0182f`.
- PR #792 first proved four requested user-visible failures on baseline v8.8.101, then merged four green regression guards and v8.8.102 release metadata to main as `190858ad842deff645bcc836be3ab24fe09cc19a`. The first main release run failed only continuity-version parity, not those product tests. PR #794 merged the manifest/current-state version repair as `bc4e5981af65f8d69871b8ca0cce9b6e1eee6db6`.
- Issue #791 is the separate unimplemented archive decomposition feature (one mixed-game archive containing multiple mods); do not claim #789 implements that.
- Issue #793 is the occupied shared updater-state H1 installed-client E2E blocker. The following workflow classified/skipped the real updater job; it is not installed-client proof.

## Verification boundary

The v8.8.102 feature PR verification completed **26/26 stages, 0 failed**, with all four red-to-green baseline probes passing on H1. The first canonical main Windows Release Gate (run `42463658105` on source `190858ad842deff645bcc836be3ab24fe09cc19a`) failed a version mismatch: handoff-manifest currentVersion 8.8.101 versus VERSION.txt 8.8.102. All 432 integration, 342 core and 112 automation tests passed. PR #794 corrected the manifest and canonical current-state version data. A new main run is still required before declaring v8.8.102 release verification or publication.

## Next required action

1. Require exact-head Windows Feature, Security, Updater Publication, and AutoUpdate checks for PR #795 (workflow-trigger repair). Do not merge a red or unreviewed head.
2. After PR #795 merges, its Windows release workflow file change must trigger the canonical main Windows Release Gate on the new exact source. Verify all 26 stages, package build, published tag, source, SHA-256, and provenance. Do not force-republish an already published version.
3. The release gate stages allowlisted source-bound closure evidence on a protected `verification/` branch. Inspect it, require normal PR checks and merge only legitimate current-source evidence. Do not alter verification fingerprints or replay stale proof.
4. Real installed-client updater update/rollback and physical Heaven2/NieR runtime proof remain independent. Issue #793 documents occupied updater-state refusal. Do not delete unowned LocalApplicationData, weaken production guards, expand permissions or claim installation without an actual receipt.
5. Preserve the historic red baseline PR #792, the merged #789/#790 functionality, and the separate unimplemented mixed-game multi-mod-pack feature #791.

## Unresolved risks

The current version and deployed build may differ; no installed device receipt yet. Some anonymous mod archives cannot be classified reliably and stay in manual review. Starting a second app instance and switching game sessions require real packaged WPF acceptance in addition to source/integration tests.

## Successor obligation

You inherit the repository's permanent continuity constitution. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full before modifying the project, obey `_AGENT_CONTEXT/LEARNED_RULES.md`, and bootstrap fresh `fengie/heaven-toolbox@main`. Update justified durable lessons and exact verified source/run/owner handoff before stopping. Every successor **must preserve, obey and recursively propagate** these Core Rules to the agent after them; that successor must repeat the requirement for the next agent. Do not break the chain.
