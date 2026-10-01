# v8.8.64 rich Browse Mods — candidate MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

PR #561 is the active v8.8.64 candidate. Candidate production/test source is `2b49f4f719dfcddfccd3a215a88595df5c779b5e`; later branch commits synchronize version and continuity metadata.

## Current MHW product work

- **BROWSE-556 / P1:** ACTIVE candidate. Header game selector uses a DisplayName item template so raw `GameProfile` text cannot appear in the selected surface.
- **BROWSE-557 / P1:** ACTIVE candidate. Browse Mods has artwork-backed rich rows, metadata, selected-mod artwork, and focused regressions.
- **CATALOG-SCALE-558 / P1:** ACTIVE. First capacity tranche is in v8.8.64; deeper provider-aware pagination/search remains open.
- **BROWSE-UX-559 / P1:** READY. Filters, sorting, provider-health and richer discovery states remain follow-up work.
- **SECURITY-554 / P0:** DONE in v8.8.63 via PR #555 after all required exact-head gates passed.
- **RECOVERY-002 / P0:** DONE for v8.8.62 catalog browsing/acquisition + CurseForge recovery.
- **Issue #281:** remains open only for real provider contracts such as conditional Steam Workshop support, optional Vortex interoperability, and future provider-specific adapters with compliance evidence.
- **RECOVERY-007 / P0:** discovery source is integrated; representative Windows/runtime installed-game proof remains before DONE.
- **RECOVERY-005 / P1:** dark ComboBox source/tests are integrated; installed Windows/WPF interaction acceptance remains an evidence gap.

## Verification boundary

v8.8.63 closed exact source `1090cdde27f979c672d633d9bb513b0c04874e4a` passed Workflow Feature PR Gate 36881182866, Heaven Toolbox Ownership Gate 36881181872, and MHW Product Security Gate 36881181751 before PR #555 merged as `29bcc6fe3c1ac3bb81091f9ba5f02e4e56b9e094`.

Do not describe v8.8.64 as integrated until PR #561 passes all required gates on one exact final head and merges. Source-gate success does not substitute for installed Windows/WPF visual acceptance.

## Coordination

The Heaven Bridge HMAC boundary remains intact: unsigned ChatGPT dispatch was rejected with `AUTH_REQUIRED`. Global Agent Control/Heaven/plugin work stays in `fengie/heaven-toolbox`. Preserve active issue/branch ownership and do not recreate generic tooling in MHW.
