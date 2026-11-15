# v8.8.108 structural mod discovery — source integrated, publication pending

Canonical state: MHW Mod Manager product source: `fengie/Heaven-Mod_Manager@main`, latest integrated source `5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7` via [PR #823](https://github.com/fengie/Heaven-Mod_Manager/pull/823). The previous v8.8.107 updater release, build 522, is immutable and contains earlier source `a1e9078b9b9a3a52106a76341c7ea79f7b1c4630`, **not** #823.

## What the new source actually does

Dashboard and Mods now expose a separate **Inspect Existing Mod Layouts** read-only command for all tracked package sources. It inventories bounded files, reports nested companion groups using contextual Sifu PAK/SIG and Stellar Blade PAK/UTOC/UCAS patterns, and exposes unknown, incomplete, FOMOD, manifest and shared-loader groups with exact relative file members and reasons. The existing **Separate Existing Mod Collections** path and its import preconditions are untouched; the new structural preview does not stage, auto-enable, copy, delete or deploy mod files.

## Verification boundary

- [PR #823 Windows Feature Gate 43707926891](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43707926891) and [AutoUpdate 43707926895](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43707926895) completed success on head `20c7a71b9906c0d1c5cc0350fab124e32945fc63`. Protected main readback confirmed the new source at `5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7`.
- [Main Windows Release Gate 43708380211](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43708380211) **completed success, 26/26, zero failures and zero trace gaps** for `5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7`. It correctly skipped v8.8.107 publication because [updater-main-522](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-522) is immutable. No new version was published by that run.
- v8.8.108 release metadata is a separate serialized candidate; no v8.8.108 immutable package, manifest, feed parity, or installed machine health is claimed yet.

## Open acceptance, risks and ownership

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) still requires real hundreds-mod Sifu/Stellar Blade corpus, verified independent installable grouping, user-confirmed transactional intake, and installed WPF acceptance. Filename companion evidence is not permission to split dependent mods.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) positive old→new packaged apply/rollback remains blocked on independently isolated Windows test identity. Do not touch occupied user updater state.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) tracks systemic release status synchronization.

Next: preserve verified main Windows gate closure → candidate exact-head native checks → protected serialized merge → fresh main release validation → verify immutable ZIP/manifest/feed, then separately installed runtime/user-corpus evidence. Preserve and recursively pass the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active Learned Rules to every successor.
