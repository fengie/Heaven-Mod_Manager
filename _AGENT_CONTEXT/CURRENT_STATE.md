# v8.8.67 provider-aware Browse Mods search — candidate state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

## Current MHW product work

- **BROWSE-556 / P1:** ACTIVE for installed-client closure. PR #563's v8.8.66 packaged selector UI Automation source gate is integrated on canonical main; publication/installed-client selector evidence remains before closure unless newer evidence supersedes this state.
- **BROWSE-557 / P1:** DONE in v8.8.64 via PR #561.
- **CATALOG-SCALE-558 / P1:** ACTIVE. v8.8.67 candidate adds explicit provider-backed text search only for configured providers advertising the `Search` capability while preserving cache-only debounce behavior. Broader pagination/browse/scale work remains open.
- **BROWSE-UX-559 / P1:** READY. Filters, sorting, provider health, and richer discovery states remain separate follow-up work.
- **RECOVERY-005 / P1:** source/test dark ComboBox work is integrated; installed WPF interaction evidence remains relevant.
- **RECOVERY-007 / P0:** installed-game discovery source is integrated; representative Windows/runtime proof remains.

## Candidate behavior

Typing in Browse Mods continues to filter the local FTS cache. The explicit Search command now queries only configured providers that advertise `CatalogProviderCapabilities.Search`, syncs returned rows through the existing cache/provenance boundary, isolates remote failures, then displays matching cached results. Nexus Mods and GameBanana are not probed for unsupported full-catalog text search.

## Verification boundary

The immediate integrated parent is v8.8.66 PR #563 exact head `e47d0a91e966b5bcd86e69825af5d77970fdb500`, which passed Workflow Feature `36966157999`, Product Security `36966158024`, and Toolbox Ownership `36966158016`, then merged as `5459db663f55388e72da97a79cb6e22ff0673048`.

An earlier #558 PR #564 reached a green reconciled pre-#563 head `117c17b9dff6535bf939dd8b65ef54892f6592e9`, but canonical main advanced and took v8.8.66. Those old greens are not inherited. v8.8.67 is a new exact-head verification boundary.

## Coordination

Issue #558 remains owned through Toolbox fallback claim `chatgpt-catalog-scale-558-20261002-0441`. The signed heaven2 HMAC boundary was not weakened. A delegated Heaven Codex attempt hit account quota plus workspace/network blockers; do not repeat that lane until durable blocker evidence changes.
