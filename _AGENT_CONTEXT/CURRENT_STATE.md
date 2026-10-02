# v8.8.69 provider-aware Browse Mods search — candidate state

Issue #556 is DONE. v8.8.68 passed exact-head PR gates and packaged installed-client acceptance; durable E2E evidence is committed on canonical main at `601496093430214791395ba1bfcfadd3b0262ad2`.

Issue #558 remains ACTIVE. Stale PR #565 contains useful provider-search semantics but cannot be merged directly after main advanced through v8.8.68, so this branch replays only the semantic product/test delta on fresh main and moves the tranche to v8.8.69.

## Candidate behavior

- Typing in Browse Mods remains a local cache/FTS filter.
- Explicit Search queries only configured providers advertising `CatalogProviderCapabilities.Search`.
- Provider results flow through `CatalogSyncService` and the existing source-aware cache/provenance boundary.
- Provider failures are isolated; cached matches remain usable.
- Nexus Mods and GameBanana are not probed for unsupported full-catalog text search.
- UI copy explains the distinction between cached filtering and explicit provider search.

## Verification boundary

PR #565 head `a03e2a73e806c3b49dacf382c2061255fc79521a` previously passed Workflow Feature `36967009248`, MHW Product Security `36967009165`, and Heaven Toolbox Ownership `36967009322`. Those results are evidence for the semantic implementation only; they do not transfer to v8.8.69 after reconciliation.

The v8.8.69 branch must pass all three required gates on one exact final head before integration.

## Routing

The current ChatGPT route exposes GitHub mutation but no callable Agent Control / Heaven Bridge dispatch surface. No local-agent verification is claimed, and no authentication boundary was weakened.
