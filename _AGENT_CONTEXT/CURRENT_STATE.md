# v8.8.69 provider-search + cache-capacity candidate

Issue #556 is **DONE** on verified v8.8.68. Issue #558 is the active catalog-scale lane.

## Verified parent

PR #567 merged source `7def58c1b16d115e1555738ebad51717d1c1f752`. Hosted Windows verification `36978710736` passed 26/26 and installed-client E2E `36979261045` passed real update, selected game DisplayName on the ComboBox peer, enabled Switch/Settings, and rollback. Preserve that selector boundary.

## Current candidate

Branch: `fix/issue558-provider-search-v8.8.69-20261002`

The stale PR #565 provider-search semantics have been reconciled onto post-#556 main without importing stale v8.8.67 metadata.

- per-keystroke Browse Mods filtering remains local to SQLite/FTS;
- explicit Search queries only configured providers whose capabilities include `CatalogProviderCapabilities.Search`;
- provider failures are isolated and successful results are persisted through the existing catalog sync/cache path;
- unsupported Nexus/GameBanana full-catalog search is not probed;
- the repository search ceiling is aligned with `CatalogVisibleResultLimit = 1000` instead of silently clamping to 500;
- integration coverage proves 600 matching cached rows can be returned through `SearchAsync(... limit: 1000)`.

## Verification boundary

No v8.8.69 green claim exists until Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership all pass on one exact final head. Refresh `main` immediately before integration and merge only that exact green head.

## Remaining #558 scope

This tranche does not make a default MHW install magically searchable across every remote catalog. The currently configured default providers remain Nexus + GameBanana, neither of which advertises supported full-catalog text Search; CurseForge Search is active only when its required configuration is supplied. #558 remains open for provider-aware discovery/pagination and additional deterministic scale/performance work.

