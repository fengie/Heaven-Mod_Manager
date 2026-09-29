# Catalog Providers

## Current support

| Provider | API | Authentication | Catalog access | Direct download | Rate limits | Scraping allowed/used? | Implementation |
|---|---|---|---|---|---|---|---|
| Nexus Mods | Official Nexus API | Personal API key for development/personal use; registered application flow required before broad public release | Yes, within exposed API feeds/endpoints | Only when the authenticated account/request is permitted by Nexus | Provider-enforced; current published limit is 20,000 requests/24h, then 500/hour | Nexus HTML scraping is not used | `NexusCatalogProvider` |

Research date: 2026-09-29.

Official policy references:
- https://help.nexusmods.com/article/114-api-acceptable-use-policy
- https://help.nexusmods.com/article/105-i-have-reached-a-daily-or-hourly-limit-api-requests-have-been-consumed-rate-limit-exceeded-what-does-this-mean
- https://help.nexusmods.com/article/18-terms-of-service

## Planned providers

ModDB, Thunderstore, CurseForge where applicable, GitHub Releases, GitLab Releases, and game-specific repositories must each receive a fresh compliance/API review before implementation. Do not infer capabilities from Nexus. Every adapter must declare a capability manifest through `CatalogProviderCapabilities`.
