# v8.8.108 read-only structural mod discovery — serialized release candidate

Canonical repository: `fengie/Heaven-Mod_Manager@main`; canonical source cutoff `5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7`. The global trainer remains `fengie/heaven-toolbox@main`. A successor must refresh both actual repositories, current branch/PR ownership, exact tests and _AGENT_CONTEXT/PROJECT_PLAN.md before acting.

**Mandatory recursive continuity:** Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` completely and obey `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve both Core Rules and actively relevant lessons, and explicitly propagate this same inheritance duty to their successor. This remains binding even when prior chats are lost. Do not break the chain.

## Implemented and verified source

- [PR #823](https://github.com/fengie/Heaven-Mod_Manager/pull/823), head `20c7a71b9906c0d1c5cc0350fab124e32945fc63`, merged as protected `main@5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7`. New **Inspect Existing Mod Layouts** action on Dashboard/Mods gives every tracked package a read-only nested file inventory and review dialog. Sifu PAK/SIG and Stellar Blade PAK/UTOC/UCAS are only filename companion-pattern evidence, never independent-mod or installation authority.
- PR #823 [native Windows Feature Gate 43707926891](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43707926891) completed success including strict source/test/function checks. [AutoUpdate Contract 43707926895](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43707926895) completed success. The source PR intentionally changed neither release metadata nor existing importer/deployer.
- The previous immutable release is [v8.8.107 updater-main-522](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-522), source `a1e9078b9b9a3a52106a76341c7ea79f7b1c4630`, ZIP SHA-256 `272cfe9f77dd7976148c76febd82e9fbbf4e4fbeff7ac8fd4cea8f61bae30695`. It **does not contain PR #823**. Version 8.8.108 is a candidate until new immutable publication and exact release evidence are independently observed.

## Verification boundary

**Canonical state:** PR #823 implementation source `main@5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7` passed [main Windows Release Gate 43708380211](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43708380211) **26/26, zero failures and zero trace gaps**. This exact run built an 8.8.107 source candidate but *intentionally skipped publishing* because immutable updater-main-522 was already published from older source. It did not publish or install v8.8.108. PR #824 metadata still needs its own exact-head gate and distinct immutable release; an evidence-only commit is not the tested source.

## Release owner / next verified action

1. Preserve the proven preceding `main@5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7` [Windows Release Gate 43708380211](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43708380211) result: completed success, 26/26; publication skipped for already immutable 8.8.107.
2. Verify exact v8.8.108 release-metadata-only PR head using native Windows Feature, README timestamp, Product Security, Toolbox Ownership and AutoUpdate. If main moves, reconcile without force and repeat invalidated checks. Respect protected expected-head/base merge and one release owner.
3. Run fresh exact-main Windows Release Gate 26/26; independently confirm **new** immutable v8.8.108 Windows ZIP and update-manifest.json, source SHA, both digests, and public updater feed parity. Preserve updater-main-522 and earlier releases.
4. Persist allowed source-bound verification closure. Neither a source test, successful release artifact, nor skipped updater E2E proves physical Heaven2 installation, UI interaction, or rollback.

Open acceptance: [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) actual Sifu/Stellar Blade user corpus and deliberate confirmed child grouping, packaging, and installed WPF smoke. [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) requires isolated Windows user/profile for positive installed updater/rollback; never clear or repurpose the occupied real updater root. [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) release handoff automation remains an independent systemic issue.

Preserve all existing source, CI, release, privacy, and ownership boundaries. Every next agent must recursively pass the continuity constitution and active Learned Rules onward again.
