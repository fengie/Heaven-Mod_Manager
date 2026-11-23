# Game adapter SDK

`IGameAdapter` in Core describes discovery, executable location, mod roots, save locations, Nexus domain, asset semantics, categories, dependency requirements, and validation. Adapters do not deploy files. The planner, CAS, write-ahead journal, recovery, and rollback remain shared.

Built-ins are `FolderGameAdapter`, `MonsterHunterWorldAdapter` and staging-only `CrimsonDesertGameAdapter`. Unknown IDs fall back to generic classification, which is **not** proof of a writable mod root. `ConflictEngine` and `DeploymentPlanner` resolve semantic capabilities through `GameAdapters.Resolve`; the workflow screen displays adapter information and validation. The existing game profile registry still owns configured game paths and workspace isolation.

To add a compiled adapter:

1. Reference `MhwModManager.Core` from a separate assembly.
2. Implement `IGameAdapter`, or derive from `FolderGameAdapter` for folder-based games.
3. Register one instance with `GameAdapters.Register(adapter)` during startup, before services are created.
4. Set the game profile's `AdapterId` to the same ID.
5. Test asset classification, relative paths, and generic transactional deploy/undo with a synthetic game directory.

Registration rejects duplicate IDs. Registration is explicit host code; there is no automatic loading of downloaded DLLs. MHW conflict semantics must only be enabled for adapters compatible with MHW asset rules. A Stellar Blade or Wilds adapter should supply its own game knowledge while leaving that capability false unless its files actually use MHW semantics.

Discovery receives candidate install roots from the host; adapters should not scan an entire drive or write into a game directory. Dependency checks should return evidence instead of installing anything. Save paths are optional and should remain empty when unknown.

This is the first SDK contract, not a promise of complete Stellar Blade/Wilds semantic adapters. External discovery/category/dependency hooks are exposed for host integration; existing game-specific automation services remain intact.

## Direct-file deployment capability (candidate #972)

An adapter answers **three independent questions**: which game owns a package, what its files mean, and whether direct writes are actually supported. Importing, detecting an executable, or recognizing a `.paz` file must never imply the third.

The optional `IGameDirectFileDeploymentPolicy` interface provides `EvaluateDirectFileDeployment(GameProfile)`, returning an `Allowed`, stable `Code` and human-readable `Reason`. `IGameAdapter` remains unchanged for source compatibility. The shared `GameAdapters.DirectFileAdmission(profile)` is consumed by `CatalogService.EnsureCapturedAsync`, `ModScanner.CaptureAsync` and `DeploymentPlanner.Build`. All three must agree on the same game profile, including legacy profiles. A registered custom adapter without this optional policy must remain staging-only.

- Built-in MHW and configured folder-loader profiles keep their existing transactional direct-file behavior.
- Automatic discovery with adapter ID `generic-root` and **no** mod root stages only. A guessed executable folder is not an authorized install destination.
- Crimson Desert is staging-only regardless of `meta` presence, DLC, loader directory name, or a cached/preindexed package. Only a separately accepted, build-bound runtime backend can change that.
- The explicit `generic-folder` profile remains compatible with existing manually configured workflows; it is not a claim that arbitrary binary layouts are safe.
- A blocked game remains visible in the library and review UI. Nothing is silently copied into its live game folder, and no ad hoc alternative executor should bypass admission.
- A future non-file backend (VFS/runtime mount) must declare a **different** capability and be verified against its own version/loader lifecycle. Do not change `Allowed` to true merely because a third-party author says a format exists.

A reusable capability does **not** imply interchangeable binary patch semantics. Continue to use MHW-specific structural analysis only for MHW; Unreal container companions stay atomic; BepInEx/ASI code and bootstrap conflicts require human review; Crimson declarative field impacts are advisory until compiled by a verified loader.

Cross-game matrix, two-way design feedback and acceptance gaps: [2026-11-23 comparative standard](research/GAME-ADAPTER-BIDIRECTIONAL-STANDARD-2026-11-23.md).

## Optional Heaven.gg game research links

The desktop UI's **Dashboard → More tools → Open Heaven.gg Game Hub**
command calls `HeavenGameHubCatalog.FindGuideAsync`. On explicit user
request it fetches the public, versioned
`https://fengie.github.io/Heaven.gg/games.json` with an 8-second
timeout, no cookies, automatic redirects disabled and a 16 KiB response
limit. The parser recognizes only the three exact published game IDs,
including `monster-hunter-world`, and validates the entire fixed v1
catalog, every relative route and the canonical public Mod Manager
release/source origin before returning an HTTPS browser URL.

If the public catalog is unavailable or its contract changes, no URL
is opened and existing local game selection, mod files, paths, database,
updater transactions and installed game profiles remain unchanged.
Only the locally selected or active game is considered. The website's
research listings never trigger `GameAdapters.Register`, import, discovery,
GameProfileRegistry mutations or game deployment. A user may still add
a game manually through the existing UI. This is a **read-only user-facing
integration**, not automatic shared game-data ingestion, background sync or
installed/released Mod Manager acceptance proof.

The public game registry is owned and verified by
`fengie/Heaven.gg`. It is not a credential, executable
dependency, native mod classification signal or new updater feed.
