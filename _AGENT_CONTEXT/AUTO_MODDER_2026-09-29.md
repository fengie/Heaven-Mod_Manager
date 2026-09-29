# Auto Modder planning checkpoint — 2026-09-29

Status: **PLANNING ONLY — no shipped Auto Modder implementation yet**

The user requested that the repository begin planning a future Auto Modder / Mod Builder feature where supported modding operations are automated and the user mainly supplies meaningful values or IDs.

Canonical product/architecture plan:

- `docs/AUTO-MODDER-PLAN.md`
- GitHub tracking epic: #201 — **[Epic] Auto Modder / Mod Builder**
- Initial planning commit: `9514be4b20baddd07d35492312c5a4b7edb462bf`

## Core decisions preserved for future agents

- Auto Modder is recipe-driven rather than a collection of hard-coded mod generators.
- Auto Mod Recipes are declarative data and may not run arbitrary scripts/processes/network/filesystem operations.
- A typed patch-plan intermediate representation is the central mutation boundary.
- Executable format adapters are a separate higher-trust boundary.
- Builds occur inside a manager-owned sandbox and publish a normal mod package.
- Existing library, conflict analysis, staging, deployment transaction, rollback, and Undo remain authoritative.
- Human-readable entity catalogs should sit in front of raw MHW IDs whenever reliable mappings are available.
- The first real implementation should support one well-understood MHW format and prove adapter reuse with at least two recipes before broadening scope.
- Reverse-recognition / Build Variant is a later milestone.
- No copyrighted game assets should be committed as test fixtures.
- Core shipped behavior belongs in the application. Optional reusable authoring/catalog/SDK tooling may use the canonical `plugins/` workspace without duplicating app core logic.

## Implementation status

No production source, UI, application version, release metadata, or packaging behavior was changed by this planning checkpoint. Documentation-only planning is exempt from an application version bump under repository policy.

Future implementation agents should read `docs/AUTO-MODDER-PLAN.md` and issue #201, inspect current `main`, then take the smallest independently verifiable milestone rather than attempting the entire epic in one branch.
