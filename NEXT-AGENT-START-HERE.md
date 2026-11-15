# v8.8.105 candidate-level mod import review — canonical source/release handoff

Canonical source: `fengie/Heaven-Mod_Manager@main`. Global trainer: `fengie/heaven-toolbox@main`; every successor must read, preserve, obey and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and applicable Learned Rules to every successor. No private memory is required.

**Do not break the chain.**

## Implemented product source

[PR #802](https://github.com/fengie/Heaven-Mod_Manager/pull/802) merged on protected main `20b8fbb67f3ffe803026590cc77e5086d97b2db9`. Explicit manual mixed-folder and archive imports now show a read-only per-child WPF review with source path, registered destination, routing/review status and reason. Background folder scanning does not open dialogs. The existing classifier, original package, game data, updater and live deployment are unchanged.

Exact PR candidate head `a401f52c396faa9f952707d7a0e56a3848daa085` passed [26/26 native Windows Feature Gate #43675134924](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43675134924): 454 integration regressions, 0 functional trace gaps, 0 failures. [AutoUpdate Contract #43675134917](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43675134917) passed. Initial 25/26 gate `43674475607` was repaired by tracing the new constructor without lowering any verifier.

**Unresolved risk and context:** Real RAR/7z/solid archive acceptance, ambiguous-candidate assignment and positive Windows installed updater E2E remain unverified; the isolated Windows profile is a release-independent blocker. Each successor must read `_AGENT_CONTEXT/LEARNED_RULES.md` alongside `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and preserve both sets of obligations.

**Verification state:** The earlier source PR #802 passed native Windows feature checks on its exact head, but this serialized v8.8.105 release candidate still requires its own passing verification and independent immutable publication. No installed-client E2E success is claimed.

## Immutable release boundary

Previously published [v8.8.104 / updater-main-504](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-504) belongs exactly to source `a9d6e9355fbbc7e05ae887d8a535fb4a9a890e32`; [release gate #43669418277](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43669418277) passed. Never overwrite it or earlier build 501.

This serialized v8.8.105 metadata change is **not yet verified or published**. Next: pass native exact-head gates on this release PR; protected serial expected-base merge; exact-main Windows Release Gate 26/26; inspect newly published immutable updater build, manifest/ZIP SHA-256 and evidence PR. Until those succeed, `v8.8.105` is source/release candidate only.

## Acceptance still open

- **[#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791)**: representative real RAR4/RAR5 solid/non-solid and 7z plus corruption/encryption/bomb fixtures, manual ambiguous-child game assignment/confirmation and installed interactive WPF keyboard/size/cancel smoke. Sibling draft PR #803 owns independent deeper Stellar Blade grouping; preserve its head/history and reconcile against current main rather than overwriting it.
- **[#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793)**: self-hosted Windows updater test shares a populated real LocalApplicationData root, so actual positive update/rollback correctly failed closed on [build 504 E2E #43670150505](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43670150505). Requires a dedicated least-privileged Windows test account/profile, checked empty independently owned updater root, and positive old→new updater, health, sentinel and rollback proof. Do **not** clear user data, weaken fail-closed checks, or call a classification-only job installed proof. Heaven2 installed source/health remains separately unverified.

Every subsequent agent must inherit and propagate the same continuity obligations and preserve these exact source/release/runtime distinctions.
