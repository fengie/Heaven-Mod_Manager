# v8.8.141 - immutable release verified

Canonical source: `fengie/Heaven-Mod_Manager@main` at `ec0c3fb86da137e4e3a927bc8cc94ff3ab9f5785`. Public immutable [updater-main-639](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-639) contains this exact source. Bootstrap `fengie/heaven-toolbox@main` before other work.

Release evidence is a snapshot. Follow [Resuming release work](AGENTS.md#resuming-release-work) to verify the intended build before scheduling acceptance. Bootstrap fengie/heaven-toolbox@main and preserve recursive continuity.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- **v8.8.141 source/integration work:** [#973](https://github.com/fengie/Heaven-Mod_Manager/pull/973) adds stage-only Crimson DMM intake, shared explicit game-root deployment admission, pre-journal protection against external edits to any owned live file, and hosted UI Automation/provider fixture resiliency. Real PAZ/ASI/game loader writes remain blocked. Verify exact-head WPF/Feature/Startup/AutoUpdate, guarded main integration, new immutable release and hosted updater/rollback; physical Heaven2 and game 2.05.01+ loader runtime remain unverified. [Research #972](https://github.com/fengie/Heaven-Mod_Manager/issues/972)

- **v8.8.140:** [#969](https://github.com/fengie/Heaven-Mod_Manager/pull/969) merged `b7a7cd51`; gates 45161454045/45161454063/45161454455 PASS. Known-size catalog downloads check destination-volume free bytes plus 256 MiB before writing. immutable v8.8.140 updater-main-637 was published from 3899c7cb; its hosted rollback and personal H2 still need source-bound acceptance.

- **v8.8.139 published history:** #904 verified same-game archive reuse, #962 reparse containment, #963 lease-backed import recovery, and #967 pending-intent recovery. Immutable updater-main-635 from `4de235c1` passed Windows Release Gate 45153233938 and real hosted update/rollback 45154773464. This does not establish personal Heaven2 acceptance; keep #669, #793 and #912 open.

- The **Installed files** tab lists read-only mod evidence from registered games, previous installations, Steam Workshop, and discoverable standalone game folders. Filter the list by game, location, or evidence.
- The inventory identifies backed Workshop records, known external loader footprints, and Unreal mod containers. Local file evidence does not establish that the game loads a mod. Verify uncertain entries before making changes.
- Independently selectable Unreal packages in existing library folders now appear as disabled catalog entries. Existing choices and original sources are preserved. Collections with unresolved variants or companion files still require review. [#926](https://github.com/fengie/Heaven-Mod_Manager/pull/926)
- The real-library source test found both active and older installs, removed 507 stock-file review entries, and retained the expected Workshop and loader evidence. [Inventory change](https://github.com/fengie/Heaven-Mod_Manager/pull/925)

- Empty local folders no longer create catalog entries. Existing rows keep their saved state and show an actionable error if their selected source folder is empty. [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919)
- Nested, project-qualified Unreal packages can be identified independently. Ordinary loose PAK groups use the game's mod subdirectory on supported profiles. Explicit mirrored destinations stay intact, and incomplete or ambiguous packages require review. [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920)
- To repair an older package index, filter the Mods list to the intended packages and use the scoped index rebuild. Check the matching game paths before choosing a reviewed package.

- Historical v8.8.118 updater proof and closure are retained in [PR #899](https://github.com/fengie/Heaven-Mod_Manager/pull/899).



## Verification boundary

Current hosted-Windows closure: v8.8.141 source `ec0c3fb86da137e4e3a927bc8cc94ff3ab9f5785` passed run `45376714698` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.141-heaven-windows-closure.log`.

Verified immutable GitHub release: [updater-main-639](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-639), exact source `ec0c3fb86da137e4e3a927bc8cc94ff3ab9f5785`, ZIP SHA-256 `76b3c22022bc7683e08e84585dc292b6c447337385ead021ea81345859a11316` and manifest SHA-256 `468b694e99af492bccb649f2c4f8245eb812670189406ae35a29b332ee4bd544`. Public release and tag ref independently match the built artifact.

The tested source remains `ec0c3fb86da137e4e3a927bc8cc94ff3ab9f5785` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) retains genuine hundreds-mod Sifu/Stellar Blade/MHW corpus, large solid RAR/7z, nested mixed-game, installed WPF Explorer drag/drop/progress/cancel and p50/p95 acceptance. Small genuine RAR4/RAR5/7z format regressions merged via #850 and passed [main Windows Release Gate 44047541323](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44047541323); they do not prove these larger workloads.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks installed verification. Build 603 has hosted and personal installed proof. v8.8.121 installed health and bounded endurance remain open.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) source-level release continuity repair [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) is integrated at `main@687607cfa3466bd2f55f29f0a333cceaf4c4b4ec` after all five exact-head gates passed. Bounded source/receipt and next-step sync now reject stale candidate preambles; keep root README/CHANGELOG publication-neutrality for the next serialized version, and distinguish packaged release from physical installation.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.

For Iceborne armor recovery, see PR #913.
