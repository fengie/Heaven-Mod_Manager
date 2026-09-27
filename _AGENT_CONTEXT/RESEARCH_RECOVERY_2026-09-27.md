# Research checkpoint 2: migration, profiles, crash diagnosis

Researched 2026-09-27. Source audit only; no production changes or new executed tests. Existing evidence remains scoped to the commits in CURRENT_REVISION.json.

## Sources and applicable conclusions

- SQLite atomic commit: https://www.sqlite.org/atomiccommit.html — transaction atomicity covers database changes. Its detailed rollback-journal sequence does not describe WAL mode. Our deployed payload files are outside SQLite; application recovery remains essential.
- SQLite WAL: https://www.sqlite.org/wal.html — read this when evaluating checkpoint/backup behavior. A live database cannot safely be treated as just the main database file with its WAL ignored.
- SQLite foreign keys: https://www.sqlite.org/foreignkeys.html — enforcement must be enabled for each connection; child relationships and deletion actions matter when journaling reversible edits.
- Microsoft.Data.Sqlite transactions: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions — deferred transactions may need a whole-transaction retry when upgrading a read transaction to a write transaction under contention. Do not blindly retry filesystem side effects alongside SQL.
- Andreas Zeller's executable delta-debugging chapter: https://www.debuggingbook.org/html/DeltaDebugger.html — describes 1-minimal reduction and PASS/FAIL/UNRESOLVED outcomes. A failure different from the original target need not count as reproduction. This supports distinguishing launch infrastructure failures from the user's target crash.

## Confirmed implementation boundaries

ManagerDatabase initializes WAL and synchronous=FULL and enables foreign_keys when opening connections. Verify effective pragmas on the actual connection used by a new backup or migration path; do not assume database-level and connection-level pragmas have identical persistence.

DeploymentExecutor.ApplyWithMetadataAsync recovers incomplete operations, captures state, prepares and journals before mutation, checks all file preconditions and each file immediately before writing, then commits manifest/state/metadata/Committed marker together. Rollback uses CancellationToken.None deliberately. This means a user cancellation must not disable restoration. Do not replace this with early returns after a failed copy.

DeploymentMetadata uses allowlisted tables/columns, expected-before comparison, an operation_metadata journal, and reverse-order inversion. UpdateMigrationService explicitly journals removal of profile_rules links before deleting their rules, allowing inverse restoration in dependency order. Metadata is not an arbitrary SQL scripting API.

UpgradeAsync recomputes a preview immediately before applying. Preview reads planner state and several metadata tables through separate calls/connections; the WPF gate protects app-local actions, but does not by itself prove a coherent snapshot against another process. Existing expected-before checks cover edited rows. Review what detects changes to previously unrelated/new rows and planner inputs before declaring concurrent edits fully covered.

The migration inherits enabled state, priority and family; it does not rewrite all saved profile_mods rows or map FOMOD choices between versions. A saved profile that names the retired mod needs explicit review when reapplied. Document policy rather than silently claiming all profiles were upgraded.

## Existing tests worth extending, not duplicating

AutomationTests/WorkflowTests.cs contains:

- MigrationCommitAndUndoRestoreMetadataAndFilesTogether.
- MigrationCrashRecoveryKeepsFilesAndMetadataOnSameSideOfCommit: faults after-file-write and before-db-commit restore old state; after-db-commit retains new state.
- MigrationStaleMetadataRollsBackFilesWithoutOverwritingExternalEdit: modifies replacement family after preview, then confirms rollback preserves that edit.
- ProfilesStoreDeltasAndFollowParentEdits and ProfileCycleIsRejectedWithoutLosingPriorState.
- Pair-interaction crash reduction and cancellation tests.

These are strong backend checks, but simulated exception injection is not a physical power-loss test or real-game acceptance. Do not rerun the whole suite for this documentation change.

## Proposed recovery fault matrix

| Scenario | Required observation | Why this adds coverage |
|---|---|---|
| Crash after durable journal, before first write | Recovery leaves bytes, ownership and metadata unchanged | Empty application window |
| Crash after files-written/state-committing transition | Old bytes and metadata restored | Wider state-machine coverage |
| Recovery invoked twice | Second recovery makes no new destructive changes | Idempotence |
| Undo after a third-party rule/family edit | Refuse stale restoration or report recoverable conflict; preserve external edit | Inverse optimistic check |
| Undo crash at the existing three injection points | Consistent pre/post-undo files and metadata | Inverse journal durability |
| Missing or corrupt CAS before-image | RecoveryRequired with diagnostic evidence; no fabricated success | Recovery resource failure |
| Live-file edit between preview/apply or during rollback | Preserve current safety policy and identify the affected path | External ownership |
| New rule added after snapshot, while unchanged rows still match | Replan/reject or explicitly document serialization assumption | Phantom-row/planner freshness question |
| Rule removed during migration with profile link | Undo restores both rule and link in FK-safe order | Multi-table inversion |

These are proposed tests; first inspect existing IntegrationTests to avoid duplicating cases there. Use a disposable game root and owned fixtures. Never exercise crash injection on the user's live installation.

## Profile semantics and optimization opportunities

LoadAsync resolves the parent chain in one transaction. SaveAsync stores deltas and inserts explicit disabled entries for inherited mods omitted from a complete saved state. A child therefore follows later parent edits only where it has no override. Test parent enable/disable and priority changes, explicit child disables, reparenting, parent deletion restriction, missing mod IDs, and the 128-level depth boundary. Explain live inheritance to users; it is not a frozen snapshot.

ListAsync resolves each profile separately within a transaction, repeatedly traversing shared ancestors. This is source-confirmed repeated work, not a measured bottleneck. Benchmark many children sharing a deep parent chain before changing it. A transaction-scoped memo of resolved ancestors could help, but avoid mutable dictionary sharing between children. Preserve cycle detection and consistent snapshots.

## Crash diagnosis follow-up

CrashBisectorEngine caches boolean probe results, checks a passing empty baseline and failing full set, then reduces subsets and complements. Its guarantee is conditional on a reproducible probe. A 1-minimal set need not be the smallest possible set or the only failing combination. Current wording already distinguishes joint suspects from individually confirmed culprits.

Recommended next contract: an explicit outcome plus reason, target crash signature, elapsed time and environment fingerprint. Separate target failure, pass, unsupported launch/dependency state, timeout, cancellation and inconclusive outcome. Decide a bounded retry policy for intermittency; record attempts rather than caching a single flaky observation as truth. Retain process cleanup and original-state restoration even on cancellation.

Suggested fixtures: three-way interaction; two independent failing pairs; baseline failure; full set fails to reproduce; dependency-invalid subset; different exception/signature; alternating probe outcomes; cancellation during apply/launch/recovery. Never convert every inability to start the game into proof that the current mod combination caused the target crash.

## Next action

Reproduce and fix the Inbox cases from research batch 1 first. Then choose a missing recovery test based on concrete risk and existing coverage. Keep these findings separate from verified defects. Push at each meaningful checkpoint and pass this obligation to the next agent.
