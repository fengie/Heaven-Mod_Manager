# Multi-game architecture research — v8.7.0

The manager now follows the same broad architectural lesson used by mature mod managers: keep the transactional deployment/conflict engine game-agnostic, then supply a small game profile or adapter with discovery and path semantics.

## Research conclusions

- Vortex game extensions can be lightweight: game identity, executable/discovery, and mod path are enough for basic support; game-specific capabilities can be layered separately.
- Mod Organizer 2's Basic Games project follows the same principle for games that do not need a bespoke plugin.
- Vortex deployment tracks owned/deployed files and reconciles outside changes. Universal support must preserve that ownership/rollback model rather than become a blind recursive copy tool.
- Nexus API resources are scoped by game domain, so Nexus enrichment must come from the active game profile rather than a hard-coded MHW domain.
- BepInEx uses `BepInEx/plugins` as the common plugin location.
- Unreal mod packages commonly target a game's `Content/Paks` tree, often a `~mods` folder, with UE5 games also using `.utoc/.ucas` alongside `.pak`.
- Steam, Epic and GOG all expose enough local installation metadata for best-effort discovery, but manual executable selection remains the universal fallback.

## Resulting design

1. `GameProfile` is the canonical game descriptor.
2. Every game gets an isolated workspace/database/history.
3. Unknown games use conservative exact-path conflict semantics.
4. Known layouts (`BepInEx`, Unreal Paks, Data, Mods) only change target-path defaults; they do not silently claim semantic compatibility.
5. Enhanced adapters can add deeper knowledge. MHW remains the first enhanced adapter.
6. Manual game addition always remains available.

## Reference material

- Vortex games/extensions: https://github.com/Nexus-Mods/Vortex-Games
- Vortex API / game extension examples: https://github.com/Nexus-Mods/Vortex/wiki
- Mod Organizer 2 Basic Games: https://github.com/ModOrganizer2/modorganizer-basic_games
- Nexus Mods API: https://api-docs.nexusmods.com/
- BepInEx plugin loading: https://docs.bepinex.dev/
- Unreal pak modding conventions: Epic/Unreal documentation and common game mod layouts.
