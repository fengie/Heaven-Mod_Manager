# Architecture snapshot

The application is a modular .NET/WPF monolith. The backend is deliberately separated into projects rather than putting behavior in WPF.

```text
WPF App
  -> Automation / Diagnostics / Filesystem / Mhw
       -> Core
Storage ------------------------------> Core
Filesystem ---------------------------> Core + Storage
Mhw ----------------------------------> Core
```

The intended direction remains: generic transactional mod-management engine, game-profile-driven behavior, and MHW-enhanced compatibility where Monster Hunter World needs deeper semantic knowledge.

## Strong backend path

The deployment path is roughly:

```text
profile/mod state
 -> ConflictEngine
 -> DeploymentPlanner / immutable-ish plan snapshot
 -> DeploymentExecutor
 -> precondition/hash validation
 -> durable SQLite operation journal
 -> CAS-backed filesystem mutation
 -> manifest/state transaction
 -> commit / rollback / restart recovery
```

The source includes WAL-backed SQLite state, content-addressed blobs, before/after hashes, operation journals, transactional manual-family changes, and recovery logic. Preserve those safety properties.

## Universal-game transition

v8.7.0 introduced real game-profile support rather than only renaming MHW concepts. `GameProfiles.cs` and `GameProfileRegistry.cs` thread game identity/path semantics through scanner, planner/conflicts, Nexus metadata, health/automation, process guarding, and workspace/database selection. Some deliberate MHW-specific branches remain in Core/Filesystem for enhanced semantics and legacy behavior.

## v8.8 verification layer

`tools/MhwModManager.FunctionVerifier` inventories production executable bodies under `src`, fingerprints Roslyn tokens without trivia, and emits per-body booleans. The persistent cache is `.verification/function-status.json`.

Known-good resolution order:

1. exact current ID + fingerprint + `verified=true` in the promoted cache;
2. exact unchanged v8.7 source file hash;
3. matching function/body ID + fingerprint parsed from the read-only v8.7 source snapshot;
4. otherwise `changed-or-new` and verification is required.

Changed/new executable bodies require an entry `MasterDebugLog.BeginMethod()` scope, except the tracing implementation inside `MasterDebugLog` itself to prevent recursive instrumentation.

Runtime `MasterDebugLog.OperationScope` now tracks first-chance exceptions for the active async-flow call chain. It reports clean completion or observed call errors without swallowing exceptions.

## Diagnostic hot-path rule

Per-function scopes remain the mechanism for associating nested exceptions with callers. `AppDomain.FirstChanceException` is used as an observation signal only; it must not change exception semantics. Full stack logging for every first-chance exception is deliberately disabled by default and can be enabled with `MHW_FIRST_CHANCE_DETAIL=1` for deep diagnostics. This preserves exhaustive observation without forcing heavy file I/O on every handled exception.
