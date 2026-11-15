# v8.8.106 consolidated mod intake — canonical state and release candidate

Canonical `fengie/Heaven-Mod_Manager@main` source already includes deep Stellar Blade mod boundaries and sibling PAK grouping (#803/#807), full archive preflight #805, mixed archives/doc review #808, full bounded game identity #809, late/conflicting ZIP/folder manifests #810, app-owned oversized manifests #811. Source cutoff is now integrated at `main@1e21c332995f2eba89a87f016b0f43360b73e268`, including #812 bounded third-party manifest handling and #813 cumulative per-selected-collection byte budget, each with exact native Windows Feature/AutoUpdate checks. This release metadata remains unverified for publication.

## Verification boundary

Current hosted-Windows closure: v8.8.106 source `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d` passed run `43690972860` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.106-heaven-windows-closure.log`.

The tested source remains `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Source versus release

Previous [v8.8.105 updater-main-509](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-509) was published from older source `e65a8990d6b70bab17f3d15aaaa5d5bc77ef8168`; later intake source fixes are not included. Reconcile this 8.8.106 serialized release candidate against verified `main@1e21c332995f2eba89a87f016b0f43360b73e268`, pass exact native release/security/ownership and README-timestamp gates, then require exact-main Windows Release Gate and a new immutable SHA-bound ZIP/manifest. The same-version 8.8.105 release policy correctly skips republishing; do not force it.

## Independent open acceptance

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large user mod corpus/rar/7z solid/encrypted/fault fixtures, manual unknown-game assignment and installed WPF UI smoke remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive updater apply/rollback requires an independent least-privileged Windows test account/profile with a proven empty updater namespace. Classification-only success with skipped update job is not deployed proof. Preserve real user updater state and game/mod files.

Every successor must read, preserve and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` to the successor after them; do not break the chain.
