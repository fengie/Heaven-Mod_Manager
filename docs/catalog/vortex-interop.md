# Vortex interoperability handoff v1

Updated: 2026-10-02.

Vortex interoperability is an **optional handoff boundary**, not a catalog provider. The manager never reads Vortex private Redux state, cookies, Nexus sessions, deployment folders, or authenticated download state. Normal Browse Mods behavior remains provider-neutral and works without Vortex installed.

## Current Monster Hunter: World identity

The reviewed MHW compatibility mapping is:

- Vortex game ID: `monsterhunterworld`
- Nexus page ID: `monsterhunterworld`
- Steam app ID: `582010`
- managed mod path: `nativePC`
- executable: `MonsterHunterWorld.exe`

These values agree with the public Vortex Monster Hunter: World game extension and this repository's canonical `GameProfile.MonsterHunterWorld` adapter.

References:

- current Vortex extension API: https://github.com/Nexus-Mods/Vortex/tree/master/packages/vortex-api
- historical/public MHW Vortex game extension: https://github.com/Nexus-Mods/vortex-games/tree/master/game-monster-hunter-world
- Vortex MHW guide: https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Users-GameGuides-Modding-Monster-Hunter-World-with-Vortex

The old `vortex-games` repository is archived; it is used only as evidence for the stable MHW identity contract, not as a runtime dependency.

## Handoff file

The app reads and writes `*.vortexhandoff.json` using
[`vortex-handoff-v1.schema.json`](../schemas/vortex-handoff-v1.schema.json).

The file contains only:

- game identity;
- local package identity and display name;
- desired enabled state and priority;
- optional Nexus mod+file identity;
- optional captured relative-path SHA-256 values.

It does **not** contain generic source URLs, mod archives, cookies, API keys, bearer tokens, signed download URLs, Vortex state databases, or deployment instructions.

Import is fail-closed:

1. the game contract must exactly match the current MHW workspace;
2. IDs must be unique and file paths must pass normal managed-path validation;
3. hashes, when present, must match the already-imported local package;
4. Nexus fallback matching requires the exact mod ID **and** exact file ID;
5. ambiguous, missing, or hash-mismatched entries remain disabled;
6. importing creates a saved profile only; it never writes to the live game tree.

A Vortex extension or external tool can implement this schema without receiving access to this application's internals. Likewise, this app can export a handoff without requiring Vortex to be running.

## Steam Workshop applicability

Steam installation identity is **not** a Steam Workshop capability signal. Monster Hunter: World has Steam app ID `582010`, but this project has no reviewed operation-specific Workshop discovery/acquisition contract for MHW. Therefore no Workshop catalog adapter is registered for MHW and the contract reports Workshop as unsupported.

A future game profile may enable Workshop only after its supported Steam interface, authentication requirements, item/game mapping, acquisition ownership, rate behavior, and safe handoff into the existing import boundary are documented and tested. Never infer Workshop support solely from `SteamAppId`.
