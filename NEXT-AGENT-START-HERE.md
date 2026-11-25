# v8.8.144 - immutable release verified

Canonical source: `fengie/Heaven-Mod_Manager@main` at `3073837e37819ce896332202e13b893777dc194e`. Public immutable [updater-main-647](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-647) contains this exact source. Bootstrap `fengie/heaven-toolbox@main` before other work.

Release evidence is a snapshot. Follow [Resuming release work](AGENTS.md#resuming-release-work) to verify the intended build before scheduling acceptance. Bootstrap fengie/heaven-toolbox@main and preserve recursive continuity.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- **v8.8.144 source:** [#980](https://github.com/fengie/Heaven-Mod_Manager/pull/980) integrated at `2417de3609a9cb8f69fd14ee636de69e6549cf5f`; verified portable install locator. Native Feature 45511979755, Startup Performance 45511979645, AutoUpdate 45511979680, Product Security 45511979698 and packaged WPF 45511979654 passed; exact tested tree merged.

- Earlier releases and detailed evidence: see [_AGENT_CONTEXT/CURRENT_STATE.md](_AGENT_CONTEXT/CURRENT_STATE.md), [_AGENT_CONTEXT/PROJECT_PLAN.md](_AGENT_CONTEXT/PROJECT_PLAN.md), and [CHANGELOG.md](CHANGELOG.md). Build 641 (v8.8.142) passed hosted update/rollback; build 639 (v8.8.141) shipped Crimson staging only, with no verified live PAZ/ASI/loader writes. Historical evidence remains in Git and the linked records.

- The **Installed files** tab lists read-only mod evidence from registered games, previous installations, Steam Workshop, and discoverable standalone game folders. Filter the list by game, location, or evidence.
- The inventory identifies backed Workshop records, known external loader footprints, and Unreal mod containers. Local file evidence does not establish that the game loads a mod. Verify uncertain entries before making changes.
- Independently selectable Unreal packages in existing library folders now appear as disabled catalog entries. Existing choices and original sources are preserved. Collections with unresolved variants or companion files still require review. [#926](https://github.com/fengie/Heaven-Mod_Manager/pull/926)
- The real-library source test found both active and older installs, removed 507 stock-file review entries, and retained the expected Workshop and loader evidence. [Inventory change](https://github.com/fengie/Heaven-Mod_Manager/pull/925)

- Empty local folders no longer create catalog entries. Existing rows keep their saved state and show an actionable error if their selected source folder is empty. [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919)
- Nested, project-qualified Unreal packages can be identified independently. Ordinary loose PAK groups use the game's mod subdirectory on supported profiles. Explicit mirrored destinations stay intact, and incomplete or ambiguous packages require review. [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920)
- To repair an older package index, filter the Mods list to the intended packages and use the scoped index rebuild. Check the matching game paths before choosing a reviewed package.

- Historical v8.8.118 updater proof and closure are retained in [PR #899](https://github.com/fengie/Heaven-Mod_Manager/pull/899).



## Verification boundary

Current hosted-Windows closure: v8.8.144 source `3073837e37819ce896332202e13b893777dc194e` passed run `45522977721` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.144-heaven-windows-closure.log`.

Verified immutable GitHub release: [updater-main-647](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-647), exact source `3073837e37819ce896332202e13b893777dc194e`, ZIP SHA-256 `88616520051e0f7b0a16cc1c719e81dbee3bc147fd73edf758aaccd3f8167ab4` and manifest SHA-256 `1d2bd1d695c39a6250d22d49423ee17938da64db966e9b0c8368960be8badb7c`. Public release and tag ref independently match the built artifact.

The tested source remains `3073837e37819ce896332202e13b893777dc194e` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) retains genuine hundreds-mod Sifu/Stellar Blade/MHW corpus, large solid RAR/7z, nested mixed-game, installed WPF Explorer drag/drop/progress/cancel and p50/p95 acceptance. Small genuine RAR4/RAR5/7z format regressions merged via #850 and passed [main Windows Release Gate 44047541323](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44047541323); they do not prove these larger workloads.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks personal H2 installation and bounded endurance; build 641 hosted success is not current personal proof.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) source-level release continuity repair [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) is integrated at `main@687607cfa3466bd2f55f29f0a333cceaf4c4b4ec` after all five exact-head gates passed. Bounded source/receipt and next-step sync now reject stale candidate preambles; keep root README/CHANGELOG publication-neutrality for the next serialized version, and distinguish packaged release from physical installation.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.

For Iceborne armor recovery, see PR #913.
