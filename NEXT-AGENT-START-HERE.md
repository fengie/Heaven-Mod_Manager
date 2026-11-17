# v8.8.109 bulk archive and drag-drop import — serialized release candidate

Canonical source: `fengie/Heaven-Mod_Manager@main`, latest implementation `9e3fc0be049cc43cbcaa506ff854502abc499773` via [#829](https://github.com/fengie/Heaven-Mod_Manager/pull/829). Global engineering authority remains `fengie/heaven-toolbox@main`. Refresh actual owner/leases, main and candidate refs, checks, project plan and latest releases before integration.

**Mandatory recursive continuity:** Every successor must read the entire `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`, inherit and preserve both, and record justified lessons. Every successor must recursively propagate these Core Rules and active Learned Rules to the next agent, who must repeat that obligation for their successor. Do not break the chain. No prior conversation is needed or authoritative.

## Implemented source and exact checks

- Bulk ZIP/RAR/7z multiselect, Explorer file/folder drag-drop, explicit folder-picker batch, per-item progress and cancellation, shared registered-game/dedup receipt intake, and non-destructive per-game Inbox routing; ambiguous layouts stay review-only. Legacy single-source cumulative copy limits are retained, while explicit archive-only batches remain per-archive bounded.
- [PR #829 Windows Feature 43950868996](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43950868996) passed 26/26, 352 core, 112 automation and 555 integration tests, zero tracing gaps. [AutoUpdate 43950868605](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43950868605) passed on exact candidate `b1fb06ed803a355bf0b05e5bfd2afa4026fb76bd`; main readback confirmed `9e3fc0be049cc43cbcaa506ff854502abc499773`.
- Previous immutable [v8.8.108 updater-main-524](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-524) was built from older source `93eb26a7b48c0fc21ece2d2c8a87b7fb04981831`. It does not contain bulk import or drag-drop. This v8.8.109 metadata is a **candidate**, not published or installed proof.

## Verification boundary

Current hosted-Windows closure: v8.8.109 source `8ed0a73ca4a320c49c579b8620a3b1869595d2b3` passed run `43983812212` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.109-heaven-windows-closure.log`.

The tested source remains `8ed0a73ca4a320c49c579b8620a3b1869595d2b3` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and release acceptance

- **Unresolved risks:** Physical installed-client update, Explorer drag/drop, real large RAR/7z corpus throughput, and isolated rollback remain unverified under [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) and [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793). [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) tracks release-status synchronization.
- New immutable v8.8.109 ZIP/manifest publication, exact SHA-256 and public feed parity require independent post-merge evidence; neither green PR CI nor source integration proves installed-client acceptance.

Preserve security, privacy, rollback, independent publisher ownership, accurate source/CI/release/runtime identity, and recursively propagate all continuity obligations to the next agent again.
