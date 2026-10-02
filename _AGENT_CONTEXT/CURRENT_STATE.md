# v8.8.66 provider-aware Browse Mods search — candidate

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

## Current MHW product work

- **BROWSE-556 / P1:** ACTIVE for runtime acceptance. v8.8.65 source/test fix is integrated via PR #562; installed Windows/WPF visual proof is still required before closing.
- **BROWSE-557 / P1:** DONE in v8.8.64 via PR #561.
- **CATALOG-SCALE-558 / P1:** ACTIVE. v8.8.66 candidate adds explicit provider-backed text search only for providers advertising the `Search` capability while preserving cache-only debounce behavior. Broader pagination/browse expansion remains open.
- **BROWSE-UX-559 / P1:** READY. Filters, sorting, provider-health and richer discovery states remain separate follow-up work.
- **RECOVERY-005 / P1:** source/test dark ComboBox work is integrated; installed WPF interaction acceptance remains an evidence gap.
- **RECOVERY-007 / P0:** installed-game discovery source is integrated; representative Windows/runtime proof remains.

## Candidate behavior

The Browse Mods search box continues to filter the local FTS cache while the user types. The explicit Search command now queries only configured providers that advertise `CatalogProviderCapabilities.Search`, syncs those results through the existing cache/provenance boundary, isolates provider failures, then displays matching cached rows. Nexus Mods and GameBanana are not probed for unsupported full-catalog search.

## Verification boundary

The latest **closed** source boundary remains v8.8.65 PR #562 exact head `236d3604f1df245f9c224eda3f21f2177979cfd2`, which passed Workflow Feature `36963458484`, Product Security `36963458384`, and Toolbox Ownership `36963458431`, then merged as `c3f238cbe4f1850763736bac999af0577a4606c5`.

v8.8.66 is a new production/source candidate and is not verified until its own required exact-head gates pass.

## Coordination

Issue #558 is owned in the Toolbox fallback collaboration store by claim `chatgpt-catalog-scale-558-20261002-0441`. The heaven2 direct control boundary remains HMAC-protected and was not weakened. A delegated Heaven Codex attempt hit an account usage limit plus workspace/network blockers; do not repeat that same lane until durable blocker evidence changes.
