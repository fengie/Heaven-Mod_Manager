# v8.9.0 game-scoped library and startup safety — canonical state

v8.9.0 is the next release identity for the verified startup and per-game mod storage change set. The previous v8.8.99 hermetic SDK and v8.8.98 catalog continuation behavior remain in effect. This text is a source-state record, **not** an installed-release receipt.

## Behavior

- Empty-library startup does not scan/hash live game assets or build an empty conflict graph; manual adoption stays an explicit separate action.
- Canonical game ID determines a separate `Mods by Game/<id>/{Installed,Downloads,Inbox,Archive}` library. Legacy MHW `Mods/` and per-game `Games/<id>/Mods/` remain readable without relocation or deletion.
- Catalog and manually imported archives are copied into game-owned Downloads only after import and full SHA-256 verification; existing user downloads are not auto-reaped.
- Immutable release/updater manifests reject persistent game-library paths; installed-client E2E checks both legacy and new user-owned download sentinels across update and rollback.
- Catalog fixtures no longer expire on real-provider review dates; real external-provider policy still fails closed. Windows test processes acquire a shared exclusive OS file lease around updater global state.
- Provider pagination/continuation, dependency/conflict/override semantics and Toolbox inheritance remain unchanged. #558 and #735 retain independent acceptance obligations.

## Verification boundary

**No current v8.9.0 Windows closure or installed-release proof has been recorded.** Last historical v8.8.99 Windows verification source `c08b31c5327522c5896704d7a24b5ddc6f70b9c4`, run `38006379575`, evidence `_AGENT_CONTEXT/EVIDENCE/v8.8.99-heaven-windows-closure.log`. Do not promote it to v8.9.0 or infer a deployed client from source merge.

## Next required action

Reconcile exact merged #784/#785 source against current main and native CI, then run canonical `windows-release-gate.yml` with new v8.9.0 metadata. Require complete exact-source verification, package/provenance/publisher readback and installed-client updater E2E. The standard synchronizer owns source/run evidence persistence. Physical NieR startup and desktop installed source identity remain unverified without a real authenticated host receipt.

## Unresolved risk and independent work

- #558 remains open for provider breadth and deterministic catalog scale/performance; #735 for installed Auto Populate timing.
- No code or CI-only result proves Heaven2's installed version, NieR load time, or live auto-updater completion.
- External signing/ruleset and authorized machine operation remain governed by the live product plan. Preserve user-owned mod downloads and all release/trust boundaries.

Every successor must read the permanent continuity constitution and active Learned Rules, document exact verified state, and explicitly require the next agent to preserve and recursively propagate the same Core Rules to its successor.
