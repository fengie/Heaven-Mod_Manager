# Visual metadata and update model

The v8.6.24 visual layer follows the same broad separation used by Vortex/Nexus integrations: remote Nexus artwork is metadata, cached locally for UI use, while install/deployment state remains separate. Current Nexus v3 schemas expose mod `thumbnail_url` and other image URL fields, and Vortex documentation describes choosing Nexus-hosted art at runtime with local fallbacks. Vortex also treats update detection separately from installing an update.

Sources consulted during implementation:
- https://github.com/Nexus-Mods/Vortex/blob/master/docs/game-art-assets.md
- https://github.com/Nexus-Mods/Vortex/blob/master/packages/nexus-api-v3/schema/openapi.yaml
- https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Users-UI-Mods-section
- https://github.com/nexus-mods/vortex/blob/master/packages/vortex-api/docs/EVENTS.md

Policy used here:
1. Prefer cached Nexus main art when a known Nexus identity exists.
2. Also index local screenshots and FOMOD `<Image>` references.
3. Never modify or move source images.
4. Decode thumbnails defensively.
5. Automatically detect update availability, but require the user to choose/install a new mod version so transactional deployment and conflict review are preserved.
