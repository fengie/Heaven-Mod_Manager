# v8.8.109 bulk archive and drag-drop import — serialized release candidate

Canonical source: `fengie/Heaven-Mod_Manager@main`, latest implementation `9e3fc0be049cc43cbcaa506ff854502abc499773` via [#829](https://github.com/fengie/Heaven-Mod_Manager/pull/829). Global engineering authority remains `fengie/heaven-toolbox@main`. Refresh actual owner/leases, main and candidate refs, checks, project plan and latest releases before integration.

**Mandatory recursive continuity:** Every successor must read the entire `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve those rules, record justified lessons and explicitly require their successor to inherit, preserve and recursively propagate both again. No prior conversation is needed or authoritative.

## Implemented source and exact checks

- Bulk ZIP/RAR/7z multiselect, Explorer file/folder drag-drop, explicit folder-picker batch, per-item progress and cancellation, shared registered-game/dedup receipt intake, and non-destructive per-game Inbox routing; ambiguous layouts stay review-only. Legacy single-source cumulative copy limits are retained, while explicit archive-only batches remain per-archive bounded.
- [PR #829 Windows Feature 43950868996](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43950868996) passed 26/26, 352 core, 112 automation and 555 integration tests, zero tracing gaps. [AutoUpdate 43950868605](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43950868605) passed on exact candidate `b1fb06ed803a355bf0b05e5bfd2afa4026fb76bd`; main readback confirmed `9e3fc0be049cc43cbcaa506ff854502abc499773`.
- Previous immutable [v8.8.108 updater-main-524](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-524) was built from older source `93eb26a7b48c0fc21ece2d2c8a87b7fb04981831`. It does not contain bulk import or drag-drop. This v8.8.109 metadata is a **candidate**, not published or installed proof.

## Serial release owner — next verification action

1. Confirm preceding exact-main source-only [Windows Release Gate 43952164444](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43952164444) has completed; it must not replace the immutable v8.8.108 artifact.
2. Verify this metadata-only candidate on its exact head: Windows Feature, README timestamp, Product Security, ownership and AutoUpdate. Refresh main/base/owner and merge with expected-head protection after all required checks succeed.
3. Run exact new main Windows Release Gate (26/26) and verify a **distinct** immutable v8.8.109 Windows ZIP and updater manifest, both SHA-256 digests and public feed parity. Preserve build 524 and all earlier releases. Do not force republish an old semantic version.
4. Separate release proof from physical installed-client update, drag-drop interactions, large real RAR/7z throughput, and isolated update/rollback. Those remain unverified under [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) and [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793). [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) still tracks release-status synchronization.

Preserve security, privacy, rollback, independent publisher ownership, accurate source/CI/release/runtime identity, and recursively propagate all continuity obligations to the next agent again.
