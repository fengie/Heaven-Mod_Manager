# v8.8.145 - immutable release verified

Canonical state: `fengie/Heaven-Mod_Manager@main` at `b611408ea33997a7e7f9a500864aff5b5aee7225`. Public immutable [updater-main-649](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-649) contains this exact source. Bootstrap `fengie/heaven-toolbox@main` before other work.

## Integrated implementation

- **v8.8.145 — #985:** executable and staged-deployment launch validation, bounded texture suggestions and reviewed overwrite semantics, explicit batch marks/activation, committed Enabled/Disabled filters. Exact native source checks passed; physical game launch, layering and benchmark acceptance remain open.

- **v8.8.144 — #980:** retain a verified same-account portable install discovery receipt after healthy startup, reject noncanonical, staging and unverifiable locations, and preserve previous receipt on cancellation. Discovery is not health proof. #981 superseded with its branch preserved.

- **v8.8.143 — #978:** propagate caller cancellation through provider failure diagnostics and best-effort failure persistence, while preserving isolated provider-local timeout behavior and existing compliance holds.

- **v8.8.142 source — #975:** bounded 128 KiB coalescing, pre-write 256 MiB staging-volume headroom checks, original 1 GiB archive limit and lease cleanup. Red-to-green three-case native integration proof, native Feature 45387115331, Startup Performance 45387115440, AutoUpdate 45387115373 passed. Point-in-time capacity observations are not volume reservations.

- **v8.8.141 source — #973:** game-scoped Crimson Desert staging, DMM V3/V3.1 read-only semantic preview, single game-root direct-file admission policy, protection against external edits to manager-owned files, and hosted WPF/provider test retries. Preserve legacy MHW/Unreal/BepInEx/configured generic game behavior. Exact versioned release/rollback and personal H2/Crimson runtime must be separately verified.

- **v8.8.140 catalog acquisition preflight source:** #969 checks actual free space for known archive bytes plus 256 MiB before HTTP payload writes. Windows Feature 45161454045, Startup Performance 45161454063 and AutoUpdate 45161454455 passed. Immutable new release, hosted rollback and physical H2 remain unverified.

- **v8.8.139 storage source:** #904 adds per-game digest-verified archive reuse under alias titles; #962 rejects reparse ancestors; #963 introduces certified lease-owned import scratch and safe exited-owner reclaim integrated into Settings, with active/unowned data preserved. Native source tests, publisher and H2 acceptance must be checked against exact source/artifact separately.


- Browse Mods cached-search input with no FTS5 term returns zero matches instead of a malformed query or full-catalog browse. The Unicode61 L/N/private-use code-point regression is in PR #960. Real provider coverage, UI p95 and physical installation remain separately open under #558.

