# Mod Catalog Provider Research — 2026-09-29

Re-verify these external contracts before shipping an adapter.

## Nexus Mods

Nexus Mods API v3 is the current API direction. The published OpenAPI schema uses production base `https://api.nexusmods.com/v3`, supports API-key and Bearer JWT authentication, and identifies v1 as legacy.

References:
- https://github.com/Nexus-Mods/Vortex/blob/master/packages/nexus-api-v3/schema/openapi.yaml
- https://github.com/Nexus-Mods
- https://help.nexusmods.com/article/114-api-acceptable-use-policy

Decision: Nexus integration is API-first. No Nexus HTML scraping. The abandoned PR #208 implementation used v1 and is not restored as the long-term transport.

## Vortex

Vortex API is the extension API for the Vortex desktop application, not a remote mod catalog service. The old standalone `Nexus-Mods/vortex-api` repository is archived; current type definitions ship from Vortex as `@nexusmods/vortex-api`.

References:
- https://github.com/Nexus-Mods/vortex-api
- https://github.com/Nexus-Mods/Vortex/tree/master/packages/vortex-api

Decision: Vortex belongs in an optional interoperability lane, not the provider discovery backend.

## GameBanana

GameBanana publishes an API portal at `https://api.gamebanana.com/` with Core/Item/Data and Core/List operations.

References:
- https://api.gamebanana.com/
- https://api.gamebanana.com/docs/endpoints/Core/List/New
- https://api.gamebanana.com/docs/endpoints/Core/Item/Data

Decision: API-first, with fixtures and compatibility guards for any newer structured endpoint shapes.

## mod.io

mod.io provides an official REST API for games, mods, and modfiles. Read-only flows use an issued API key; authenticated operations use access tokens. Some binary URLs are short-lived.

References:
- https://docs.mod.io/restapi/introduction
- https://docs.mod.io/restapi/docs/get-mods
- https://docs.mod.io/restapi/docs/get-modfile

Decision: never persist expiring binary URLs as durable source identity.

## Thunderstore

Thunderstore is an open-source mod database with a REST API; its repository documents Swagger and package-list endpoints.

Reference:
- https://github.com/thunderstore-io/Thunderstore

## CurseForge

CurseForge publishes an official REST API for mod search/details/files and requires an `x-api-key`.

References:
- https://docs.curseforge.com/rest-api/
- https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key

Decision: official API only. Third-party access requires an approved API key and acceptance of the provider's API terms. The adapter therefore stays disabled unless explicit credentials/game mapping are configured; there is no HTML fallback.

## GitHub Releases

GitHub's REST API exposes release metadata and assets.

Reference:
- https://docs.github.com/en/rest/releases/releases

Decision: use for curated/linked repositories, not unbounded guessing that arbitrary repositories are mods.

## GitLab Releases

GitLab exposes a project Releases API with release lists and assets/links.

Reference:
- https://docs.gitlab.com/api/releases/

## Steam Workshop

Steamworks documents Workshop query/detail interfaces; credential requirements vary by operation and some server-side methods require publisher credentials.

References:
- https://partner.steamgames.com/doc/webapi/IPublishedFileService
- https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage

Decision: game/capability gated. The current Monster Hunter: World Steam Community surface for app 582010 exposes discussions/screenshots/artwork/guides rather than a Workshop catalog, so MHW does not register a Workshop adapter. Re-evaluate only when another supported game has a real Workshop capability.

Current MHW reference:
- https://steamcommunity.com/app/582010/

## Mod DB

Mod DB explicitly offers RSS feeds for releases, downloads, addons, and per-game/mod content and requests attribution. Its terms also restrict obtaining data to means the service intends to make available.

References:
- https://www.moddb.com/rss
- https://www.moddb.com/terms-of-use

Decision: feed-first. Arbitrary HTML scraping is disabled by default and requires a distinct permission/terms review.

## Scraping conclusion

The catalog is a federated source system, not a "scrape everything" system. Scraping is the last source class after official APIs, supported integrations, and feeds. Every crawler-enabled provider needs a checked-in compliance manifest, rate limiter, cache policy, deterministic parser fixtures, schema-drift failure mode, and kill switch.
