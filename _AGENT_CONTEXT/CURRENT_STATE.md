# v8.8.106 consolidated mod intake — canonical state and release candidate

Canonical `fengie/Heaven-Mod_Manager@main` source already includes deep Stellar Blade mod boundaries and sibling PAK grouping (#803/#807), full archive preflight #805, mixed archives/doc review #808, full bounded game identity #809, late/conflicting ZIP/folder manifests #810, app-owned oversized manifests #811. Remaining source cutoff: independent #812 generalized bounded third-party manifest intake and #813 cumulative per-selected-collection byte budget. Those must merge with verified native Windows CI before this release candidate.

## Verification boundary

PR #811 exact head `ffc774640d373b0a535add98cd948c89411b6872` passed native Feature Gate [43686796948](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43686796948) 26/26 (352 core, 112 automation, 504 integration/fault tests). Its source merged as `e29a01d3bc9218ff84f7c7731ff21eea74c189e1`. Later #812/#813 source and this v8.8.106 metadata have **no observed canonical v8.8.106 release or installed-client proof**, and older tests do not certify new content. Keep existing lastClosedVerification pointers untouched until verified exact-source/run-bound promotion.

## Source versus release

Previous [v8.8.105 updater-main-509](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-509) was published from older source `e65a8990d6b70bab17f3d15aaaa5d5bc77ef8168`; later intake source fixes are not included. Merge the two outstanding feature PRs, reconcile the 8.8.106 serialized release candidate against fresh main, pass exact native release/security/ownership and README-timestamp gates, then require exact-main Windows Release Gate and new immutable SHA-bound ZIP/manifest. The same-version 8.8.105 release policy correctly skips republishing; do not force it.

## Independent open acceptance

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large user mod corpus/rar/7z solid/encrypted/fault fixtures, manual unknown-game assignment and installed WPF UI smoke remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive updater apply/rollback requires an independent least-privileged Windows test account/profile with a proven empty updater namespace. Classification-only success with skipped update job is not deployed proof. Preserve real user updater state and game/mod files.

Every successor must read, preserve and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` to the successor after them; do not break the chain.
