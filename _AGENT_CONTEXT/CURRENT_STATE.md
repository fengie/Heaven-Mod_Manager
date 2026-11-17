# v8.8.112 — Inbox progress and measurable verification, serialized release candidate

Canonical state: `fengie/Heaven-Mod_Manager@main` source `e9d8c09bfeefe452eecad989aca7c612a965b57d`, including #844/#846. Prior immutable [v8.8.111 updater-main-537](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-537) is source `1e24b3519d56471bcb3995ab6b70ae9f6c09d00a`, before both changes. Bootstrap `fengie/heaven-toolbox@main` before task-specific work.

## Integrated implementation

- #844 detailed package/file/byte Inbox progress, WPF overlay responsiveness and phase timing; [exact Windows Feature 44028036684](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44028036684) and [main Gate 44028954968](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44028954968) PASS; user-corpus WPF p95 not measured.
- #846 code coverage instrumentation/workflow, 19-feature mapped test inventory, functional ZIP route→Inbox→index/dedup chain and fresh-state macrobenchmark definitions; [Feature 44029013896](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/44029013896) PASS. Coverage percentage and benchmark times not yet measured; no invented score.



- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

- #839 safely rejects family pairs that cannot satisfy the existing strong-evidence predicate before root intersection/formatting, with negative identical-path and uniform-stem regressions. Synthetic same-runner 500 shared-root case: 281.974 → 223.461 ms and 507.76 → 420.2 MB allocated in three benchmark iterations; real WPF p95 remains unknown.

## Verification boundary

- Preceding immutable [v8.8.111 updater-main-537](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-537), source `1e24b3519d56471bcb3995ab6b70ae9f6c09d00a`, ZIP SHA-256 `abb63eb14c5ead6b062be75b8c01d82d8b370b0ab7cb7685c0140a2a724f35d0`, manifest SHA-256 `3e899c13f170ba92d5ab53f7bc45f7964e3c8295fdd297a37588b6be020a4361` independently published and verified.
- Exact merged v8.8.112 implementation baseline `e9d8c09bfeefe452eecad989aca7c612a965b57d` contains #844/#846 after exact-head native CI. v8.8.112 metadata is not yet tested or published. Require its fresh PR gates, expected-head protected merge, 26/26 new main gate and independent distinct immutable ZIP/manifest/source-tag/feed verification. Never overwrite the previous immutable release.
- This is a source verification boundary, not proof of actual installed Heaven2 version/health, user RAR/7z corpus throughput or WPF p95. #793 has two positive isolated build-537 E2E runs but one intermittent timeout; do not claim zero-flake behavior.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): packaged updater [43993780193](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43993780193) failed safely before update on occupied shared root (2 safety pass / 1 positive fail). Dedicated least-privileged Windows test profile required; do not delete shared user state or claim installed acceptance.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): release-status/verification-closure synchronization remains independently tracked.

Next: verify and integrate the v8.8.111 metadata on current main, publish a distinct immutable ZIP/manifest with source SHA and public-feed parity, and obtain separate installed/user-corpus evidence. Do not modify updater-main-535. Every successor must inherit, obey and recursively propagate the permanent continuity constitution and active Learned Rules to the next agent.
