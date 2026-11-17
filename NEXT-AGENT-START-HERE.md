# v8.8.110 planner/dependency reconciliation — serialized release candidate

Canonical source: `fengie/Heaven-Mod_Manager@main` source `8ed0a73ca4a320c49c579b8620a3b1869595d2b3` via [#832](https://github.com/fengie/Heaven-Mod_Manager/pull/832), [#835](https://github.com/fengie/Heaven-Mod_Manager/pull/835) and [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836). Bootstrap `fengie/heaven-toolbox@main`; refresh both repositories, owner leases, source/main heads and checks before acting.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- #832 planner allocation and overlay name/token caching: source candidate `5cf175d871fe51ac1895378c919c8d2d1d14784c` passed [Windows Feature 43978338548](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43978338548) and AutoUpdate 43978338567, integrated main `550d74b62c9bbc3cef8922a4e019cf5239d90bde`. Synthetic benchmarks do not measure installed-client p95.
- #835 SQLite 11.0.0 and SharpCompress 0.50.9 updates, third-party notices and missing native dependency-CI triggers: candidate `2974738085def5dc4183d7c7ad055b6545922c14` passed Windows Feature 43979469737, Product Security 43979469676, Updater Publication 43979469883, AutoUpdate 43979469626; integrated main `b9acd9b2fca75ccf5eda7627f9b10b88cc93ec56`.
- #836 deterministic bulk ZIP fault, family order-invariance and optional replay/topology benchmark sources merged at `main@8ed0a73ca4a320c49c579b8620a3b1869595d2b3` after exact-head Windows Feature 43982862135 and AutoUpdate 43982863098 passed on candidate `9cd082d94a9fcd9dc8f3993085d380ce7ad66b2c`. Actual user RAR/7z and installed WPF/rollback remain unverified.
- Previous immutable [v8.8.109 updater-main-530](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-530) targets earlier source `a29103111dd9b0e56574c50bd3fbb1baaf25d33a`. This v8.8.110 metadata remains candidate until exact checks, merge and a different immutable release.

## Verification boundary

1. [Main Windows Release Gate 43981825454](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43981825454) succeeded for #835 source. Confirm the newer [main Windows Release Gate 43983812212](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43983812212) for #836 source terminates before moving main. Both must preserve immutable 8.8.109 artifacts.
2. #836 source is already merged on main 8ed0a73. This release branch must reconcile its actual tree with that newer main, and rerun its own exact-head verification; never use earlier branch-only green evidence as the tested release head.
3. Verify release metadata with Windows Feature, README timestamp, Product Security, Toolbox Ownership and AutoUpdate; merge protected main using expected head/base, never by force or stale check.
4. Verify fresh exact-main 26/26 Windows Release Gate, distinct immutable v8.8.110 ZIP + update-manifest.json, SHA-256, exact source and public updater feed. Physical installed checks are independent.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) still requires actual large RAR/solid RAR5/7z mod packs, deeply nested mixed-game collections and installed WPF Explorer drag/drop/progress/cancel. ZIP-only synthetic tests do not close it.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) requires dedicated, least-privileged Windows account/profile with an independently empty test-owned LocalApplicationData updater root, positive packaged update + rollback evidence. Never clear or reuse the shared occupied real updater state. E2E classification success with skipped positive job is not proof.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) tracks accurate post-release continuity and artifact proof. Distinguish source verified, artifact published and physically installed.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again.
