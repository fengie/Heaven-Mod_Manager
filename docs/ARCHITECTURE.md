# Universal game architecture

The application is now organized around a game-agnostic transactional manager plus an active `GameProfile`. Generic profiles provide executable/process identity, a managed mod target, optional Nexus domain/save path, and isolated state. Enhanced adapters may add semantic asset rules; Monster Hunter: World is currently the deepest enhanced adapter.

Generic profiles deliberately use conservative exact-path conflict behavior. Game-specific rules must not leak into generic mode. All deployment still uses the same content-addressed blobs, journal, manifest, recovery, dry-run and rollback pipeline.

Store discovery (Steam/Epic/GOG) is convenience only. Manual executable selection is the universal fallback.

# Architecture — v8.1 Hardened

## Invariants

The production core is designed to be deterministic, idempotent where applicable, transactional, crash-consistent, fail-closed for uncertain structural data, incremental, indexed, bounded-concurrency, cancellation-safe, observable, and testable. Performance fixes may not weaken those invariants.

## Layers

- **App** — WPF/MVVM composition. No deployment or directory traversal belongs in code-behind.
- **Core** — immutable domain records, conflict classification/indexes, precedence graph, provider selection, planner.
- **Storage** — SQLite schema/migrations, WAL, connection-per-operation access, profiles, v7 migration.
- **Filesystem** — streaming hash/CAS, source scanning, secure archive handling, Restart Manager integration, atomic replacement, transaction executor/recovery.
- **Mhw** — game discovery, armor catalog/model knowledge, process safety.
- **Diagnostics** — structured logging, correlation IDs, exception policy, health/support bundle and runtime telemetry.

## Persistence

SQLite lives at `State\Next\manager.db`; content blobs live under `State\Next\Blobs`. `State\V2` remains legacy/fallback input and is never reused as the mutable v8 database.

SQLite uses WAL, `synchronous=FULL`, foreign keys, a busy timeout, bounded WAL/checkpoint settings, and short-lived pooled connections. A single global mutable `SqliteConnection` is intentionally avoided.

## Planning

Planning receives a stable `PlannerSnapshot`. Only staged-enabled mod-file rows need to be materialized for normal conflict analysis. Files are indexed once into a case-insensitive `path -> providers` map. Conflict rules are indexed once into mod-pair overlay and sparse incompatibility structures. Planner change construction uses path dictionaries rather than repeated linear searches.

Automatic resolution order remains conservative:

1. explicit incompatibility;
2. exact-file winner;
3. identical bytes;
4. shared namespace provider;
5. remembered overlay;
6. high-confidence family/patch inference;
7. texture priority;
8. uncertain subset blocker;
9. hard structural/game/plugin/unknown blocker.

## Deployment protocol and commit point

1. Recover any earlier incomplete transaction.
2. Reject blocked plan.
3. Capture first-takeover unmanaged baselines into CAS without mutating the live tree.
4. Durably write the operation and all per-file journal rows in one SQLite transaction (`Prepared`).
5. Validate **all** live preconditions before the first filesystem mutation.
6. Mark `Applying`.
7. For each path: revalidate its precondition immediately before write, materialize a same-directory temp, flush, atomically replace/move, then mark the journal row applied.
8. Mark `FilesWritten`, then `StateCommitting`.
9. In **one SQLite transaction**, update deployment manifest, original-file ownership, optional mod enabled/priority state, and the operation state `Committed`.
10. Return success only after that transaction commits.

A crash before step 9 is recovered to the before-image. A crash after step 9 is already committed and is not rolled back. Recovery never overwrites an external file that matches neither the journal's before nor after hash; it enters `RecoveryRequired` instead.

## External changes and watchers

`deployment_manifest.expected_live_sha256` plus actual live observations are authoritative. FileSystemWatcher exists only to coalesce invalidation hints for UX. Missing, duplicated, reordered, delayed, or overflowed watcher events cannot make a deployment correct or incorrect.

## Hashing/cache model

SHA-256 is authoritative blob/integrity identity. Size+mtime are cheap filters only. A potential metadata cache hit is verified by an XXH3 pass before reusing the old SHA-256/CAS entry, preventing same-size/same-timestamp edits from becoming stale deployments.

## UI responsiveness

No substantial filesystem enumeration, archive work, hashing, planner computation, migration, or health scan should run synchronously on the WPF Dispatcher. Large collection replacements are batched and DataGrid row/column virtualization/container recycling stay enabled. Search debounce only refreshes the in-memory view; it does not rebuild conflicts.

## Diagnostics

Every substantial operation can have a correlation ID and timing/runtime counters. A Dispatcher watchdog records stalls without adding unbounded heartbeat work. Diagnostic/support paths are deliberately non-authoritative: telemetry failure may reduce observability but may never corrupt a deployment.

## Incremental function verification (v8.8)

Source verification is intentionally outside the runtime domain model. `tools/MhwModManager.FunctionVerifier` parses `src` with the Roslyn parser shipped by the pinned SDK and maintains `.verification/function-status.json`. Unchanged exact fingerprints with `verified=true` are reused; changed/new functions are re-opened for verification and must have a method-entry master trace. Cache promotion is a final commit step that occurs only after the full build/test/self-test pipeline passes. A failed run cannot overwrite the previous known-good function baseline.

The runtime master trace also carries an async-flow scope chain. First-chance exceptions mark all active scopes as having observed an error, producing `PASS-CHECK` or `ERROR-CHECK` diagnostics at scope completion without swallowing exceptions or changing transactional behavior.
