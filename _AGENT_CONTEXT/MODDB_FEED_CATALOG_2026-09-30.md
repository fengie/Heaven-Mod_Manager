# Mod DB feed catalog checkpoint — 2026-09-30

## Scope

This checkpoint implements the planned RSS/Atom + Mod DB feed foundation from `docs/catalog/PROJECT_PLAN.md` without broadening into HTML crawling, direct-download bypass, or archive mirroring.

## Research / policy boundary

- Mod DB currently publishes RSS 2.0 feeds and explicitly encourages use with attribution.
- The adapter uses the official `rss.moddb.com` HTTPS feed host only.
- Mod DB feed discovery is **browser-assisted acquisition only**. Feed entries may link to provider pages, but the adapter does not scrape those pages, manufacture direct archive URLs, bypass interstitial/account/download flows, or treat feed links as archive payloads.
- Required attribution is recorded as `Mod DB - Game Development`.
- Generic Atom parsing is included so the feed transport is provider-neutral and reusable, but the Mod DB provider itself remains host-allowlisted.

## Implementation

Production source initially landed through PR #386 / merge `559299767d26c4c20983f7e606dd8df4c786dd3a`:

- `src/MhwModManager.Core/SyndicationFeed.cs`
  - RSS 2.0 + Atom parser;
  - DTD/external entity processing disabled;
  - HTTPS-only entry/source links;
  - bounded response reads and text normalization;
  - explicit XML media-type allowlist;
  - cancellation propagation;
  - 429 / Retry-After surfaced without automatic retry loops.
- `src/MhwModManager.Core/ModDbFeedCatalogPolicy.cs`
  - feed-only compliance record;
  - direct download and HTML parsing disabled;
  - attribution required.
- `src/MhwModManager.Core/ModDbFeedCatalogProvider.cs`
  - explicit `rss.moddb.com` source allowlist;
  - deterministic feed-entry identity;
  - browse/search/metadata normalization;
  - browser-assisted-only acquisition;
  - health mapping for rate limiting, malformed feeds, refusal, and offline behavior.

Regression coverage added immediately afterward:

- `tests/MhwModManager.Tests/SyndicationFeedTests.cs`
- `tests/MhwModManager.Tests/ModDbFeedCatalogProviderTests.cs`
- `tests/MhwModManager.Tests/Fixtures/Catalog/ModDb/downloads-rss.xml`
- `tests/MhwModManager.Tests/Fixtures/Catalog/Syndication/atom.xml`
- test-project fixture copy rules for both XML fixture directories.

Coverage includes RSS and Atom normalization, HTML-text sanitization, DTD/XXE rejection, HTTPS-only policy, payload bounds, content-type rejection, cancellation, 429/no-retry behavior, official-host enforcement, required attribution, wrong-game rejection, health mapping, and proof that acquisition remains assisted rather than direct.

## Coordination incident / prevention precedent

A concurrent branch-cleanup/integration lane merged the task branch as PR #386 immediately after the production files landed, before the planned tests had been committed. This did **not** justify treating the feature as verified. The missing tests/fixtures were added to `main` immediately and an exact-SHA Windows verification was queued.

General prevention rule: automation that harvests or merges another agent's active branch must not infer readiness merely from branch existence or a compilable-looking diff. Integration requires an explicit ready/ownership signal **and** the task's required verification evidence. If another agent owns the lane, branch cleanup must preserve it until that signal or a terminal owner state is present.

## Verification state

The older exact-main Heaven verification at `f0643a8ce4dbd791ecdaf2a78f77a866ca5c4c9d` failed on two unrelated verifier regressions that were subsequently repaired on `main`:

1. invalid PowerShell interpolation using `"$relative: ..."` in `Test-CiSecurityPolicy.ps1`;
2. integration tests still resolving moved verification scripts from obsolete paths.

A fresh isolated Heaven worktree job, `job-20260930T040400Z-moddb-current-main-gate`, is queued to:
1. run the full unit-test project;
2. run the CI security policy;
3. run `scripts/release/Verify-Release.ps1` on one exact fetched `origin/main` SHA.

Do not call this feed tranche release-verified until that exact job is green. If `main` advances afterward, preserve the exact-SHA scope of the evidence.

## Remaining catalog work

This checkpoint is feed infrastructure/provider support, not completion of the whole federated catalog project. Remaining issue #281 work still includes additional provider adapters, permitted crawler infrastructure, Vortex interoperability, integrated catalog UI, exact installed-origin update flow, and provider-wide acceptance/evidence as applicable.

Preserve the repository continuity constitution and pass this checkpoint to successor agents without weakening the assisted-acquisition or verification boundaries.
