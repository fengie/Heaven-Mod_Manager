# Catalog Provider Matrix

Research date: 2026-09-29.

| Source | Preferred integration | Authentication | Discovery | Acquisition | HTML scraping | Priority |
|---|---|---|---|---|---|---|
| Nexus Mods | Nexus Mods API v3 | API key / Bearer per provider contract | Yes | Authorized direct or assisted | No | P0 |
| Vortex | Extension/runtime API | Vortex runtime | Not a standalone remote catalog | Interop only | No | Interop |
| GameBanana | Official API | Provider-specific | Yes | Official file/source links | Normally unnecessary | P1 |
| GitHub Releases | REST Releases API | Public/GitHub auth as needed | Curated/linked repos | Release assets | No | P1 |
| mod.io | Official REST API | API key; access token for authenticated flows | Yes | Official/dynamic modfile URLs | No | P1/P2 |
| Thunderstore | Public REST API (adapter implemented; compliance-gated) | Public read API | Community/package | Provider-generated package URL | No | P2; disabled pending current Terms reference |
| CurseForge | Official REST API | x-api-key | Yes | Approved file/download APIs | No by default | P2 |
| GitLab Releases | Releases API | Public/PAT as needed | Curated/linked projects | Release assets/links | No | P2 |
| Steam Workshop | Steamworks APIs | Operation-specific Steam credentials | Supported games only | Workshop/game-managed | No | P2 |
| Mod DB | Official RSS first | Public | Releases/downloads/addons feeds | Assisted/source flow | Disabled by default | P2/P3 |
| User feed/manifest | RSS/Atom/JSON manifest | Optional | Explicit configured source | Explicit source | N/A | P2/P3 |

See `PROJECT_PLAN.md`, `provider-research-2026-09-29.md`, and `provider-policy.md`.
