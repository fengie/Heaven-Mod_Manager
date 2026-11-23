# v8.8.140 — catalog download capacity release candidate

Release evidence is a snapshot. Follow [Resuming release work](AGENTS.md#resuming-release-work) to verify the intended build before scheduling acceptance. Bootstrap fengie/heaven-toolbox@main and preserve recursive continuity.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- **v8.8.140:** [#969](https://github.com/fengie/Heaven-Mod_Manager/pull/969) merged `b7a7cd51`; gates 45161454045/45161454063/45161454455 PASS. Known-size catalog downloads check destination-volume free bytes plus 256 MiB before writing. v8.8.140 release/rollback and personal H2 remain pending.

- **v8.8.139 published history:** #904 verified same-game archive reuse, #962 reparse containment, #963 lease-backed import recovery, and #967 pending-intent recovery. Immutable updater-main-635 from `4de235c1` passed Windows Release Gate 45153233938 and real hosted update/rollback 45154773464. This does not establish personal Heaven2 acceptance; keep #669, #793 and #912 open.

- The **Installed files** tab lists read-only mod evidence from registered games, previous installations, Steam Workshop, and discoverable standalone game folders. Filter the list by game, location, or evidence.
- The inventory identifies backed Workshop records, known external loader footprints, and Unreal mod containers. Local file evidence does not establish that the game loads a mod. Verify uncertain entries before making changes.
- Independently selectable Unreal packages in existing library folders now appear as disabled catalog entries. Existing choices and original sources are preserved. Collections with unresolved variants or companion files still require review. [#926](https://github.com/fengie/Heaven-Mod_Manager/pull/926)
- The real-library source test found both active and older installs, removed 507 stock-file review entries, and retained the expected Workshop and loader evidence. [Inventory change](https://github.com/fengie/Heaven-Mod_Manager/pull/925)

- Empty local folders no longer create catalog entries. Existing rows keep their saved state and show an actionable error if their selected source folder is empty. [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919)
- Nested, project-qualified Unreal packages can be identified independently. Ordinary loose PAK groups use the game's mod subdirectory on supported profiles. Explicit mirrored destinations stay intact, and incomplete or ambiguous packages require review. [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920)
- To repair an older package index, filter the Mods list to the intended packages and use the scoped index rebuild. Check the matching game paths before choosing a reviewed package.

- Historical v8.8.118 updater proof and closure are retained in [PR #899](https://github.com/fengie/Heaven-Mod_Manager/pull/899).

- **Historical v8.8.113 source:** #850â€“#874 provided genuine small archive format fixtures, crash recovery, game routing, conflict safeguards, startup telemetry, and catalog visibility. Large real Sifu/Stellar Blade/MHW corpora and installed p95 remain unverified.

- Previous source details remain in [CHANGELOG.md](CHANGELOG.md): #832 planner allocations; #835 dependencies; #836 archive regressions; #838 intake receipt serialization; #839 family pruning. Their native checks predate this candidate and do not prove installed p95.
- **Earlier v8.8.112:** immutable updater-main-548 is preserved at source `e9b1212985f80533ce04e3ab1e43910290692687`; later versions have separate immutable artifacts and receipts.

## Verification boundary

Published [v8.8.139 build 635](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-635), source `4de235c1b632c37f34f4a851a98da369c6a3c136`: [Windows Gate 45153233938](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/45153233938) and [actual packaged updater/rollback E2E 45154773464](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/45154773464) both passed. These are hosted release checks, not physical Heaven2 adoption. The earlier [H2 build-626 installed receipt](https://github.com/fengie/Heaven-Mod_Manager/issues/946#issuecomment-7066456396) and isolated 1,002-file archive readback remain historical proof only.

For earlier source-bound release evidence, see [CHANGELOG.md](CHANGELOG.md) and [PR #899](https://github.com/fengie/Heaven-Mod_Manager/pull/899).

**Not established by hosted proof:** personal physical Heaven2 installed binary/version/health; bounded endurance/recovery distribution; real mixed-game archive UI and p95; provider authorization, independent signing and judiciary strict-mode enforcement. Reuse [Toolbox #1001](https://github.com/fengie/heaven-toolbox/issues/1001#issuecomment-7003951728) for the existing authenticated read-only Heaven2 observation capability request; never bypass host custody or modify personal updater state for the sake of proof.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) retains genuine hundreds-mod Sifu/Stellar Blade/MHW corpus, large solid RAR/7z, nested mixed-game, installed WPF Explorer drag/drop/progress/cancel and p50/p95 acceptance. Small genuine RAR4/RAR5/7z format regressions merged via #850 and passed [main Windows Release Gate 44047541323](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44047541323); they do not prove these larger workloads.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks installed verification. Build 603 has hosted and personal installed proof. v8.8.121 installed health and bounded endurance remain open.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) source-level release continuity repair [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) is integrated at `main@687607cfa3466bd2f55f29f0a333cceaf4c4b4ec` after all five exact-head gates passed. Bounded source/receipt and next-step sync now reject stale candidate preambles; keep root README/CHANGELOG publication-neutrality for the next serialized version, and distinguish packaged release from physical installation.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.

For Iceborne armor recovery, see PR #913.
