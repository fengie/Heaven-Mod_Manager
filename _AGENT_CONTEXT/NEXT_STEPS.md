# Heaven Mod Manager — ordered next actions

Canonical source and verification state: [CURRENT_REVISION.json](CURRENT_REVISION.json). Current work and ownership: [PROJECT_PLAN.md](PROJECT_PLAN.md). This is a compact successor queue, not a replacement for those sources.

## Current release acceptance

Verified immutable [v8.8.115 updater-main-579](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-579) targets exact source `caa8fd1f5801a0afb3f14bb5ea205faf119497d5`; Windows Release Gate [44455718899](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44455718899) passed; ZIP/manifest SHA-256 are published and verified. [Hosted real packaged updater E2E 44456946318](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44456946318) **PASSED** installed build 60→579, confirmed health and injected rollback to 60 with four matching sentinels. This disposable-Windows proof does not establish personal Heaven2 installation/health.

## Persistent product acceptance

1. [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): verify large real RAR/7z and mixed Sifu/Stellar Blade/MHW collections, unknown-game review, real Explorer drag/drop and bounded recovery.
2. [#735](https://github.com/fengie/Heaven-Mod_Manager/issues/735): measure installed-WPF p50/p95 with 100–10,000 mods and correctness/memory baselines before optimizing.
3. [#669](https://github.com/fengie/Heaven-Mod_Manager/issues/669): prove safe bounded storage reclamation, crash/cancel endurance and test-owned cleanup.
4. [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): hosted packaged updater build 60 → 579 and injected rollback **PASSED** in [run 44456946318](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44456946318); PR #886 source-bound evidence and PR #887 release handoff are merged; independently verify personal Heaven2 installed source/health and investigate earlier hosted timeouts without treating skipped classifier-only reruns as full update/rollback proof.
5. [#558](https://github.com/fengie/Heaven-Mod_Manager/issues/558) and the Browse UX queue: finish provider-compliant discovery and catalog-scale acceptance; preserve offline/partial-failure behavior.
6. [#709](https://github.com/fengie/Heaven-Mod_Manager/issues/709), RECOVERY-005 and RECOVERY-007: revisit dependency lock files and installed Windows UI/game-discovery acceptance without reopening already integrated code.
7. [#786](https://github.com/fengie/Heaven-Mod_Manager/issues/786), [#350](https://github.com/fengie/Heaven-Mod_Manager/issues/350), [#354](https://github.com/fengie/Heaven-Mod_Manager/issues/354): renew provider compliance evidence and preserve external publisher/security prerequisites.
8. Preserve existing privacy/legal/accessibility controls. Accounts or hosted personal data, analytics or ads, payment, tracking/cookies, marketing, minors' personal data, telemetry uploads and new bundled assets trigger a renewed review before release.

Every successor must read and preserve _AGENT_CONTEXT/CONTINUITY_PROTOCOL.md and active _AGENT_CONTEXT/LEARNED_RULES.md, and require its own successor to do the same. Do not break the chain.
