# v8.8.106 consolidated mod intake — canonical state and release candidate

Canonical `fengie/Heaven-Mod_Manager@main` source already includes deep Stellar Blade mod boundaries and sibling PAK grouping (#803/#807), full archive preflight #805, mixed archives/doc review #808, full bounded game identity #809, late/conflicting ZIP/folder manifests #810, app-owned oversized manifests #811. Source cutoff is now integrated at `main@1e21c332995f2eba89a87f016b0f43360b73e268`, including #812 bounded third-party manifest handling and #813 cumulative per-selected-collection byte budget, each with exact native Windows Feature/AutoUpdate checks. This release metadata remains unverified for publication.

## Verification boundary

PR #811 exact head `ffc774640d373b0a535add98cd948c89411b6872` passed native Feature Gate [43686796948](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43686796948) 26/26 (352 core, 112 automation, 504 integration/fault tests). Its source merged as `e29a01d3bc9218ff84f7c7731ff21eea74c189e1`. Source #812 head `83ad12080930e7e1bf479f1f82adc0392f057717` passed [Windows Feature Gate 43689126798](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43689126798) 26/26 (including 513 integration/fault tests), while #813 head `4adfdea3d8b7e616a70e5ff2d95f01b0223fdcf5` passed [Windows Gate 43688039038](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43688039038). Both merged in `main@1e21c332995f2eba89a87f016b0f43360b73e268`. This v8.8.106 metadata has **no observed immutable release or installed-client proof**, and older tests do not certify changed release inputs. Keep existing lastClosedVerification pointers untouched until verified exact-source/run-bound promotion.

## Source versus release

Previous [v8.8.105 updater-main-509](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-509) was published from older source `e65a8990d6b70bab17f3d15aaaa5d5bc77ef8168`; later intake source fixes are not included. Reconcile this 8.8.106 serialized release candidate against verified `main@1e21c332995f2eba89a87f016b0f43360b73e268`, pass exact native release/security/ownership and README-timestamp gates, then require exact-main Windows Release Gate and a new immutable SHA-bound ZIP/manifest. The same-version 8.8.105 release policy correctly skips republishing; do not force it.

## Independent open acceptance

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large user mod corpus/rar/7z solid/encrypted/fault fixtures, manual unknown-game assignment and installed WPF UI smoke remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive updater apply/rollback requires an independent least-privileged Windows test account/profile with a proven empty updater namespace. Classification-only success with skipped update job is not deployed proof. Preserve real user updater state and game/mod files.

Every successor must read, preserve and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` to the successor after them; do not break the chain.
