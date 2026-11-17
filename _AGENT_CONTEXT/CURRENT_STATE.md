# v8.8.110 planner/dependency reconciliation — source integrated, release pending

Canonical state: `fengie/Heaven-Mod_Manager@main` includes #832 and #835 source and #836/#838 at `285ba72396ce942862842c89818cfce90ff64c56`. Previous immutable [v8.8.109 updater-main-530](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-530) was published from earlier source `a29103111dd9b0e56574c50bd3fbb1baaf25d33a` and does not include these newer changes.

## Integrated implementation

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

## Verification boundary

Current hosted-Windows closure: v8.8.110 source `fefc78110f69e5d3532d6fd424d0e11253adf09c` passed run `43993837451` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.110-heaven-windows-closure.log`.

The tested source remains `fefc78110f69e5d3532d6fd424d0e11253adf09c` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): positive packaged update-and-rollback requires isolated Windows user/profile; do not clear occupied shared updater state or treat E2E classification success as an actual update.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): release-status/verification-closure synchronization remains independently tracked.

Next: finish #838 main gate 43987535401, reconcile v8.8.110 metadata to 285ba723 and rerun candidate native checks, verify/publish a distinct immutable version, and gather separate installed/user-corpus evidence. Every successor must inherit, obey and recursively propagate the permanent continuity constitution and active Learned Rules to the next agent.
