# v8.8.64 rich Browse Mods — integrated MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

PR #561 is integrated on canonical `main`. Exact verified head `3ced8b41041909d91b06902631ef36085bfd7489` passed all three required gates and merged as `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`.

## Current MHW product work

- **BROWSE-556 / P1:** DONE. The header game selector uses an explicit `DisplayName` item template instead of rendering raw `GameProfile` text.
- **BROWSE-557 / P1:** DONE. Browse Mods has safe HTTPS artwork, richer result metadata, selected-mod artwork/context, recycled row virtualization, and focused regressions.
- **CATALOG-SCALE-558 / P1:** ACTIVE. v8.8.64 integrated the first capacity tranche (100 items/provider request limit and 1000 visible cached results); deeper provider-aware pagination/search remains open.
- **BROWSE-UX-559 / P1:** READY. Filters, sorting, provider-health and richer discovery/loading/empty/partial-failure states remain follow-up work.
- **SECURITY-554 / P0:** DONE in v8.8.63 via PR #555 after all required exact-head gates passed.
- **RECOVERY-002 / P0:** DONE for v8.8.62 catalog browsing/acquisition + CurseForge recovery.
- **Issue #281:** remains open only for real provider contracts such as conditional Steam Workshop support, optional Vortex interoperability, and future provider-specific adapters with compliance evidence.
- **RECOVERY-007 / P0:** discovery source is integrated; representative Windows/runtime installed-game proof remains before DONE.
- **RECOVERY-005 / P1:** dark ComboBox source/tests are integrated; installed Windows/WPF interaction acceptance remains an evidence gap.

## Verification boundary

Exact v8.8.64 PR #561 head `3ced8b41041909d91b06902631ef36085bfd7489` passed Workflow Feature PR Gate 36895834301, MHW Product Security Gate 36895834289, and Heaven Toolbox Ownership Gate 36895834269. It merged as `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`.

Fresh-main reconciliation preserved newer v8.8.63 hosted/updater evidence and verification caches. Source-gate success still does not substitute for installed Windows/WPF visual acceptance of live remote thumbnails and the repaired selector.

## Coordination

The Heaven Bridge HMAC boundary remains intact: unsigned ChatGPT dispatch was rejected with `AUTH_REQUIRED`. Global Agent Control/Heaven/plugin work stays in `fengie/heaven-toolbox`. Future #558/#559 agents must claim non-overlapping scopes before mutation.