- Bulk inbox processing and explicit archive import keep the registered game folder when unpacking mods. In a 120-package mixed collection, all 1,440 payloads indexed at their expected game paths. [Acceptance test](https://github.com/fengie/Heaven-Mod_Manager/issues/791#issuecomment-7034527877)

- The Mods list now shows verified BepInEx plugins and ReShade installations in the registered game. Search by plugin name, ID or source path. Exact DLL copies already in a saved package appear once, including disabled packages that have never been indexed. Different DLLs can share a plugin ID and remain distinct. [#930](https://github.com/fengie/Heaven-Mod_Manager/pull/930)

- The Mods list now includes Steam Workshop items with a matching local Steam install record and item directory for the registered game. Search by item ID or source path. Steam manages these items. Manager bulk enable, reindex and deployment continue to operate on Manager-owned packages. [#928](https://github.com/fengie/Heaven-Mod_Manager/pull/928)

- The **Installed files** tab lists read-only mod evidence from registered games, previous installations, Steam Workshop, and discoverable standalone game folders. Filter the list by game, location, or evidence.
- The inventory identifies backed Workshop records, known external loader footprints, and Unreal mod containers. Local file evidence does not establish that the game loads a mod. Verify uncertain entries before making changes.
- Independently selectable Unreal packages in existing library folders now appear as disabled catalog entries. Existing choices and original sources are preserved. Collections with unresolved variants or companion files still require review. [#926](https://github.com/fengie/Heaven-Mod_Manager/pull/926)
- The real-library source test found both active and older installs, removed 507 stock-file review entries, and retained the expected Workshop and loader evidence. [Inventory change](https://github.com/fengie/Heaven-Mod_Manager/pull/925)

- Empty local folders no longer create catalog entries. Existing rows keep their saved state and show an actionable error if their selected source folder is empty. [#919](https://github.com/fengie/Heaven-Mod_Manager/pull/919)
- Nested, project-qualified Unreal packages can be identified independently. Ordinary loose PAK groups use the game's mod subdirectory on supported profiles. Explicit mirrored destinations stay intact, and incomplete or ambiguous packages require review. [#920](https://github.com/fengie/Heaven-Mod_Manager/pull/920)
- To repair an older package index, filter the Mods list to the intended packages and use the scoped index rebuild. Check the matching game paths before choosing a reviewed package.

- **v8.8.118 published:** #895 protects updater recovery when pending state is malformed. Immutable build 588 source `b6e1e151b43323218041b80204dee070002c297b` passed Windows Gate 44481505844 and real hosted packaged update/rollback 44483604969; durable receipt merged through #899. Personal installed acceptance is separate.

- **v8.8.117 published:** #893 protects live/unknown terminal updater owner leases with redâ†’green Windows regressions. Immutable updater-main-586 at `69a317ea4c7c18fed5ed5d18323a3d1af8925618`, Windows Gate 44475835455 and hosted real packaged update/rollback 44477647687 PASSED; receipt merged via PR #896.

- **v8.8.116 published:** PR #889 fixes finalized catalog download lease-age handling and includes regression tests. Immutable updater-main-583 uses exact source `407ae3fb6ee39b62a1451b03f3439da283ca4cc3` with Windows Gate 44468041048 and hosted real packaged update/rollback 44469075908 PASS; #669 full storage acceptance remains open.

- **v8.8.115 published:** #881 rejects Alt/Choose cross-source suggestion mistakes; #882 prunes redundant shared-root candidate pair generation; #884 shares one hyphen-safe choice vocabulary across inference routes while retaining original-name evidence. Exact-head PR checks and [canonical Windows Gate 44455718899](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44455718899) passed; source `caa8fd1f5801a0afb3f14bb5ea205faf119497d5` is included in the immutable public v8.8.115 package, but real mixed-game corpus and Heaven2 installed tests remain outstanding.

- **Historical v8.8.113 source:** #850â€“#874 brought real small-format RAR/7z fixtures, durable import recovery, conflict and game-routing safeguards, startup measures and catalog visibility. Real large mixed-game collections and installed p95 remain unverified.

- #844 phase-aware Inbox progress and performance reports; #846 opt-in code coverage, matrix and synthetic benchmarks; #847 hosted-Windows UIA import/preview/apply/undo/switch; #848 automatic approved active-game manual bulk-import completion and Mods refresh. All integrated at `main@62f3dee97e1e6ed56bcc098b50db515e4b08b624`, with #848 exact-head Feature 44037120853 and hosted WPF UIA 44037121012 (4/4). These source changes are included in immutable [v8.8.112 updater-main-548](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-548); installation on Heaven2 is not yet verified.

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

- #839 safely rejects family pairs that cannot satisfy the existing strong-evidence predicate before root intersection/formatting, with negative identical-path and uniform-stem regressions. Synthetic same-runner 500 shared-root case: 281.974 â†’ 223.461 ms and 507.76 â†’ 420.2 MB allocated in three benchmark iterations; real WPF p95 remains unknown.

## Verification boundary

Current hosted-Windows closure: v8.8.145 source `b611408ea33997a7e7f9a500864aff5b5aee7225` passed run `45612139146` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.145-heaven-windows-closure.log`.

Verified immutable GitHub release: [updater-main-649](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-649), exact source `b611408ea33997a7e7f9a500864aff5b5aee7225`, ZIP SHA-256 `22e448db89f5d870269fa2bbc966c2d59d7de2d5601fc2663c6b77b97b648b1f` and manifest SHA-256 `f4b3846382de1c42dc274b927eadf70a12564bc822b8763ce3af39bc93aa6d9a`. Public release and tag ref independently match the built artifact.

The tested source remains `b611408ea33997a7e7f9a500864aff5b5aee7225` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) tracks personal H2 installation and bounded endurance; build 647 hosted acceptance is verified, not personal health.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) merged the deterministic source-bound release and current-next-step sync on `main@687607cf` after exact-head native Windows, ownership, product security, and AutoUpdate checks. Historical candidate prose is reconciled in the canonical documentation-only release handoff. No new product binary or version was created.

Next: Integrate synchronized v8.8.145 metadata, verify immutable publication and genuine packaged updater/rollback. Verify real game launch and texture layering, measure the existing texture benchmarks, and preserve #946 broader semantic and performance acceptance. Keep #986 separate until its own current-base packaged WPF gate and review pass. Preserve provider access, personal data, rollback and recursive continuity.

Auto Populate checks pre-Iceborne armor materials since v8.8.119. See product PR #913 for profile recovery and verification.
