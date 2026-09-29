# Game adapter SDK

`IGameAdapter` in Core describes discovery, executable location, mod roots, save locations, Nexus domain, asset semantics, categories, dependency requirements, and validation. Adapters do not deploy files. The planner, CAS, write-ahead journal, recovery, and rollback remain shared.

Built-ins are `FolderGameAdapter` and `MonsterHunterWorldAdapter`. Unknown IDs fall back to conservative generic semantics. `ConflictEngine` and `DeploymentPlanner` resolve semantic capabilities through `GameAdapters.Resolve`; the workflow screen displays adapter information and validation. The existing game profile registry still owns configured game paths and workspace isolation.

To add a compiled adapter:

1. Reference `MhwModManager.Core` from a separate assembly.
2. Implement `IGameAdapter`, or derive from `FolderGameAdapter` for folder-based games.
3. Register one instance with `GameAdapters.Register(adapter)` during startup, before services are created.
4. Set the game profile's `AdapterId` to the same ID.
5. Test asset classification, relative paths, and generic transactional deploy/undo with a synthetic game directory.

Registration rejects duplicate IDs. Registration is explicit host code; there is no automatic loading of downloaded DLLs. MHW conflict semantics must only be enabled for adapters compatible with MHW asset rules. A Stellar Blade or Wilds adapter should supply its own game knowledge while leaving that capability false unless its files actually use MHW semantics.

Discovery receives candidate install roots from the host; adapters should not scan an entire drive or write into a game directory. Dependency checks should return evidence instead of installing anything. Save paths are optional and should remain empty when unknown.

This is the first SDK contract, not a promise of complete Stellar Blade/Wilds semantic adapters. External discovery/category/dependency hooks are exposed for host integration; existing game-specific automation services remain intact.
