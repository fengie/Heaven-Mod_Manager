# v8.8.103 mod-boundary importer — canonical source and release candidate

## Source truth

- Prior public release: immutable `updater-main-499` (v8.8.102) from `4a26a12e`, which predates mod-boundary discovery.
- PR #796 merged into protected main as `03219436e4f110dfa9b63a66452d94a2bb42553e`. Candidate `7896195b6e02581171b7395d487b33fbdc16c289` passed 26/26 Windows feature stages, 342 core, 112 automation and 453 integration tests. No v8.8.103 release or installed proof is asserted here.
- Product change: mixed-folder and archive boundaries, per-mod digest/replay receipts, game-independent Inbox routing, one-shot import, fail-closed handling of unknown/FOMOD options. Preserves original files, protected live game installations, legacy libraries, SQLite/deployment journals and updater trust gates.

## Release

v8.8.103 metadata is pending exact PR checks and expected-head protected merge. Then prove exact-main Windows Release Gate 26/26, immutable updater tag and manifest/artifact digest; promote only allowlisted source evidence by normal PR. Never overwrite/re-sign a prior release.

## Independent blockers

- #791 needs representative real RAR/7z fixtures, manual review/assignment and packaged WPF/min-width/keyboard/cancel smoke.
- #793 needs independently owned empty updater state/profile for real packaged old-to-new/rollback test; shared user LocalApplicationData must not be touched. Heaven2 installed version/health is unverified.
- Persistent closed verification records remain unchanged until exact-source evidence is genuinely promoted. Continue and recursively propagate the complete continuity constitution to successors.
