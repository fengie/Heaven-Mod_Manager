# v8.8.107 mod collection reanalysis — current source and release candidate

Canonical state: `fengie/Heaven-Mod_Manager@main` source cutoff `5b50e6581b4b1bef56ad09fe422621388f494c85` includes: deep Stellar Blade boundaries and importer hardening through #813 from the prior v8.8.106 release; #815 cumulative inventory accounting; #818 safe user-driven game assignment for one unknown standalone mod; and #819 explicit read-only preview, confirm, and duplicate-safe staging of independent child mods from previously tracked, disabled single-package folders. No original package is automatically replaced or deleted, and live game files/activation/rollback authority remain unchanged.

## Verification boundary

[#819 native Windows Feature Gate 43695906763](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43695906763) passed on exact candidate `254f813f27be324cdf02f6a6b2dfcecb1bb53f0b`, 26/26 verification stages, zero trace gaps and zero integration failures. AutoUpdate 43695906697 passed. #819 then merged at `5b50e6581b4b1bef56ad09fe422621388f494c85`. v8.8.107 metadata is **not** the tested source or a published immutable asset until its own exact-head and exact-main gates complete.

## Source versus immutable release

Last verified immutable published version: [v8.8.106 updater-main-517](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-517), exact source `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d`, Windows Release 43690972860 26/26, ZIP SHA-256 `55222c54ff61754ebed38f43d4b3ee395a1e066a606e9e851fce5fa5c401bd64`, manifest SHA-256 `cb5f4d166854b816f92b31a49da79e39d2c0042d6d0356f0313cddfeba539`. Earlier tags 509/504/501 remain immutable. This new metadata release candidate requires new published evidence before claiming v8.8.107 publication; physically installed Heaven2 and actual rollback remain independent.

## Open acceptance and owner boundaries

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual high-volume Stellar Blade user corpus, real RAR/7z/solid format and fault tests (peer PR #820), unrecognized mod structures, installed WPF interaction/minimum-width UX.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive packaged updater apply/rollback requires a dedicated isolated Windows identity with verified empty updater root. Never delete or repurpose shared real user LocalApplicationData. A skipped E2E job or main source CI is not installed proof.
- Release owner: serialize after current main Windows Release Gate; prove version metadata, protected merge, new immutable tag, ZIP/manifest SHA, public feed parity. Preserve all unrelated owner work.

Next successor must read, preserve and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md` onward again.
