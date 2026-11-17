# v8.8.111 family performance — implementation merged, serialized release candidate

Canonical state: `main@fefc78110f69e5d3532d6fd424d0e11253adf09c` includes #839 early strong-evidence family pruning plus previously published #829/#832/#835/#836/#838. Previous immutable [v8.8.110 updater-main-535](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-535) has source `968d9e7d77a7d5872fb94a63eea0d9819586a0b8`, so the public ZIP does **not** contain #839.

## Integrated implementation

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

- #839 safely rejects family pairs that cannot satisfy the existing strong-evidence predicate before root intersection/formatting, with negative identical-path and uniform-stem regressions. Synthetic same-runner 500 shared-root case: 281.974 → 223.461 ms and 507.76 → 420.2 MB allocated in three benchmark iterations; real WPF p95 remains unknown.

## Verification boundary

- #839 exact head `176116f8` passed native Windows Feature [43989850199](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43989850199), AutoUpdate 43989850215 and required `enforce`; merged `main@fefc7811` passed 26/26 [Windows Release Gate 43993837451](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43993837451) and AutoUpdate 43993837449. Unchanged 8.8.110 version intentionally skipped duplicate publication.
- Immutable v8.8.110 updater-main-535: published earlier source `968d9e7d`, independent ZIP SHA-256 `68c49dbc534b1125ca6cec7c8048ebeded380bc4c2ae003b11cd2b59f4860ffd`, manifest SHA-256 `d9f97ea3d1da85d710d3e207f3fb8ced592eaeb7356c389121c260d0a5f3ae71`.
- v8.8.111 metadata candidate requires exact-head Feature/README/Security/Ownership/AutoUpdate, protected expected-head merge and fresh 26/26 Windows Release Gate. Distinct v8.8.111 immutable artifact/manifest/source/feed SHA readback is mandatory; none has been claimed yet.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): packaged updater [43993780193](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43993780193) failed safely before update on occupied shared root (2 safety pass / 1 positive fail). Dedicated least-privileged Windows test profile required; do not delete shared user state or claim installed acceptance.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): release-status/verification-closure synchronization remains independently tracked.

Next: verify and integrate the v8.8.111 metadata on current main, publish a distinct immutable ZIP/manifest with source SHA and public-feed parity, and obtain separate installed/user-corpus evidence. Do not modify updater-main-535. Every successor must inherit, obey and recursively propagate the permanent continuity constitution and active Learned Rules to the next agent.
