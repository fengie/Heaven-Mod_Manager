# Federated Mod Catalog Project Plan

Research baseline: 2026-09-29. Implementation status refreshed 2026-09-30.

## Objective

Build a provider-neutral mod catalog that can discover, search, inspect, acquire, and update mods from multiple legitimate sources without turning the application into a third-party archive mirror or coupling the UI to one vendor.

The catalog is an acquisition/discovery layer. Existing archive inspection, FOMOD handling, local-library registration, deployment planning, conflict resolution, rollback, and live-game safety remain the authoritative install/apply boundaries.

## Architecture

```text
official APIs / feeds / permitted structured pages / interop
        |
        v
source adapter -> raw response envelope -> normalizer -> provenance
        |                                      |
        +-> rate/compliance state              v
                                      source-aware cache
                                               |
                                               v
                                      local FTS/search index
                                               |
                                               v
                                 identity/linkage + dedup layer
                                               |
                                               v
                                   provider-neutral catalog UI
                                               |
                                               v
                           acquisition resolver (direct/assisted)
                                               |
                                               v
                       existing safe archive/FOMOD import pipeline
```

Provider-specific payloads must not leak into generic UI/application contracts.

## Provider roadmap

### P0 — Nexus Mods API v3

Use the official Nexus Mods API v3/OpenAPI surface as the primary Nexus integration. The prior experimental catalog branch used legacy v1; do not restore that as the long-term transport.

- API-first; no Nexus HTML scraping.
- Support provider-approved API-key and/or bearer authentication.
- Normalize games, mod details, files, categories, images, changelog/version metadata, collections where useful, rate state, and allowed download-link resolution.
- Preserve assisted download when direct acquisition is not permitted.

### P1 — GameBanana

Use GameBanana's published API surface and validated structured API endpoints.

Target metadata: game, mod identity, author, version, timestamps, stats, images, files, hashes when exposed, source page, and legitimate file URLs.

### P1 — GitHub Releases

Use the official GitHub Releases API for explicitly linked or curated mod repositories. GitHub is not a game-wide discovery engine by default.

Target metadata: repository identity, release/tag/version, assets, checksums when publisher-provided, and update lineage.

### P1/P2 — mod.io

Use the official mod.io REST API.

- API key for supported read-only access.
- Access tokens only for provider-supported authenticated flows.
- Preserve platform/portal semantics.
- Treat expiring binary URLs as acquisition data, never durable identity.

### P2 — Thunderstore

Use Thunderstore's public REST package API for relevant communities. Normalize packages, versions, dependencies, downloads, and lineage.

### P2 — CurseForge

Implemented through the official CurseForge REST API. The provider is enabled only when the operator supplies both `MOD_MANAGER_CURSEFORGE_API_KEY` and `MOD_MANAGER_CURSEFORGE_GAME_ID`; missing or partial configuration fails closed. The API key is header-only and expiring/signed acquisition URLs are never stored as catalog provenance. No HTML fallback exists.

### P2 — GitLab Releases

Use the official GitLab Releases API for explicitly linked/curated projects.

### P2 — Steam Workshop

Enable only for games that actually use Workshop and only through supported Steam interfaces. Monster Hunter: World (Steam app 582010) has no Workshop catalog in its Steam Community surface, so no dead Workshop adapter is registered for the current MHW profile. A future supported game must provide explicit Workshop capability and operation-specific credentials before this adapter class becomes relevant.

### P2 — Mod DB feeds

Use Mod DB's official RSS feeds for releases/downloads/addons with attribution. Deeper automated extraction requires a separate terms/permission review.

### P3 — permitted structured-page/HTML adapters

`PermittedCatalogCrawler` provides the generic fail-closed boundary. Every domain remains disabled until it supplies an explicit compliance manifest with fresh terms/robots review, approved HTTPS origin/path prefixes, a local kill switch, bounded response size, and deterministic parser fixtures. The framework rejects origin/path escapes and non-HTML responses; it is not a license to scrape arbitrary providers.

### Interop — Vortex

Vortex API is an extension/runtime API, not a remote catalog backend.

Current optional interop:
- existing Nexus metadata ingestion accepts MO2/Vortex `meta.ini` / `vortex.meta.ini` sidecars and imported artwork without requiring Vortex to run;
- catalog exact-origin state uses the recovered provider/mod/file identity rather than a fuzzy title match.

Possible later interop:
- export compatible metadata;
- compatibility with additional Vortex game-extension concepts;
- optional supported handoff workflows.

Normal catalog browsing must not depend on Vortex running or reuse Vortex-authenticated sessions without an explicit supported contract.

## Compliance manifest

Every provider must have a checked-in compliance record containing:

- provider ID and source kind;
- official API/docs URL;
- terms URL and review date;
- robots URL/review date when crawling applies;
- allowed and forbidden operations;
- authentication/secret boundary;
- attribution requirements;
- rate-limit, Retry-After, concurrency, and backoff policy;
- cache/conditional-request policy;
- direct-download policy;
- compatibility verification date;
- kill switch / disabled reason.

Crawler-enabled adapters fail closed when required compliance information is missing or stale.

## Scraping rules

1. Prefer official API, application integration, RSS/Atom, or official structured feed.
2. Obey provider terms and robots directives.
3. Never bypass login requirements, premium/account restrictions, ads, required download pages, CAPTCHA, Cloudflare/anti-bot controls, signed-link protection, or rate limits.
4. Never impersonate browsers to defeat bot controls.
5. Identify the application honestly with a stable User-Agent.
6. Use ETag / If-None-Match and Last-Modified / If-Modified-Since when available.
7. Respect 429 and Retry-After; use per-provider concurrency caps and bounded exponential backoff.
8. Cache enough to avoid unnecessary repeated requests.
9. Parser drift disables the adapter rather than silently corrupting normalized data.
10. Do not mirror third-party archives.
11. Downloads must use provider-authorized direct URLs or assisted user flows.
12. Secrets, cookies, bearer tokens, API keys, and signed URLs never enter catalog rows, raw snapshots, logs, or diagnostics.

