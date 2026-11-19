# v8.8.115 - immutable release verified

Canonical state: `fengie/Heaven-Mod_Manager@main` at `caa8fd1f5801a0afb3f14bb5ea205faf119497d5`. Public immutable [updater-main-579](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-579) contains this exact source. Bootstrap `fengie/heaven-toolbox@main` before other work.

Canonical state source baseline: `fengie/Heaven-Mod_Manager@main` at `50f45ed90b973e9a9c91cb8f3e3d8927d3ed18bb` has #881/#882/#884 merged, not yet packaged into v8.8.115. v8.8.114 is immutable [v8.8.114 updater-main-575](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-575) at source `6ac4e474c8769e04d456433dab43fe97b2ae5ace`; Windows Release Gate [44440884960](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44440884960) and real hosted packaged update/rollback [44442306486](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44442306486) passed. Personal Heaven2 remains unverified. Personal Heaven2 installation still needs independent proof. Bootstrap `fengie/heaven-toolbox@main`.

## Integrated implementation

- **v8.8.115 candidate:** #881 rejects Alt/Choose cross-source suggestion mistakes; #882 prunes redundant shared-root candidate pair generation; #884 shares one hyphen-safe choice vocabulary across inference routes while retaining original-name evidence. Exact-head native PR gates passed, main source `50f45ed90b973e9a9c91cb8f3e3d8927d3ed18bb`. Await new immutable version publication.

- **Historical v8.8.113 source:** #850–#874 brought real small-format RAR/7z fixtures, durable import recovery, conflict and game-routing safeguards, startup measures and catalog visibility. Real large mixed-game collections and installed p95 remain unverified.

- #844 phase-aware Inbox progress and performance reports; #846 opt-in code coverage, matrix and synthetic benchmarks; #847 hosted-Windows UIA import/preview/apply/undo/switch; #848 automatic approved active-game manual bulk-import completion and Mods refresh. All integrated at `main@62f3dee97e1e6ed56bcc098b50db515e4b08b624`, with #848 exact-head Feature 44037120853 and hosted WPF UIA 44037121012 (4/4). These source changes are included in immutable [v8.8.112 updater-main-548](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-548); installation on Heaven2 is not yet verified.

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

- #839 safely rejects family pairs that cannot satisfy the existing strong-evidence predicate before root intersection/formatting, with negative identical-path and uniform-stem regressions. Synthetic same-runner 500 shared-root case: 281.974 → 223.461 ms and 507.76 → 420.2 MB allocated in three benchmark iterations; real WPF p95 remains unknown.

## Verification boundary

Current hosted-Windows closure: v8.8.115 source `caa8fd1f5801a0afb3f14bb5ea205faf119497d5` passed run `44455718899` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.115-heaven-windows-closure.log`.

Verified immutable GitHub release: [updater-main-579](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-579), exact source `caa8fd1f5801a0afb3f14bb5ea205faf119497d5`, ZIP SHA-256 `df725070c0b77711d6aebcd3455daa97e7e15e591c1bba316881c8495a24fe34` and manifest SHA-256 `697a42528f452f9f20891a95a62202fe150df2110b80a38def8085f20b3b205b`. Public release and tag ref independently match the built artifact.

The tested source remains `caa8fd1f5801a0afb3f14bb5ea205faf119497d5` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): hosted real update/rollback run 44442306486 passed build 60→575 and WPF/sentinel checks on disposable Windows. Personal Heaven2 installed SHA/health and longer-term reliability remain unverified.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): [PR #852](https://github.com/fengie/Heaven-Mod_Manager/pull/852) merged the deterministic source-bound release and current-next-step sync on `main@687607cf` after exact-head native Windows, ownership, product security, and AutoUpdate checks. Historical candidate prose is reconciled in this evidence-only handoff; root README/CHANGELOG wording remains a later serialized release-metadata task.

Next: publish v8.8.115 from verified exact main after serial publisher, verify its tag/ZIP/manifest and hosted real update/rollback, then independently verify Heaven2. Retain #791 real corpora, #735 p95, #669 storage, #793 personal deployment, #786 provider terms and #350 trust/signing as OPEN. Every successor must inherit, obey and recursively propagate the permanent continuity constitution and active Learned Rules to the next agent.
