# Safety and Destructive Operations

Applies to deletion, replacement, overwrite, migration, cleanup, uninstall, rollback, rename, move, cache pruning, reconciliation, and similar state-changing operations.

## Before mutation
Prove, as appropriate:
- normalized target identity;
- allowed scope/root;
- ownership or authority to mutate;
- physical containment, not merely lexical path containment;
- expected current state/preconditions;
- backup/recovery source;
- whether aliases, links, mounts, reparse points, or redirects can escape scope.

If safety state cannot be proven, fail closed.

## Physical vs lexical safety
String normalization and prefix checks prevent many traversal bugs but do not prove where the filesystem object resolves. Define a link/reparse/mount policy and test it on supported platforms.

## Validate before the mutation you authorize
A safety check that runs only after the forbidden side effect can occur is not fail-closed. Identify the first externally visible create/write/delete/rename and prove its required ownership, containment, and topology checks happen before it. For nested path creation, validate existing components before descending or creating deeper components; assert absence of forbidden side effects in regression tests, not merely that an exception was thrown.

## Native and external operations
A failed native/system call does not necessarily mean no state changed. Model documented partial-failure postconditions and inspect actual resulting state before cleanup or rollback.

## Mutation ownership scope
A single-writer guard must be visible to every supported process, account/session, or host that can mutate the protected target. Name/namespace scope is part of the safety proof: for example, Windows `Local\` kernel objects are session-scoped. Fail closed if required ownership cannot be established, and test both contention and abandoned-owner recovery.

## Atomicity
Prefer same-filesystem staging where atomic rename/replace semantics require it, transactional metadata changes, commit-on-success publication, and recovery journals when file and database state cross atomicity boundaries. Do not claim atomicity where the platform does not provide it.

## Backups and rollback
Backups must be identifiable, scoped, integrity-checked when needed, and retained until success is durable. Rollback must itself have explicit failure behavior.

## Crash recovery
Design recovery for the states that can actually exist after interruption. Recovery must be idempotent or explicitly detect non-retryable ambiguity.

Durable `in progress` state describes protocol state, not writer liveness. Before recovery rolls back, replays, resets, or otherwise takes ownership of an incomplete operation, establish exclusive mutation ownership or prove the previous writer is gone. Database busy/transaction locks protect individual database operations; they do not automatically serialize a larger filesystem-plus-database state machine across processes. Test both live-peer exclusion and abandoned-owner takeover.

## Migration
A restartable migration must prove ownership before deleting/resetting destination state, validate reused artifacts rather than trusting existence, distinguish cleanup attempted from cleanup completed, converge on retry or emit a precise recovery action, and preserve the authoritative source until completion is proven.

Content-addressed or otherwise immutable destination data must also have an explicit ownership model after migration. A one-time digest check does not make the destination immutable if its bytes remain hardlinked or otherwise aliased to a pathname that another owner can still modify. Prefer independently owned verified bytes, or prove and enforce immutability across every retained alias for the object's lifetime.

## Publication
Do work in a catalog-invisible or otherwise non-public staging area. Make visibility an explicit commit-on-success step. Cleanup is not a publication guarantee because cleanup can fail or be skipped by process death.

## Resource and cancellation boundaries
An outer cancellation token or metadata preflight does not bound long-running inner I/O by itself. Pass cancellation into the actual read/write/decompression loop, meter the resources actually consumed there, and fail before the next write would cross the operation budget. Declared sizes, compression ratios, and free-space checks are useful preflight signals but are not substitutes for stream-level enforcement. A cancellation regression should interrupt work mid-unit and assert the operation does not report success or continue to full mutation afterward.

## Identity retirement
Deleting a row/object is not enough when semantic references survive outside formal foreign keys. Inventory live references, define history-retention policy, and test identity reuse.

## Verification
Test success, failure before mutation, failure after partial mutation, cancellation, restart, retry, concurrent access, stale state, unsafe topology, and recovery failure for high-risk operations.
