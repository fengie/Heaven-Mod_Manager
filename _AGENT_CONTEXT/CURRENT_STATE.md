# v8.8.108 structural mod discovery — source integrated, publication pending

Canonical state: MHW Mod Manager product source: `fengie/Heaven-Mod_Manager@main`, latest integrated source `5c9b6f0a1b33e0520cb8d7f07db8a1b0684d07a7` via [PR #823](https://github.com/fengie/Heaven-Mod_Manager/pull/823). The previous v8.8.107 updater release, build 522, is immutable and contains earlier source `a1e9078b9b9a3a52106a76341c7ea79f7b1c4630`, **not** #823.

## What the new source actually does

Dashboard and Mods now expose a separate **Inspect Existing Mod Layouts** read-only command for all tracked package sources. It inventories bounded files, reports nested companion groups using contextual Sifu PAK/SIG and Stellar Blade PAK/UTOC/UCAS patterns, and exposes unknown, incomplete, FOMOD, manifest and shared-loader groups with exact relative file members and reasons. The existing **Separate Existing Mod Collections** path and its import preconditions are untouched; the new structural preview does not stage, auto-enable, copy, delete or deploy mod files.

## Verification boundary

Current hosted-Windows closure: v8.8.108 source `93eb26a7b48c0fc21ece2d2c8a87b7fb04981831` passed run `43710641181` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.108-heaven-windows-closure.log`.

The tested source remains `93eb26a7b48c0fc21ece2d2c8a87b7fb04981831` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Open acceptance, risks and ownership

- [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) still requires real hundreds-mod Sifu/Stellar Blade corpus, verified independent installable grouping, user-confirmed transactional intake, and installed WPF acceptance. Filename companion evidence is not permission to split dependent mods.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) positive old→new packaged apply/rollback remains blocked on independently isolated Windows test identity. Do not touch occupied user updater state.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) tracks systemic release status synchronization.

Next: preserve verified main Windows gate closure → candidate exact-head native checks → protected serialized merge → fresh main release validation → verify immutable ZIP/manifest/feed, then separately installed runtime/user-corpus evidence. Preserve and recursively pass the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active Learned Rules to every successor.