## Storage and local search

Move beyond one opaque JSON cache toward source-aware persistence:

- `catalog_sources`
- `catalog_items`
- `catalog_files`
- `catalog_provenance`
- `catalog_links`
- `catalog_sync_state`
- `catalog_rate_state`
- SQLite FTS5 over title, author, summary, tags, category, and optionally description.

Use stale-while-revalidate behavior so cached content remains browseable during provider outages.

## Identity and deduplication

Never auto-merge projects solely because names or authors look similar.

Strong linkage evidence, in descending confidence:

1. same provider + provider mod ID;
2. explicit cross-link from provider metadata;
3. same canonical repository/project URL declared by the author;
4. same trusted package/project identifier;
5. exact archive SHA-256 for the same released artifact.

Fuzzy similarity may produce a UI suggestion, never an automatic canonical merge. Preserve source-specific records and source badges.

## Sync model

Adapters declare supported modes:

- paged/cursor scan;
- updated-since;
- feed polling;
- release-list polling;
- on-demand detail hydration.

The scheduler must deduplicate identical in-flight requests, cap concurrency per provider, persist cursors only after successful commits, make cancellation safe, avoid retry storms, isolate failures, and expose freshness/health.

## Acquisition

Every source resolves to one of:

- `Direct`
- `AuthenticatedDirect`
- `Assisted`
- `Unavailable`

All acquired archives continue through the existing safe download validation -> archive inspection -> FOMOD/archive import -> staged local-mod pipeline. The catalog never writes directly to the live game directory.

## Delivery phases

### Phase 0 — foundation
- land this architecture/provider research;
- restore only useful provider-neutral pieces from the abandoned PR #208;
- add provider compliance model and validation;
- add deterministic provider fixtures;
- implement Nexus v3 transport boundary;
- verify focused tests before any broad UI restore.

### Phase 0 implementation status — 2026-09-29

Completed on the current-main lineage:
- provider-neutral domain and acquisition contracts;
- fail-closed provider compliance model and Nexus v3 compliance record;
- deterministic Nexus v3 response fixtures;
- HTTPS-only Nexus v3 transport boundary for trending, mod details, mod files, and mod-file versions;
- API-key (`apikey`) and Bearer authentication without secret-bearing diagnostics;
- bounded response reads, cancellation propagation, conditional cache validators, 304 handling, schema-envelope drift detection, and explicit 429/Retry-After surfacing without automatic retry storms.

Still required before Nexus is considered a supported catalog provider:
- [implemented] normalize Nexus v3 payloads into `CatalogMod` / `CatalogModFile` with fail-closed inner-schema validation;
- [implemented] hydrate the global mod id needed by file endpoints and expand exact file versions;
- [implemented] provider health/rate/auth state plus compliant assisted acquisition policy;
- [implemented] timeout/auth/offline/rate-limit/malformed-inner-schema provider fixtures;
- route acquired archives through the existing safe import boundary;
- persist source-aware cache/provenance and expose the provider through the integrated catalog UI.

### Phase 1 — useful federated catalog
- Nexus v3;
- GameBanana;
- curated GitHub Releases;
- source-aware SQLite cache + FTS5;
- source badges/filters;
- exact installed-origin update checks.

### Phase 2 — broaden official APIs
- mod.io;
- Thunderstore;
- CurseForge;
- GitLab Releases;
- Steam Workshop where relevant;
- dependency normalization.

### Phase 3 — feeds and explicitly permitted crawlers
- generic RSS/Atom adapter;
- Mod DB RSS;
- crawler compliance manifests and kill switches;
- approved structured-page adapters only where API/feed coverage is insufficient.

### Phase 4 — interoperability and advanced linkage
- [implemented baseline] optional Vortex/MO2 sidecar interoperability;
- explicit cross-source identity links;
- update lineage across linked sources;
- provider collections/packs where contracts allow.

## Acceptance criteria

A provider is not "supported" until:

- compliance manifest is complete;
- normalizer fixtures cover representative and malformed responses;
- timeout, cancellation, 429/backoff, auth failure, offline/stale cache, and schema-drift behavior are tested;
- secrets do not appear in logs/cache/DB;
- direct-download restrictions are respected;
- acquired archives use the existing safe import boundary;
- one provider failing cannot break others;
- provenance is persisted and visible;
- relevant Windows build/tests pass on the reconciled candidate.

## Execution notes

PR #208 was closed without merge and its task branch was deleted. This project starts from current canonical `main` and selectively recovers ideas/code only after current-state review; it will not resurrect the old branch wholesale.


### Integrated catalog status — 2026-09-30

The current implementation now includes:

- a recovered and reconciled **Browse Mods** split-pane UI over the source-aware SQLite/FTS cache, with provider filtering, local search/sort, provider health and source-page handoff;
- exact installed-origin inspection from persisted provider/mod/file identity, including explicit missing/changed states and a UI action that never guesses a replacement file;
- an official CurseForge REST adapter with API-key authentication, strict schema checks, bounded responses, 429/Retry-After health, deterministic fixtures, and ephemeral authorized download resolution;
- the generic permitted-crawler safety boundary described above;
- existing Vortex/MO2 sidecar metadata interoperability;
- Steam Workshop intentionally **not registered for Monster Hunter: World**, because the supported MHW Steam profile does not expose a Workshop catalog. This is a capability-gated non-applicability decision, not a scraping fallback.

External/provider credentials remain operator configuration, not repository secrets.
