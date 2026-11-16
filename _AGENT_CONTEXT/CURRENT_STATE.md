# v8.8.109 bulk archive drag-drop import — source merged, release pending

Canonical state: Product source is `fengie/Heaven-Mod_Manager@main` at `9e3fc0be049cc43cbcaa506ff854502abc499773` through [PR #829](https://github.com/fengie/Heaven-Mod_Manager/pull/829). Previous v8.8.108 immutable [updater-main-524](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-524) belongs to older source `93eb26a7`; it does not contain this feature.

## User-facing implementation

Import ZIP/RAR/7z archives through multi-select or drag files/folders onto the WPF window; Dashboard exposes a one-shot folder batch. Shared game registry/receipts and sequential bounded archive routing reduce repeated intake overhead, display per-item progress, and preserve originals. Identified mods stage into the corresponding registered games' Inboxes regardless of active game. No unsafe automatic guessing, unauthorized live-game deployment or deletion. Legacy single-collection cumulative byte limits remain unchanged; explicit archive-only folder batches stage bounded individual sources.

## Verification boundary

Current hosted-Windows closure: v8.8.109 source `550d74b62c9bbc3cef8922a4e019cf5239d90bde` passed run `43979311739` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.109-heaven-windows-closure.log`.

The tested source remains `550d74b62c9bbc3cef8922a4e019cf5239d90bde` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent acceptance

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): large real RAR/7z collections, installed WPF drag/drop/progress, game-specific unknown layouts and independent mod boundaries.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): installed-client update/rollback must use an isolated safe Windows user/profile; never delete or repurpose occupied updater state.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): systemic release-status continuity after publication.

Next: finish the source-only main gate, verify metadata candidate, serial protected merge, publish and verify a distinct immutable version, then gather physical installation and real-user performance evidence. Every successor inherits, preserves and recursively propagates the full continuity constitution and active Learned Rules to its successor.
