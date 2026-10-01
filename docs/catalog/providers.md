# Catalog Provider Matrix

Research date: 2026-09-29. Implementation status refreshed 2026-09-30.

| Source | Preferred integration | Authentication | Discovery | Acquisition | HTML scraping | Priority |
|---|---|---|---|---|---|---|
| Nexus Mods | Nexus Mods API v3 | API key / Bearer per provider contract | Yes | Authorized direct or assisted | No | P0 |
| Vortex | Extension/runtime API | Vortex runtime | Not a standalone remote catalog | Interop only | No | Interop |
| GameBanana | Official API | Provider-specific | Yes | Official file/source links | Normally unnecessary | P1 |
| GitHub Releases | REST Releases API | Public/GitHub auth as needed | Curated/linked repos | Release assets | No | P1 |
| mod.io | Official REST API | API key; access token for authenticated flows | Yes | Official/dynamic modfile URLs | No | P1/P2 |
| Thunderstore | Public REST API | Provider-specific | Community/package | Package URL | No | P2 |
| CurseForge | Official REST API | x-api-key | Yes | Approved file/download APIs | No by default | P2 |
| GitLab Releases | Releases API | Public/PAT as needed | Curated/linked projects | Release assets/links | No | P2 |
| Steam Workshop | Steamworks APIs | Operation-specific Steam credentials | Supported games only | Workshop/game-managed | No | P2 |
| Mod DB | Official RSS first | Public | Releases/downloads/addons feeds | Assisted/source flow | Disabled by default | P2/P3 |
| User feed/manifest | RSS/Atom/JSON manifest | Optional | Explicit configured source | Explicit source | N/A | P2/P3 |

See `PROJECT_PLAN.md`, `provider-research-2026-09-29.md`, and `provider-policy.md`.


## Current Monster Hunter: World status

- **Nexus Mods + GameBanana:** composed into Browse Mods by default for the MHW profile.
- **CurseForge:** adapter implemented; composed only when both `MOD_MANAGER_CURSEFORGE_API_KEY` and `MOD_MANAGER_CURSEFORGE_GAME_ID` are supplied. The repository does not guess a game ID or persist the API key/signed URLs.
- **Steam Workshop:** not applicable to the current MHW Steam app 582010; its Steam Community surface has no Workshop catalog. No inert or scraping-based adapter is registered.
- **HTML/crawlers:** framework exists but all domains are disabled unless an explicit current terms/robots manifest and path allowlist are provided.
- **Vortex:** optional interop is local metadata/sidecar compatibility, not a remote provider or authenticated-session reuse.
- **Installed-origin updates:** the Browse Mods page can check persisted provider/mod/file origins exactly; missing identities fail closed and no fuzzy replacement file is selected.
