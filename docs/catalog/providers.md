# Catalog Provider Matrix

Research date: 2026-09-29.

| Source | Preferred integration | Authentication | Discovery | Acquisition | HTML scraping | Priority |
|---|---|---|---|---|---|---|
| Nexus Mods | Nexus Mods API v3 | API key / Bearer per provider contract | Yes | Authorized direct or assisted | No | P0 |
| Vortex | Explicit handoff v1 + extension/runtime identity | None required by this app | Not a standalone remote catalog | Metadata/profile handoff only | No | Interop (implemented for MHW) |
| GameBanana | Official API | Provider-specific | Yes | Official file/source links | Normally unnecessary | P1 |
| GitHub Releases | REST Releases API | Public/GitHub auth as needed | Curated/linked repos | Release assets | No | P1 |
| mod.io | Official REST API | API key; access token for authenticated flows | Yes | Official/dynamic modfile URLs | No | P1/P2 |
| Thunderstore | Public REST API | Provider-specific | Community/package | Package URL | No | P2 |
| CurseForge | Official REST API | x-api-key | Yes | Approved file/download APIs | No by default | P2 |
| GitLab Releases | Releases API | Public/PAT as needed | Curated/linked projects | Release assets/links | No | P2 |
| Steam Workshop | Steamworks APIs | Operation-specific Steam credentials | Supported games only | Workshop/game-managed | No | P2; disabled for MHW until a reviewed per-game contract exists |
| Mod DB | Official RSS first | Public | Releases/downloads/addons feeds | Assisted/source flow | Disabled by default | P2/P3 |
| User feed/manifest | RSS/Atom/JSON manifest | Optional | Explicit configured source | Explicit source | N/A | P2/P3 |

See `PROJECT_PLAN.md`, `provider-research-2026-09-29.md`, and `provider-policy.md`.


## Interoperability notes

- Vortex handoff v1 is documented in `vortex-interop.md` and does not make Vortex a source adapter.
- The MHW mapping is explicit and test-covered. Other game profiles fail closed until they receive their own reviewed mapping.
- A Steam app ID is installation/store metadata, not proof of Steam Workshop capability. MHW therefore exposes no Workshop adapter.
