# v8.8.110 planner/dependency reconciliation — source integrated, release pending

Canonical state: `fengie/Heaven-Mod_Manager@main` includes #832 and #835 source and #836 at `8ed0a73ca4a320c49c579b8620a3b1869595d2b3`. Previous immutable [v8.8.109 updater-main-530](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-530) was published from earlier source `a29103111dd9b0e56574c50bd3fbb1baaf25d33a` and does not include these newer changes.

## Integrated implementation

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

## Verification boundary

- #832 head `5cf175d871fe51ac1895378c919c8d2d1d14784c` passed exact Windows Feature [43978338548](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43978338548) and AutoUpdate 43978338567. #835 head `2974738085def5dc4183d7c7ad055b6545922c14` passed exact Feature [43979469737](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43979469737), Product Security 43979469676, Updater Publication 43979469883 and AutoUpdate 43979469626.
- Prior main Windows Release Gate 43981825454 passed for #835. New [main Windows Release Gate 43983812212](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43983812212) for #836 must terminate before protected version integration; never overwrite immutable updater-main-530.
- New v8.8.110 publication is **pending** candidate exact native gates, serialized protected merge, exact-main Windows release verification and independent ZIP/manifest/sha/public-feed readback. Source CI and publication are not installed-runtime proof.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive packaged update-and-rollback requires isolated Windows user/profile; do not clear occupied shared updater state or treat E2E classification success as an actual update.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): release-status/verification-closure synchronization remains independently tracked.

Next: finish newer main gate 43983812212, reconcile v8.8.110 metadata to 8ed0a73 and rerun candidate native checks, verify/publish a distinct immutable version, and gather separate installed/user-corpus evidence. Every successor must inherit, obey and recursively propagate the permanent continuity constitution and active Learned Rules to the next agent.
