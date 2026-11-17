# v8.8.112 — Inbox progress and code coverage, serialized release candidate

Canonical source: `fengie/Heaven-Mod_Manager@main` at `e9d8c09bfeefe452eecad989aca7c612a965b57d`, integrating #844 and #846 after exact native CI. Previous immutable [v8.8.111 updater-main-537](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-537) was published from `1e24b3519d56471bcb3995ab6b70ae9f6c09d00a` and excludes both changes. Bootstrap current `fengie/heaven-toolbox@main`, reconcile owner/main/PRs and serialized release slot before mutation.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- #844 Inbox progress/phase-tracing/WPF responsive overlay merged `main@e5147f0a8345b6d2f35128c92fdb64f9b7ba58fd` after [Feature 44028036684](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44028036684), with [main Gate 44028954968](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44028954968) PASS. Real client p95 and multi-archive wall time are unmeasured.
- #846 added opt-in MTP code coverage, 19-feature inventory, functional ZIP route/index/dedup and fresh-state Inbox benchmark **source**. Integrated `main@e9d8c09bfeefe452eecad989aca7c612a965b57d` after [Feature 44029013896](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44029013896) PASS, [Product Security 44029014036](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44029014036) PASS and exact `enforce`. Collection percentages and macrobenchmark timing are not yet observed.
- Previous public immutable [v8.8.111 updater-main-537](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-537) targets exact source `1e24b3519d56471bcb3995ab6b70ae9f6c09d00a`; do not force/overwrite it. v8.8.112 requires a distinct signed immutable build.




Historical #832/#835/#836/#838/#839 source, tests, dependencies and release identities remain in CHANGELOG.md, PROJECT_PLAN.md and verified prior GitHub releases; the current handoff is scoped to #844/#846 and v8.8.112.

## Verification boundary

1. This v8.8.112 metadata candidate requires exact-head Windows Feature, README timestamp, Product Security, Toolbox Ownership, AutoUpdate and strict `enforce`, then protected expected-head merge on fresh main.
2. Await all preceding canonical main Windows Release Gate jobs; preserve immutable v8.8.111 `updater-main-537`. The new source at `e9d8c09bfeefe452eecad989aca7c612a965b57d` and earlier published build 537 are distinct proof boundaries.
3. Run new exact-main 26/26 Windows Release Gate for the new version, verify build/test/provenance, public immutable v8.8.112 tag/source/ZIP/manifest hashes and latest updater feed independently.
4. [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) verified isolated Windows real updater+WPF selector/rollback on build 537 in [hosted run 44001412839](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44001412839) and [run 44003492684](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44003492684); separate run 44002248099 timed out, so repeatability is not proven. New build and Heaven2 installed health remain unverified.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) still requires actual large RAR/solid RAR5/7z mod packs, deeply nested mixed-game collections and installed WPF Explorer drag/drop/progress/cancel. ZIP-only synthetic tests do not close it.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): published build 537 updater/selector/rollback acceptance passed twice on disposable Windows; one timeout also observed. New build/Heaven2 runtime requires separate validation. Never clear shared occupied updater state.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) tracks accurate post-release continuity and artifact proof. Distinguish source verified, artifact published and physically installed.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.
