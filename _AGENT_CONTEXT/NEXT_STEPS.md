# Heaven Mod Manager — ordered next actions

Canonical source/verification state: [CURRENT_REVISION.json](CURRENT_REVISION.json). Current product work and owners: [PROJECT_PLAN.md](PROJECT_PLAN.md). This is a compact successor queue, not a replacement for those sources.

## Current release acceptance

Verified immutable [v8.8.112 updater-main-548](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-548) targets source ``e9b1212985f80533ce04e3ab1e43910290692687``; hosted Windows release verification run ``44044388877`` passed. Publication does not establish installation or healthy execution on Heaven2.

## Persistent product acceptance

1. [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): verify large real RAR/7z and mixed Sifu/Stellar Blade/MHW collections, unknown-game review, real Explorer drag/drop and bounded recovery.
2. [#735](https://github.com/fengie/Heaven-Mod_Manager/issues/735): measure installed-WPF p50/p95 with 100–10,000 mods, with correctness/memory baselines before optimizing.
3. [#669](https://github.com/fengie/Heaven-Mod_Manager/issues/669): prove safe bounded storage reclamation, crash/cancel endurance and test-owned cleanup.
4. [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): hosted updater 60 → 548 and rollback passed [run 44045741670](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44045741670); verify Heaven2 installed SHA/health separately and investigate earlier hosted timeouts.
5. [#786](https://github.com/fengie/Heaven-Mod_Manager/issues/786), [#350](https://github.com/fengie/Heaven-Mod_Manager/issues/350), [#354](https://github.com/fengie/Heaven-Mod_Manager/issues/354): renew provider compliance evidence and preserve external publisher/security prerequisites.

Every successor must read and preserve _AGENT_CONTEXT/CONTINUITY_PROTOCOL.md and active _AGENT_CONTEXT/LEARNED_RULES.md, and require its own successor to do the same. Do not break the chain.
