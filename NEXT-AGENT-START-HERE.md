# v8.8.69 catalog-scale candidate — #558 provider search + cache capacity

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Working branch: `fix/issue558-provider-search-v8.8.69-20261002`
Parent canonical main: `3291b4c9270c4cb2a4beac2900ce724dd00ef0e6`
Active issue: #558
Superseded semantic source: PR #565 / `agent/issue-558-provider-search-v8.8.67-20261002`

## Verified parent boundary

Issue #556 is closed on v8.8.68. Preserve its exact selector contract:

- source merge `7def58c1b16d115e1555738ebad51717d1c1f752`;
- hosted Windows verification run `36978710736` passed 26/26;
- installed-client E2E `36979261045` passed update, peer-level selected DisplayName, enabled Switch, enabled Settings, and rollback;
- `ActiveGameSelector`, `AutomationProperties.ItemStatus={Binding SelectedGame.DisplayName}`, `TextSearch.TextPath="DisplayName"`, the visual ItemTemplate regression, and character ellipsis must not regress.

## v8.8.69 candidate

This branch reconciles only still-useful #558 semantics from stale PR #565 onto the verified v8.8.68 line and fixes the review-discovered storage/UI capacity mismatch.

- Ordinary query typing remains local to SQLite/FTS.
- Explicit **Search** contacts only configured providers advertising `CatalogProviderCapabilities.Search`.
- Provider failures remain isolated and successful remote rows flow through `CatalogSyncService` into the normal source-aware cache before display.
- Unsupported Nexus/GameBanana full-catalog search is not probed or invented.
- `CatalogRepository.SearchAsync` now allows the UI's existing 1,000-row virtualized window instead of truncating at 500.
- A behavioral integration regression inserts and retrieves 600 matching cached rows, proving rows beyond 500 are reachable.
- Product/version metadata is v8.8.69.

## Exact-head completion boundary

1. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final v8.8.69 head.
2. Repair only concrete failures; do not weaken provider capability gating, cache/provenance identity, or the v8.8.68 selector contract.
3. Refresh current `main` immediately before integration. If main moved, reconcile first and rerun exact-head gates.
4. Merge only the exact green candidate and verify remote `main` readback.
5. Close/supersede stale PR #565 after its unique semantics are confirmed integrated.
6. Keep #558 open: default installs still need broader provider-aware discovery/pagination because Nexus/GameBanana do not expose supported full-catalog text search and CurseForge Search is configuration-dependent.
7. Continue deterministic scale/performance coverage before moving to #559 discovery UX.

## Routing and safety

Use current `fengie/heaven-toolbox@main` as the global bootstrap source. Do not weaken Heaven/Agent Control authentication. If local execution is unavailable from the current chat, use the authorized GitHub fallback and record only verification actually produced.

## Successor obligation

Read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` plus task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Refresh live main/issues/PRs before mutation. **Do not break the chain.**
