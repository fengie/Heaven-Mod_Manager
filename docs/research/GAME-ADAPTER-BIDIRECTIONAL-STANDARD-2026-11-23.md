# Multi-game adapter standard — Crimson Desert ↔ existing games (2026-11-23)

**Owner:** fengie/Heaven-Mod_Manager · **source baseline:** main@3899c7cb, Crimson feature PR #973 · **status:** candidate design/implementation; gameplay adoption not proven. This document refines [RESEARCH-MULTIGAME.md](RESEARCH-MULTIGAME.md) and the [adapter SDK](../GAME-ADAPTER-SDK.md) rather than replacing the existing workflow.

## Common lifecycle

The user's goal is an unstructured inbox that safely becomes a useful, recoverable mod library. Every game should pass through the same six *manager-owned* phases:

1. **Identify installation.** Scoped Steam/Epic/GOG/Xbox metadata and selected executable; canonical parent game root and game identity, no whole-drive crawl.
2. **Identify package.** Bounded ZIP/RAR/7z/loose folder inventory; distinguish container distribution, authored mod boundaries, installer variants, dependencies and metadata.
3. **Preserve evidence.** Keep original source bytes and digest, explain uncertain identity, reject unsafe traversal/reparse paths, preserve independent candidates even when one candidate needs review.
4. **Classify effects.** Exact destination paths are a baseline; enhanced adapters may provide distinct semantic keys. A semantic key isn't evidence a game's binary table or loader is compatible.
5. **Admit deployment separately.** Stage/import and metadata recognition do not authorize direct game writes. An optional, version-aware adapter policy explicitly says whether *direct-file deployment* is supported; otherwise hold, even for preindexed files. A future virtual mount must have a different, independently verified capability contract.
6. **Apply and restore.** For admitted direct-file games, use existing planner, CAS, journaling, provenance, collision review, and Undo. Never let loader detection or source name override these guarantees.

## Compare and contrast (observed owner code)

| Game or profile | Known intake/discovery evidence | Target and conflict semantics | Direct deployment admission | Transferable lesson |
|---|---|---|---|---|
| MHW (`mhw`) | executable, `nativePC`, armor asset structure and main/optional relationships | `nativePC`, exact-file + proven outfit/model semantics | Existing transactional direct-file path; retain | Rich game semantics must be opt-in, never inferred from file extensions |
| Unreal Paks / Stellar Blade (`unreal-paks`) | project-qualified `Content/Paks`, `.pak/.sig/.utoc/.ucas` companions | Known Paks root, package-atomic companions, exact-path decisions | Existing file deployment, subject to loader path checks | Never split incomplete containers or strip nested mount prefixes |
| BepInEx / other folder loaders | installed `BepInEx/plugins` or other configured mod directory | DLL/scripts under one recognized root, exact-path conflicts and bootstrap safety | Existing direct-file transaction | Loader root and executable code require separate trust from archive identity |
| Discovered unknown (`generic-root` with empty mod root) | only an executable; no verified writable loader root | Former fallback targets the entire game root | **New safe default: staging-only** | An install root is not automatically a mod root |
| Crimson Desert | `bin64/CrimsonDesert.exe`, `meta/0.papgt` and `0.paver`, DMM JSON intents, PAZ/PAMT pairs | Current game uses build-dependent archive/table paths, field-level declarative claims, ASI/VFS backend | **Staging-only until real build-bound backend proof** | Split *content comprehension* from *runtime installation authorization* |

`GameAdapters.Resolve` remains the canonical identity/semantic resolver. A new optional `IGameDirectFileDeploymentPolicy` leaves existing `IGameAdapter` implementations source-compatible while requiring third-party adapters to explicitly assert their direct-file contract. `GameAdapters.DirectFileAdmission` is shared by catalog capture, scanner and planner; it prevents admitting a failed import through stale database content. The established generic-folder profile with an explicitly selected root retains compatibility. The *inferred* `generic-root` fallback is held, because its destination was not verified.

## Bidirectional feedback applied

**Existing games → Crimson**

- Adopt atomic package boundaries from Unreal container triplets, but do **not** treat a PAZ/PAMT pair as a verified loader installable.
- Reuse MHW family/base/optional and exclusive variants without pretending name or shared archive means a mod family.
- Reuse generic filesystem scope, original-source hashes, copy budgets, no-reparse/ZIP traversal, idempotent receipts and safe Undo.
- Reuse BepInEx bootstrap and code-bearing trust rules when evaluating ASI/DLL. Do not silently install competing runtimes.

**Crimson → all games**

- Separate static discovery and *import staging* from explicit deployment authorization. No detected executable alone implies a writable destination.
- A version-specific table or virtual-asset operation is an **advisory footprint**. Simple distinct record fields may avoid false positive conflicts, but a record-wide operation conflicts with any edit on the same record; these operations are not auto-merged into binaries.
- Apply fail-closed build/loader proof, dependency and add-on checks wherever an adapter gains version-sensitive semantics.
- Scope detection of third-party JSON to recognized formats: `settings.json` and other arbitrary files must not claim another game's library solely from a `game` field.
- Convert observed failures into cross-game regression permutations before broadening the default resolver.

## Verification matrix

- **Direct admission:** MHW nativePC, Unreal project Paks, BepInEx plugins and explicit Mods folder remain admitted; inferred `generic-root` holds; newly detected Crimson, legacy generic Crimson and incomplete Crimson installations hold.
- **Effect permutations:** same target field/different value → conflict; different field or different record → no synthetic collision; record-wide edit + field of same record → review; same asset vpath from different mods → review; unknown operation/schema → staging review.
- **Import permutations:** deeply nested independent archives; packages containing multiple options; malformed/ambiguous JSON; conflicting declared game identity; path collisions; duplicate hashes; known executable with no loader; partial/cancel/retry; source preserved, no game writes on review.
- **Performance:** retain baseline and exact-candidate p50/p95 and bounded resource budgets; don't infer speedups from improved algorithmic structure alone.
- **Acceptance boundary:** native Windows feature/integration/startup/WPF gates for exact SHA, independent authority and main verification; separate physical installed-gaming version and runtime-backend proof. PR-source green ≠ deployed working mods.

## Remaining intentionally game-specific work

Crimson v3/V3.1 parsing currently inspects *declarative* table/asset/localization claims; it is **not** a PAZ/PAMT compiler. The DMM 5.1 [author guide](https://www.nexusmods.com/crimsondesert/articles/65) documents arbitrary JSON/JSONC names, table operations and DLC metadata. The [DMM mod guide](https://www.nexusmods.com/crimsondesert/articles/64) describes runtime ASI overlays on 2.04+ and rejects raw numbered PAZ/PAMT mod archives. No compatible installed runtime, actual game version, or ownership of external loader binaries was verified for this product. Do not rewrite `meta/0.papgt`, game archives, saves, launchers or unrelated mod manager state.

**Do not build another generic backend or new orchestrator now.** Preserve the narrow shared admission seam and test matrices; only add new adapters, loader integrations and semantic resolvers after proving their existing owner and game-version contract. See #972, #791 and #946 for remaining bulk corpus, installed WPF and real-game acceptance.
