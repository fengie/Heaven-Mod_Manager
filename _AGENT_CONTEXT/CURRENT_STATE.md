# v8.8.112 - source verified, publication unverified

Canonical state: `fengie/Heaven-Mod_Manager@main` verified at `98af6dc8982102567430d0484390639e488593d0` (Windows Gate 44047541323); exact immutable release not yet verified. Bootstrap `fengie/heaven-toolbox@main` before other work.

## Integrated implementation

- #844 phase-aware Inbox progress and performance reports; #846 opt-in code coverage, matrix and synthetic benchmarks; #847 hosted-Windows UIA import/preview/apply/undo/switch; #848 automatic approved active-game manual bulk-import completion and Mods refresh. All integrated at `main@62f3dee97e1e6ed56bcc098b50db515e4b08b624`, with #848 exact-head Feature 44037120853 and hosted WPF UIA 44037121012 (4/4). No assertion of v8.8.112 publication or installed-device proof.

- #829 ZIP/RAR/7z bulk-import and Explorer drag/drop already stages confidently classified candidates into registered-game Inboxes while preserving originals and no direct live-game writes; uncertain candidates remain for review.
- #832 reduces dense shared-path planner allocations through canonical pair IDs and per-plan cached overlay names/tokens. Existing inference and explicit-rule precedence remain; synthetic measurements are not installed-client timing proof.
- #835 upgrades Microsoft.Data.Sqlite to 11.0.0 and SharpCompress to 0.50.9, keeps System.IO.Hashing 11.0.0, synchronizes third-party notices and ensures dependency-file changes enter native Windows source CI.

- #836 adds 10 synthetic ZIP/invalid-archive/cancellation/dedup fault integration tests, 3 family topology unit tests and opt-in replay/family benchmark suites; exact-head native Windows Feature 43982862135 and AutoUpdate 43982863098 passed. This does not establish real RAR/7z or installed UI behavior.

- #838 protects cross-process mod-intake receipt durability by serializing independent writer commits and re-reading/merging receipt hashes under the writer lease. Exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847, integrated on main 285ba723; physical installed concurrency proof remains unverified.

- #839 safely rejects family pairs that cannot satisfy the existing strong-evidence predicate before root intersection/formatting, with negative identical-path and uniform-stem regressions. Synthetic same-runner 500 shared-root case: 281.974 → 223.461 ms and 507.76 → 420.2 MB allocated in three benchmark iterations; real WPF p95 remains unknown.

## Verification boundary

Current hosted-Windows closure: v8.8.112 source `98af6dc8982102567430d0484390639e488593d0` passed run `44047541323` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.112-heaven-windows-closure.log`.

Public release status: **NOT VERIFIED** for source `98af6dc8982102567430d0484390639e488593d0`. Successful build or another source's same-version release is not proof of publication.

The tested source remains `98af6dc8982102567430d0484390639e488593d0` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining acceptance and risks

- [#836](https://github.com/fengie/Heaven-Mod_Manager/pull/836) is integrated at main 8ed0a73 and passed exact native checks. Its synthetic tests run in CI; replay/topology benchmark wall times are unmeasured unless separately dispatched, and physical mod corpus remains untested.
- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): actual large RAR/7z and nested Sifu/Stellar Blade mod collections, WPF drag/drop/progress/cancel, unknown layouts and physically installed acceptance remain unverified.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): packaged updater [43993780193](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43993780193) failed safely before update on occupied shared root (2 safety pass / 1 positive fail). Dedicated least-privileged Windows test profile required; do not delete shared user state or claim installed acceptance.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817): release-status/verification-closure synchronization remains independently tracked.

Next: verify and integrate the v8.8.111 metadata on current main, publish a distinct immutable ZIP/manifest with source SHA and public-feed parity, and obtain separate installed/user-corpus evidence. Do not modify updater-main-535. Every successor must inherit, obey and recursively propagate the permanent continuity constitution and active Learned Rules to the next agent.
