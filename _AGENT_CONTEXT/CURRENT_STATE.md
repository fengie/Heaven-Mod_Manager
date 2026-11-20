# v8.8.124 - immutable release verified

Canonical state: `fengie/Heaven-Mod_Manager@main` at `fc99e48270feb2c7e15df39f79733e000d4ba8a7`. Public immutable [updater-main-612](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-612) contains this exact source. Bootstrap `fengie/heaven-toolbox@main` before other work.

Build 611 is the published v8.8.123 package from ccb8df449b0bcb0f7487224670316a3aae2b7813. The v8.8.124 mixed-import candidate preserves the registered game folder in both archive import paths. Bootstrap `fengie/heaven-toolbox@main` and preserve recursive continuity.

## Integrated implementation

- Bulk inbox processing and explicit archive import keep the registered game folder when unpacking mods. In a 120-package mixed collection, all 1,440 payloads indexed at their expected game paths. [Acceptance test](https://github.com/fengie/Heaven-Mod_Manager/issues/791#issuecomment-7034527877)

- The Mods list now shows verified BepInEx plugins and ReShade installations in the registered game. Search by plugin name, ID or source path. Exact DLL copies already in a saved package appear once, including disabled packages that have never been indexed. Different DLLs can share a plugin ID and remain distinct. [#930](https://github.com/fengie/Heaven-Mod_Manager/pull/930)

- The Mods list now includes Steam Workshop items with a matching local Steam install record and item directory for the registered game. Search by item ID or source path. Steam manages these items. Manager bulk enable, reindex and deployment continue to operate on Manager-owned packages. [#928](https://github.com/fengie/Heaven-Mod_Manager/pull/928)

- The **Installed files** tab lists read-only mod evidence from registered games, previous installations, Steam Workshop, and discoverable standalone game folders. Filter the list by game, location, or evidence.
- The inventory identifies backed Workshop records, known external loader footprints, and Unreal mod containers. Local file evidence does not establish that the game loads a mod. Verify uncertain entries before making changes.
- Independently selectable Unreal packages in existing library folders now appear as disabled catalog entries. Existing choices and original sources are preserved. Collections with unresolved variants or companion files still require review. [#926](https://github.com/fengie/Heaven-Mod_Manager/pull/926)
- The real-library source test found both active and older installs, removed 507 stock-file review entries, and retained the expected Workshop and loader evidence. [Inventory change](https://github.com/fengie/Heaven-Mod_Manager/pull/925)

- Empty local folders no longer create catalog entries. Existing rows keep their saved state and show an actionable error if their selected source folder is empty. [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919)
- Nested, project-qualified Unreal packages can be identified independently. Ordinary loose PAK groups use the game's mod subdirectory on supported profiles. Explicit mirrored destinations stay intact, and incomplete or ambiguous packages require review. [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920)
- For an older, already-enabled Unreal package, filter the library to the intended mod and use **Refresh visible mod** to reindex it. Review **Preview Mod Changes**, then use **Apply** when the staged changes and destination match your intent.

- **v8.8.118 published:** #895 protects updater recovery when pending state is malformed. Immutable build 588 source `b6e1e151b43323218041b80204dee070002c297b` passed Windows Gate 44481505844 and real hosted packaged update/rollback 44483604969; durable receipt merged through #899. Personal installed acceptance is separate.

- **v8.8.117 published:** #893 protects live/unknown terminal updater owner leases with red→green Windows regressions. Immutable updater-main-586 at `69a317ea4c7c18fed5ed5d18323a3d1af8925618`, Windows Gate 44475835455 and hosted real packaged update/rollback 44477647687 PASSED; receipt merged via PR #896.

- **v8.8.116 published:** PR #889 fixes finalized catalog download lease-age handling and includes regression tests. Immutable updater-main-583 uses exact source `407ae3fb6ee39b62a1451b03f3439da283ca4cc3` with Windows Gate 44468041048 and hosted real packaged update/rollback 44469075908 PASS; #669 full storage acceptance remains open.

- **v8.8.115 published:** #881 rejects Alt/Choose cross-source suggestion mistakes; #882 prunes redundant shared-root candidate pair generation; #884 shares one hyphen-safe choice vocabulary across inference routes while retaining original-name evidence. Exact-head PR checks and [canonical Windows Gate 44455718899](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44455718899) passed; source `caa8fd1f5801a0afb3f14bb5ea205faf119497d5` is included in the immutable public v8.8.115 package, but real mixed-game corpus and Heaven2 installed tests remain outstanding.

- **Historical v8.8.113 source:** #850–#874 brought real small-format RAR/7z fixtures, durable import recovery, conflict and game-routing safeguards, startup measures and catalog visibility. Real large mixed-game collections and installed p95 remain unverified.

- #844 phase-aware Inbox progress and performance reports; #846 opt-in code coverage, matrix and synthetic benchmarks; #847 hosted-Windows UIA import/preview/apply/undo/switch; #848 automatic approved active-game manual bulk-import completion and Mods refresh. All integrated at `main@62f3dee97e1e6ed56bcc098b50db515e4b08b624`, with #848 exact-head Feature 44037120853 and hosted WPF UIA 44037121012 (4/4). These source changes are included in immutable [v8.8.112 updater-main-548](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-548); installation on Heaven2 is not yet verified.

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

- #839 safely rejects family pairs that cannot satisfy the existing strong-evidence predicate before root intersection/formatting, with negative identical-path and uniform-stem regressions. Synthetic same-runner 500 shared-root case: 281.974 → 223.461 ms and 507.76 → 420.2 MB allocated in three benchmark iterations; real WPF p95 remains unknown.

## Verification boundary

Current hosted-Windows closure: v8.8.124 source `fc99e48270feb2c7e15df39f79733e000d4ba8a7` passed run `44735736546` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.124-heaven-windows-closure.log`.

Verified immutable GitHub release: [updater-main-612](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-612), exact source `fc99e48270feb2c7e15df39f79733e000d4ba8a7`, ZIP SHA-256 `20e9778c0c5a5294a188a2b2bf597a45ae6442c64de0fcc55a2954eb78e192cb` and manifest SHA-256 `8d520666f1ce81427516a1cd19efcd8b1eab2ce0ddce524c0e28bd1daa76582f`. Public release and tag ref independently match the built artifact.

The tested source remains `fc99e48270feb2c7e15df39f79733e000d4ba8a7` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks installed verification. Build 603 has hosted and personal installed proof. v8.8.121 installed health and bounded endurance remain open.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) merged the deterministic source-bound release and current-next-step sync on `main@687607cf` after exact-head native Windows, ownership, product security, and AutoUpdate checks. Historical candidate prose is reconciled in the canonical documentation-only release handoff. No new product binary or version was created.

Next: integrate and publish v8.8.120 through the protected Windows release path. Confirm the new immutable source, ZIP and manifest, then verify the packaged updater and disposable-home startup. Preserve the active personal Manager session and recursively pass on the Core Rules and Learned Rules.

Auto Populate checks pre-Iceborne armor materials since v8.8.119. See product PR #913 for profile recovery and verification.
