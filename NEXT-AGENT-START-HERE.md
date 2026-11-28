# v8.8.148 — intake and recovery validation

The v8.8.148 source combines #997 and #1002–#1010 on build 658’s verified main. Verify combined native tests, immutable publication and packaged update/rollback before reporting release acceptance. Physical verification remains under #793/#946.

## v8.8.147 - immutable release verified

Canonical source: `fengie/Heaven-Mod_Manager@main` at `fb13129a1b876b6da4cf020cad8c7b9e4cc85d74`. Public immutable [updater-main-658](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-658) contains this exact source. Bootstrap `fengie/heaven-toolbox@main` before other work.

Release evidence is a snapshot. Follow [Resuming release work](AGENTS.md#resuming-release-work) to verify the intended build before scheduling acceptance. Bootstrap fengie/heaven-toolbox@main and preserve recursive continuity.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- **v8.8.147** — Explicit Apply commits staged choices. Verify publication and update/rollback under #793, then canonical Heaven2 use and preservation of primary and unmanaged bytes under #946.

- **v8.8.146 source:** [#986](https://github.com/fengie/Heaven-Mod_Manager/pull/986) integrated at `5a97cb34242c5b5bf2d1f6791a06cf7bdb6f77bd`; exact-head Feature 45704981417, Startup Performance 45704981205, packaged WPF 45704981295, Product Security 45704981433 and AutoUpdate 45704981269 passed. [Main Windows Gate 45712238130](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/45712238130) passed verification/build; publication skipped for the unchanged version. The subsequent updater workflow classified no new publication and skipped real update/rollback.

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

Current hosted-Windows closure: v8.8.147 source `fb13129a1b876b6da4cf020cad8c7b9e4cc85d74` passed run `46086319245` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.147-heaven-windows-closure.log`.

Verified immutable GitHub release: [updater-main-658](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-658), exact source `fb13129a1b876b6da4cf020cad8c7b9e4cc85d74`, ZIP SHA-256 `e98e551429dca4c0f340fa39d34fbf09f309f431698ee47dbade15be6bb04727` and manifest SHA-256 `82e80e9d5ac34316c28c33934eb133960642d481000e396223a44835e89a6ba5`. Public release and tag ref independently match the built artifact.

The tested source remains `fb13129a1b876b6da4cf020cad8c7b9e4cc85d74` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) retains genuine hundreds-mod Sifu/Stellar Blade/MHW corpus, large solid RAR/7z, nested mixed-game, installed WPF Explorer drag/drop/progress/cancel and p50/p95 acceptance. Small genuine RAR4/RAR5/7z format regressions merged via #850 and passed [main Windows Release Gate 44047541323](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44047541323); they do not prove these larger workloads.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks personal H2 installation and bounded endurance; build 649 hosted success is not current personal proof.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) source-level release continuity repair [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) is integrated at `main@687607cfa3466bd2f55f29f0a333cceaf4c4b4ec` after all five exact-head gates passed. Bounded source/receipt and next-step sync now reject stale candidate preambles; keep root README/CHANGELOG publication-neutrality for the next serialized version, and distinguish packaged release from physical installation.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.

For Iceborne armor recovery, see PR #913.
