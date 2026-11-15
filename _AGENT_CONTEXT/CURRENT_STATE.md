# v8.8.105 candidate-review importer — canonical state

[PR #802](https://github.com/fengie/Heaven-Mod_Manager/pull/802) integrated in protected main `20b8fbb67f3ffe803026590cc77e5086d97b2db9` and provides an explicitly opened, read-only WPF per-candidate report for one-shot mixed-folder/archive imports (source, detected registered game, routing status, evidence). It does not auto-assign unknown mods, install into a live game or alter updater state. Background folder monitoring has no review dialog.

Exact source PR head `a401f52c396faa9f952707d7a0e56a3848daa085` passed [Windows Feature Gate #43675134924](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43675134924) **26/26** and [AutoUpdate #43675134917](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43675134917). The prior 25/26 failure was missing constructor tracing and was repaired without changing the verifier. Core, Automation and 454 Integration regressions passed. Windows relaxed solution compiled with 0 errors.

## Verification boundary

Current hosted-Windows closure: v8.8.105 source `bdbb38f2a51cdd5299e68c63a24de272d540486e` passed run `43686751623` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.105-heaven-windows-closure.log`.

The tested source remains `bdbb38f2a51cdd5299e68c63a24de272d540486e` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Source versus release

Immutable prior [v8.8.104 build 504](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-504) was published from exact source `a9d6e9355fbbc7e05ae887d8a535fb4a9a890e32`; main Windows Release Gate #43669418277 succeeded. This serialized v8.8.105 release metadata must still pass its own exact-head native PR, protected merge, main Windows Release Gate, immutable new tag/ZIP/manifest verification. Code merge is not 8.8.105 publication or machine installation.

## Independent open acceptance

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791): real RAR/7z including solid, encrypted/corrupt/cumulative limits, explicit ambiguous-candidate registered-game assignment and packaged WPF keyboard/size/cancel smoke remain. Peer draft #803 targets deeper Stellar Blade inventory grouping; preserve independent ownership and exact base.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793): latest build-504 real installed-client updater E2E #43670150505 correctly refused to delete occupied shared Windows updater root, **before** positive update/rollback. Dedicate a truly separate Windows user/profile and independently prove old→new update, injected rollback, preservation of sentinel/user files, and installed Heaven2 source/health. Do not disable this guard or delete real state.

All successors must read, preserve, obey and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` to the agent after them.
