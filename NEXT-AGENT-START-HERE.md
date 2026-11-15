# v8.8.107 existing-library mod reanalysis — serialized release candidate

Canonical repository: `fengie/Heaven-Mod_Manager@main`. Mandatory global Toolbox-first bootstrap: `fengie/heaven-toolbox@main`. Source cutoff: `5b50e6581b4b1bef56ad09fe422621388f494c85`; previous verified immutable v8.8.106 [build 517](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-517) from `2a317cf5cd79e39ac9cc9b1e9a7d9bfa5f084d3d`. This 8.8.107 metadata is **not** evidence of new immutable publication or physical installation.

Every successor must read, preserve, obey and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md` to the next successor. That successor must pass these Core Rules onward again; continue without private chat history and refresh current Toolbox/MHW source, owners and `_AGENT_CONTEXT/PROJECT_PLAN.md`.

## Implemented source and cutoff

- [#815](https://github.com/fengie/Heaven-Mod_Manager/pull/815) repaired cumulative visited-directory/file budget accounting after build 517.
- [#818](https://github.com/fengie/Heaven-Mod_Manager/pull/818) added explicit registered-game assignment only for one review-only standalone mod; no overrides or live-game writes.
- [#819](https://github.com/fengie/Heaven-Mod_Manager/pull/819) was integrated as `main@5b50e6581b4b1bef56ad09fe422621388f494c85`. Dashboard → Separate Existing Mod Collections previews deeply nested independent mods inside prior tracked one-package folders. It only processes disabled, unstaged packages, requires at least two confident children, copies through existing per-game Inbox, preserves original roots/IDs/live game files, and relies on receipts for idempotence.
- Real-archive-format [#820](https://github.com/fengie/Heaven-Mod_Manager/pull/820) merged at `main@9aa21347654b257a4824b20bb50f8fa71efe646a`; this release branch incorporated its two pinned real-format acceptance files by a non-force merge while preserving product source and metadata.

## Verification boundary

Current hosted-Windows closure: v8.8.107 source `a1e9078b9b9a3a52106a76341c7ea79f7b1c4630` passed run `43702127240` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.107-heaven-windows-closure.log`.

The tested source remains `a1e9078b9b9a3a52106a76341c7ea79f7b1c4630` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Serialized release / open risk / next actions

1. Confirm preceding main Windows Release Gate and current peer owners are terminal/stable. Verify exact v8.8.107 metadata candidate with native Windows Feature/README timestamp/Product Security/Ownership/AutoUpdate checks; keep scope to nine release/handoff metadata files and reconcile current `main` without force.
2. Under the protected, serialized release slot, merge metadata at exact expected head/base. Run **fresh exact-main Windows Release Gate** 26/26. Confirm distinct new immutable updater tag with v8.8.107 Windows ZIP, update-manifest.json, artifact/manifest SHA-256 and matching public updater feed. Do not reuse/overwrite prior release build 517.
3. Persist source-bound verification evidence via the protected evidence PR and reconcile stale continuity if needed. A source CI pass, released ZIP, skipped E2E job, or updater workflow success alone cannot establish physical Heaven2 install/rollback.
4. Leave [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) open until the real hundreds-mod corpus, unresolved layouts and packaged UI are tested, and [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) open until a separate least-privileged Windows test profile achieves positive packaged old→new updater/rollback. Never clear the occupied shared updater root.

Every successor must preserve and recursively pass on these same Core Rules to their successor; do not break the chain.
