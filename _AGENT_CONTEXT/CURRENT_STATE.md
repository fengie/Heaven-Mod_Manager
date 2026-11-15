# v8.8.106 consolidated mod intake — published source and open acceptance

Canonical `fengie/Heaven-Mod_Manager@main` source already includes deep Stellar Blade mod boundaries and sibling PAK grouping (#803/#807), full archive preflight #805, mixed archives/doc review #808, full bounded game identity #809, late/conflicting ZIP/folder manifests #810, app-owned oversized manifests #811. Source cutoff is now integrated at `main@1e21c332995f2eba89a87f016b0f43360b73e268`, including #812 bounded third-party manifest handling and #813 cumulative per-selected-collection byte budget, each with exact native Windows Feature/AutoUpdate checks. Source release metadata was merged as PR #814; source `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d` passed the canonical release gate and was published as immutable updater build 517.

## Verification boundary

Current hosted-Windows closure: v8.8.106 source `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d` passed run `43690972860` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.106-heaven-windows-closure.log`.

The tested source remains `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Source versus release

[v8.8.106 / updater-main-517](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-517) is published from exact source `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d` following [Windows Release Gate #43690972860](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43690972860) with 26/26 verification stages. Package SHA-256: `55222c54ff61754ebed38f43d4b3ee395a1e066a606e9e851fce5fa5c401bd64`; manifest SHA-256: `cb5f4d166854b816f92b31a49da79e39d2c0042d6d0356f66f0313cddfeba539`. Prior immutable 509/504/501 releases remain unchanged.

[PR #815](https://github.com/fengie/Heaven-Mod_Manager/pull/815) fixes visited-empty-directory inventory accounting, but is subsequent source work **not included** in build 517. Merge it only after fresh exact-head checks, current-main reconciliation, and source ownership checks; it requires a later separately versioned updater release to ship. Issue #793 actual installed update/rollback and Heaven2 proof remain separate unverified acceptance boundaries.

## Independent open acceptance

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large user mod corpus/rar/7z solid/encrypted/fault fixtures, manual unknown-game assignment and installed WPF UI smoke remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive updater apply/rollback requires an independent least-privileged Windows test account/profile with a proven empty updater namespace. Classification-only success with skipped update job is not deployed proof. Preserve real user updater state and game/mod files.

Every successor must read, preserve and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` to the successor after them; do not break the chain.
