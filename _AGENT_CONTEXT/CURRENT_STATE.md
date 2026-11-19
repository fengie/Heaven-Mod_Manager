# v8.8.118 - source verified, publication unverified

Canonical state: `fengie/Heaven-Mod_Manager@main` verified at `f1dd0dd4500ad8383ed8475a5618106d6bd5ba40` (Windows Gate 44490632349); exact immutable release not yet verified. Bootstrap `fengie/heaven-toolbox@main` before other work.

## Integrated implementation

- **v8.8.118 candidate:** #895 protects entire updater cleanup against malformed pending state, preserving recovery transaction and staging; regression plus all four exact-head Windows/source/security checks PASSED. Merged to main `c3d32904`, with exact v8.8.117 hosted receipt #896 at main `109ee761`. Publication and real v8.8.118 hosted update/rollback still pending.

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

Current hosted-Windows closure: v8.8.118 source `f1dd0dd4500ad8383ed8475a5618106d6bd5ba40` passed run `44490632349` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.118-heaven-windows-closure.log`.

Public release status: **NOT VERIFIED** for source `f1dd0dd4500ad8383ed8475a5618106d6bd5ba40`. Successful build or another source's same-version release is not proof of publication.

The tested source remains `f1dd0dd4500ad8383ed8475a5618106d6bd5ba40` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): hosted real updater 60→586 and rollback 44477647687 PASSED; independent personal Heaven2 installed SHA/health and bounded endurance remain unverified.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) merged the deterministic source-bound release and current-next-step sync on `main@687607cf` after exact-head native Windows, ownership, product security, and AutoUpdate checks. Historical candidate prose is reconciled in this evidence-only handoff; root README/CHANGELOG wording remains a later serialized release-metadata task.

Next: complete serialized v8.8.118 release candidate exact-head checks after preceding main publisher, protected expected-head merge, exact immutable source/tag/artifact and real hosted packaged update/rollback; independently verify Heaven2, #791 mixed-game archive acceptance, #735 installed p95, #669 full storage lifecycle, provider and independent trust. Preserve recursive Core/Learned Rules.
