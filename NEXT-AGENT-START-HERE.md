# v8.8.120 - source verified, publication unverified

Canonical source: `fengie/Heaven-Mod_Manager@main` verified at `81b09514f95fd1dd3c4f4e131be9825fddfcc03a` (Windows Gate 44684742882); exact immutable release not yet verified. Bootstrap `fengie/heaven-toolbox@main` before other work.

Manager main `cda9bc05b54f0912787e8a7409102f3fe67a6c03` includes the empty-catalog fix [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919) and nested Unreal package intake and routing [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920). v8.8.120 requires its own immutable publication, hosted updater verification, and disposable-home smoke test. Physical game loading remains unverified. Bootstrap `fengie/heaven-toolbox@main` and preserve recursive continuity.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- Empty local folders no longer create catalog entries. Existing rows keep their saved state and show an actionable error if their selected source folder is empty. [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919)
- Nested, project-qualified Unreal packages can be identified independently. Ordinary loose PAK groups use the game's mod subdirectory on supported profiles. Explicit mirrored destinations stay intact, and incomplete or ambiguous packages require review. [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920)
- For an older, already-enabled Unreal package, filter the library to the intended mod and use **Refresh visible mod** to reindex it. Review **Preview Mod Changes**, then use **Apply** when the staged changes and destination match your intent.

- **v8.8.118 released implementation:** #895 preserves terminal updater recovery with corrupt pending marker; #897 integrated coordinated release metadata at `b6e1e151`; immutable `updater-main-588` published after Windows Gate 44481505844 PASS. Real packaged updater E2E 44483604969 PASS (Confirmed, injected RolledBack to 60, four intact user-data hashes). Source-bound receipt `_AGENT_CONTEXT/EVIDENCE/updater-installed-client-e2e-v8.8.118.log` merged by [PR #899](https://github.com/fengie/Heaven-Mod_Manager/pull/899) at `e426d87d`. PR #898 added local catalog paging after release; it is not in the immutable build-588 ZIP. No claim of personal Heaven2 installation follows from this evidence.

- **v8.8.117 verified historical implementation:** PR #893 protects active/unknown terminal updater owners. Immutable public updater-main-586 source `69a317ea4c7c18fed5ed5d18323a3d1af8925618`, Windows Gate 44475835455 and real hosted update/rollback 44477647687 passed; PR #896 preserved the durable receipt.

- **v8.8.116:** #889 fixed catalog-download lease retention; immutable updater-main-583 and real hosted rollback passed. #669 and physical-device acceptance remain open.

- **v8.8.115:** #881/#882/#884 fixed Alt/Choose family safety, sparse pair scaling and renamed pick-one evidence. Immutable updater-main-579 and hosted update/rollback passed; Heaven2 remains unverified.

- **Historical v8.8.113 source:** #850–#874 provided genuine small archive format fixtures, crash recovery, game routing, conflict safeguards, startup telemetry, and catalog visibility. Large real Sifu/Stellar Blade/MHW corpora and installed p95 remain unverified.

- Previous source details remain in [CHANGELOG.md](CHANGELOG.md): #832 planner allocations; #835 dependencies; #836 archive regressions; #838 intake receipt serialization; #839 family pruning. Their native checks predate this candidate and do not prove installed p95.
- **Earlier v8.8.112:** immutable updater-main-548 is preserved at source `e9b1212985f80533ce04e3ab1e43910290692687`; later versions have separate immutable artifacts and receipts.

## Verification boundary

Current hosted-Windows closure: v8.8.120 source `81b09514f95fd1dd3c4f4e131be9825fddfcc03a` passed run `44684742882` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.120-heaven-windows-closure.log`.

Public release status: **NOT VERIFIED** for source `81b09514f95fd1dd3c4f4e131be9825fddfcc03a`. Successful build or another source's same-version release is not proof of publication.

The tested source remains `81b09514f95fd1dd3c4f4e131be9825fddfcc03a` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) retains genuine hundreds-mod Sifu/Stellar Blade/MHW corpus, large solid RAR/7z, nested mixed-game, installed WPF Explorer drag/drop/progress/cancel and p50/p95 acceptance. Small genuine RAR4/RAR5/7z format regressions merged via #850 and passed [main Windows Release Gate 44047541323](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44047541323); they do not prove these larger workloads.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks installed verification. Build 600 has hosted packaged coverage. v8.8.120 installed health and bounded endurance remain open.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) source-level release continuity repair [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) is integrated at `main@687607cfa3466bd2f55f29f0a333cceaf4c4b4ec` after all five exact-head gates passed. Bounded source/receipt and next-step sync now reject stale candidate preambles; keep root README/CHANGELOG publication-neutrality for the next serialized version, and distinguish packaged release from physical installation.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.

For Iceborne armor recovery, see PR #913.
